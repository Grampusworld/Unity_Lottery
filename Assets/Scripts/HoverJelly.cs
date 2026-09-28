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
public class HoverJelly : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
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

    [Header("Press Pulse")]
    [Tooltip("按下时叠一记短促弹出，作为点击反馈。强度是 amplitude 的倍数（1 = 悬停幅度）。")]
    [SerializeField] private bool pressPulse = true;
    [SerializeField, Range(0f, 4f)] private float pressPulseStrength = 1.8f;
    [SerializeField, Range(0.03f, 0.4f)] private float pressPulseDuration = 0.10f;

    private const float MaxStep = 1f / 120f;       // 帧内子步长上限
    // 子步长还要按刚度收敛：半隐式欧拉要求 ω·h < 2，留 8 倍余量取 0.25。
    // 不这么做的话，脉冲把 duration 压到 60ms 时 ω≈208，ω·h≈1.75 已在稳定边缘，
    // 再叠上「脉冲间隔 ≈ 弹簧共振周期」就会把 scale 推到 1e34（实测踩到过）。
    private const float StabilityRatio = 0.25f;
    private const float MinScale = 0.5f;           // 弹簧硬限幅：任何情况下都不允许跑飞
    private const float MaxScale = 1.5f;
    private const float SettleTolerance = 0.02f;   // 收敛判定：相对幅度的 2%，对应 settleTime 的 2% 准则
    private const float MinEpsilon = 0.0001f;
    private const float DisabledFactor = 0.5f; // interactable=false 时幅度减半
    private const float PressedFactor = 0.8f;  // 世界物体被按住时的恒定缩放
    private const float ReducedAmplitudeFactor = 0.5f;
    private const float ReducedSettleTime = 0.05f;  // 临界阻尼下收敛比欠阻尼慢，取值要比目标时长更短
    private const float PulseHoldRatio = 0.4f;      // 脉冲里「抬起」占的时长比例，剩下的留给回弹

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
    private float pulseStrength;        // 程序触发的脉冲增益，0 = 无脉冲
    private float pulseTimer;           // > 0 表示目标仍被抬起
    private float pulseDurationScale = 1f;
    private bool pulseEngaged;          // 从 Pulse 到收敛期间，弹簧时长按此比例压缩
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

    // 程序触发一次弹跳（盘子进机、结算等）。duration 是整个脉冲的总时长；
    // 传 <= 0 表示用默认 settleTime。高频场景（快放时每 27ms 一个）要把 duration
    // 压到 60ms 并把 strength 降到 0.6 左右，否则脉冲会互相叠加成持续膨胀。
    public void Pulse(float strength = 1f, float duration = -1f)
    {
        float baseDuration = reduced ? ReducedSettleTime : settleTime;
        if (duration <= 0f) duration = baseDuration;
        pulseDurationScale = Mathf.Clamp(duration / Mathf.Max(0.01f, baseDuration), 0.05f, 4f);
        pulseStrength = Mathf.Max(0f, strength);
        pulseTimer = duration * PulseHoldRatio;
        pulseEngaged = true;
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

    // 按下瞬间叠一记脉冲：对已经 hover 的按钮来说，是在 1.04 之上再弹出到约 1.075，
    // 松开前就回落。走 Pulse 通道 → 与 hover 取最大增益、互不打断，不会把状态机搞乱。
    public void OnPointerDown(PointerEventData eventData)
    {
        if (hitSource != HitSource.UIEvent) return;
        if (!pressPulse) return;
        if (uiButton != null && !uiButton.interactable) return;

        // 降级档下幅度已在 Step 里减半，强度不能再减：strength 一旦小于 hover 的增益 1.0，
        // 悬停中的按钮按下去就没有任何变化（增益取最大值，脉冲会被 hover 顶掉）。
        if (reduced)
            Pulse(pressPulseStrength, ReducedSettleTime);
        else
            Pulse(pressPulseStrength, pressPulseDuration);
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

        float delta = Time.unscaledDeltaTime;

        // 悬停 / 按住 / 脉冲三条来源取最大增益，互不打断：脉冲期间鼠标移入也不会被顶掉。
        float gain = pressed ? PressedFactor : (hovering ? 1f : 0f);
        if (pulseTimer > 0f && pulseStrength > gain) gain = pulseStrength;

        // 计时器必须**在增益判定之后**推进：pulseTimer 只占 duration 的 40%（默认 0.10s → 40ms），
        // 掉帧到 25fps 以下时一帧就跨过整个窗口，先减后判会让脉冲一帧都不生效。
        if (pulseTimer > 0f)
        {
            pulseTimer -= delta;
            if (pulseTimer < 0f) pulseTimer = 0f;
        }

        bool engaged = gain > 0f;
        float targetX = 1f;
        float targetY = 1f;
        if (engaged)
        {
            targetX = 1f + amp * gain;
            targetY = 1f + amp * gain * (1f - squashRatio);
        }

        // 2% 收敛准则：ts ≈ 4 / (ζ·ωn)，反解出固有频率。
        float zeta = reduced ? 1f : damping;
        float duration = reduced ? ReducedSettleTime : (engaged ? settleTime : releaseTime);
        if (pulseEngaged) duration *= pulseDurationScale;
        float omegaX = 4f / Mathf.Max(0.01f, zeta * duration);
        float omegaY = omegaX * verticalLag;

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
            pulseEngaged = false;
            pulseStrength = 0f;
            pulseTimer = 0f;
        }

        ApplyScale();
    }

    private static void Integrate(ref float value, ref float velocity, float target,
        float omega, float zeta, float delta)
    {
        // 半隐式欧拉 + 子步：步长同时受帧长与刚度约束，掉帧和短脉冲都不会积分爆炸。
        float maxStep = Mathf.Min(MaxStep, StabilityRatio / Mathf.Max(0.01f, omega));
        while (delta > 0f)
        {
            float step = Mathf.Min(delta, maxStep);
            delta -= step;
            float accel = omega * omega * (target - value) - 2f * zeta * omega * velocity;
            velocity += accel * step;
            value += velocity * step;
        }

        // 兜底限幅：真出现数值意外时也只表现为封顶的小幅抖动，不会把物体放大成无穷大。
        if (value > MaxScale)
        {
            value = MaxScale;
            if (velocity > 0f) velocity = 0f;
        }
        else if (value < MinScale)
        {
            value = MinScale;
            if (velocity < 0f) velocity = 0f;
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
        pulseStrength = 0f;
        pulseTimer = 0f;
        pulseEngaged = false;
        pulseDurationScale = 1f;
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
