using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 统一的悬停果冻反馈：鼠标移入弹一下（带 overshoot 与挤压拉伸），移出平滑复位。
//
// 命中检测两种来源，用一个组件覆盖：
//   UIEvent      —— 走 EventSystem 的 pointer enter/exit，事件驱动、不占 Update。
//   WorldBounds  —— 每帧用 sprite 的未缩放局部 bounds 判定，不加 Collider2D、
//                   不加 Physics2DRaycaster，对现有物理与射线零侵入。
//
// 动画由弹簧-阻尼积分驱动，而不是播放曲线或起协程：
//   打断 = 改目标值，位置与速度天然连续，不需要 StopCoroutine、不需要反解起点。
//   overshoot 来自阻尼比 < 1；降级档把阻尼比压到 1.0（无回弹）+ 提高刚度即可。
//
// 全程只写 localScale（外加对文本子节点的反向缩放），不改布局属性、不碰点击判定。
public class HoverJelly : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private enum HitSource
    {
        UIEvent = 0,
        WorldBounds = 1
    }

    [Header("Hit Test")]
    [SerializeField] private HitSource hitSource = HitSource.UIEvent;
    [SerializeField] private SpriteRenderer worldRenderer;
    [SerializeField] private Camera worldCamera;

    [Header("Jelly")]
    [SerializeField, Min(0.01f)] private float amplitude = 0.10f;        // 峰值 = 1 + amplitude
    [SerializeField, Range(0f, 1f)] private float squashRatio = 0.75f;   // Y 比 X 少涨多少（挤压感）
    [SerializeField, Range(0.05f, 0.5f)] private float settleTime = 0.22f;   // 移入收敛时间
    [SerializeField, Range(0.05f, 0.5f)] private float releaseTime = 0.16f;  // 移出复位时间
    [SerializeField, Range(0.1f, 1f)] private float damping = 0.32f;     // 阻尼比，越小回弹越明显
    [SerializeField, Range(0.5f, 1f)] private float verticalLag = 0.72f; // Y 弹簧滞后，制造瞬态挤压

    [Header("Text")]
    [Tooltip("对 TMP 文本子节点做反向缩放，让文字始终保持 1.0 采样不被拉伸糊掉。")]
    [SerializeField] private bool counterScaleText = true;

    private const float MaxStep = 1f / 120f;       // 低帧率下的积分步长上限，保证数值稳定
    private const float SettleTolerance = 0.02f;   // 收敛判定：相对幅度的 2%，对应 settleTime 的 2% 准则
    private const float MinEpsilon = 0.0001f;
    private const float DisabledFactor = 0.5f; // interactable=false 时幅度减半
    private const float PressedFactor = 0.8f;  // 世界物体被按住时的恒定缩放
    private const float ReducedAmplitudeFactor = 0.5f;
    private const float ReducedSettleTime = 0.05f;  // 临界阻尼下收敛比欠阻尼慢，取值要比目标时长更短

    private struct TextCounter
    {
        public Transform target;
        public Vector3 baseScale;
    }

    private readonly List<TextCounter> counters = new List<TextCounter>();

    private Button uiButton;
    private SpriteRenderer cachedRenderer;
    private Camera cachedCamera;
    private Vector3 baseScale = Vector3.one;
    private Vector3 rendererBaseScale = Vector3.one;

    private float x = 1f, y = 1f;
    private float velocityX, velocityY;
    private bool hovering, pressed;
    private bool settled = true;
    private bool lastInteractable = true;
    private bool reduced;

    private void Awake()
    {
        uiButton = GetComponent<Button>();
#if UNITY_EDITOR
        // UI 的 localScale 绕 pivot 缩放：pivot 落在角点时，果冻只会朝一个方向长，
        // 看起来像整体往右下/右上位移，而不是以中心向四边均匀挤压。这里提前报出来。
        if (uiButton != null && transform is RectTransform rect)
        {
            Vector2 offset = new Vector2(0.5f - rect.pivot.x, 0.5f - rect.pivot.y);
            if (offset.sqrMagnitude > 1e-6f)
            {
                Debug.LogWarning($"[HoverJelly] {name} 的 pivot 是 {rect.pivot}（不在中心），" +
                                 "果冻会单边外扩。可执行菜单 Tools/挂个爽/给所有 Button 挂悬停果冻 自动归中心。", this);
            }
        }
#endif
        cachedRenderer = worldRenderer != null ? worldRenderer : GetComponent<SpriteRenderer>();
        cachedCamera = worldCamera != null ? worldCamera : Camera.main;
        baseScale = transform.localScale;
        if (cachedRenderer != null) rendererBaseScale = cachedRenderer.transform.localScale;
        CollectTextCounters();
        reduced = HoverJellySettings.ReducedMotion;
        HoverJellySettings.Changed += OnSettingsChanged;
    }

    private void OnDestroy() => HoverJellySettings.Changed -= OnSettingsChanged;

    private void OnEnable()
    {
        // 重新挂载/面板重新打开时，从静止状态起步，避免残留上一次的缩放。
        ResetToRest();
    }

    private void OnDisable() => ResetToRest();

    private void Update()
    {
        if (hitSource == HitSource.WorldBounds) RefreshWorldHover();
        Step();
    }

    // ---- 外部驱动 ----------------------------------------------------------

    // 由拖拽类脚本（如 SpongeDrag）调用：按住时锁定一个恒定缩放，松手交还给 hover。
    public void SetPressed(bool value)
    {
        if (pressed == value) return;
        pressed = value;
        settled = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hitSource != HitSource.UIEvent) return;
        SetHovering(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hitSource != HitSource.UIEvent) return;
        SetHovering(false);
    }

    private void SetHovering(bool value)
    {
        if (hovering == value) return;
        hovering = value;
        settled = false;
    }

    // ---- 命中检测 ----------------------------------------------------------

    private void RefreshWorldHover()
    {
        if (!TryReadPointer(out Vector2 screen) || cachedCamera == null ||
            cachedRenderer == null || !cachedRenderer.enabled || cachedRenderer.sprite == null)
        {
            SetHovering(false);
            return;
        }

        Vector3 world = cachedCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y,
            Mathf.Abs(cachedCamera.transform.position.z - transform.position.z)));
        world.z = transform.position.z;

        // 在忽略当前 scale 的局部空间里判定，hover 缩放不会撑大或缩小命中区域。
        SetHovering(ContainsPointUnscaled(cachedRenderer.transform, cachedRenderer.sprite.bounds,
            world, rendererBaseScale));
    }

    // 把世界点换算到“只带旋转、按静止基准缩放”的局部空间再做矩形判定。
    // 注意不能用 InverseTransformPoint：它会把当前 scale 除掉，正好和未缩放的
    // sprite.bounds 抵消，命中区会跟着 hover 缩放一起变，等于白改。
    public static bool ContainsPointUnscaled(Transform target, Bounds localBounds,
        Vector3 worldPoint, Vector3 baseScale)
    {
        Vector3 offset = Quaternion.Inverse(target.rotation) * (worldPoint - target.position);
        offset.x /= Mathf.Approximately(baseScale.x, 0f) ? 1f : baseScale.x;
        offset.y /= Mathf.Approximately(baseScale.y, 0f) ? 1f : baseScale.y;
        return offset.x >= localBounds.min.x && offset.x <= localBounds.max.x &&
               offset.y >= localBounds.min.y && offset.y <= localBounds.max.y;
    }

    private static bool TryReadPointer(out Vector2 position)
    {
        position = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        position = Mouse.current.position.ReadValue();
        return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        position = Input.mousePosition;
        return true;
#else
        return false;
#endif
    }

    // ---- 弹簧积分 ----------------------------------------------------------

    private void Step()
    {
        bool interactable = uiButton == null || uiButton.interactable;
        if (interactable != lastInteractable)
        {
            lastInteractable = interactable;
            settled = false;
        }
        if (settled) return;

        float amp = amplitude * (reduced ? ReducedAmplitudeFactor : 1f);
        if (!interactable) amp *= DisabledFactor;

        bool engaged = hovering || pressed;
        float targetX = 1f;
        float targetY = 1f;
        if (engaged)
        {
            float gain = pressed ? PressedFactor : 1f;
            targetX = 1f + amp * gain;
            targetY = 1f + amp * gain * (1f - squashRatio);
        }

        // 2% 收敛准则：ts ≈ 4 / (ζ·ωn)，反解出固有频率。
        float zeta = reduced ? 1f : damping;
        float duration = reduced ? ReducedSettleTime : (engaged ? settleTime : releaseTime);
        float omegaX = 4f / Mathf.Max(0.01f, zeta * duration);
        float omegaY = omegaX * verticalLag;

        float delta = Time.unscaledDeltaTime;
        Integrate(ref x, ref velocityX, targetX, omegaX, zeta, delta);
        Integrate(ref y, ref velocityY, targetY, omegaY, zeta, delta);

        // 阈值取幅度的相对比例：幅度大的按钮不会拖出一条看不见的长尾。
        float epsilon = Mathf.Max(MinEpsilon, amp * SettleTolerance);
        if (Mathf.Abs(x - targetX) < epsilon && Mathf.Abs(velocityX) < epsilon * omegaX &&
            Mathf.Abs(y - targetY) < epsilon && Mathf.Abs(velocityY) < epsilon * omegaY)
        {
            x = targetX;
            y = targetY;
            velocityX = velocityY = 0f;
            settled = true;
        }

        ApplyScale();
    }

    private static void Integrate(ref float value, ref float velocity, float target,
        float omega, float zeta, float delta)
    {
        // 半隐式欧拉 + 定长子步：掉帧时也不会积分爆炸。
        while (delta > 0f)
        {
            float step = Mathf.Min(delta, MaxStep);
            delta -= step;
            float accel = omega * omega * (target - value) - 2f * zeta * omega * velocity;
            velocity += accel * step;
            value += velocity * step;
        }
    }

    // ---- 写 transform ------------------------------------------------------

    private void ApplyScale()
    {
        transform.localScale = new Vector3(baseScale.x * x, baseScale.y * y, baseScale.z);
        if (!counterScaleText) return;
        for (int i = 0; i < counters.Count; i++)
        {
            TextCounter counter = counters[i];
            if (counter.target == null) continue;
            counter.target.localScale = new Vector3(
                counter.baseScale.x / x, counter.baseScale.y / y, counter.baseScale.z);
        }
    }

    private void ResetToRest()
    {
        hovering = pressed = false;
        x = y = 1f;
        velocityX = velocityY = 0f;
        settled = true;
        ApplyScale();
    }

    private void CollectTextCounters()
    {
        counters.Clear();
        if (!counterScaleText) return;
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i].transform == transform) continue;
            counters.Add(new TextCounter { target = texts[i].transform, baseScale = texts[i].transform.localScale });
        }
    }

    private void OnSettingsChanged()
    {
        reduced = HoverJellySettings.ReducedMotion;
        settled = false;
    }
}
