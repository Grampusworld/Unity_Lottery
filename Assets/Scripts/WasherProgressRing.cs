using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 机器的圆环形进度条：世界空间 Canvas + Image(Filled/Radial360)。
//
// 挂在机器的**同级**物体上，而不是子物体：
//   ① 机器 localScale 是 26/38，做子物体会把 Canvas 一起放大；
//   ② 果冻缩放会带着环一起抖，环应该保持稳定的尺寸。
//
// 职责划分（2026-09-29 重构，两台机器共用这一个组件）：
//   · 机器只说「我多大、我可见内容的中心在哪」—— 几何变化时推一次 SetMachineAnchor()；
//   · 环自己每帧跟随（LateUpdate 只比 transform.position，没动就跳过，不标脏 Canvas）。
//
// 为什么把「跟随」从调用点搬进来：原来两台机器各写一份（一个每帧现算、一个缓存偏移），
// 而 FollowMachine() 只在「拖动 / 落定 / 回设计位」三处被调 —— 惯性滑行那条路径上
// MachineDrag 直接 return 了，环和机内内容就留在原地。写在这里以后不会再漏。
public class WasherProgressRing : MonoBehaviour
{
    [Header("Ring")]
    [SerializeField] private Image ringImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Color ringColor = new Color(0.36f, 0.85f, 1f, 1f);

    [Header("Progress")]
    [Tooltip("填充量化步数。<= 1 表示**不量化**（逐帧连续）。\n" +
             "历史值 24 是刻意的像素风量化，但它每 4.17% 跳一格：8 秒一轮就是每 0.33 秒跳一下。" +
             "Filled/Radial360 是 mesh 几何切边，Point 采样下切边本来就是硬边 —— 量化并不会让它更干净。")]
    [SerializeField] private int fillSteps = 0;

    [Header("Placement")]
    [Tooltip("环直径 = 机器可见内容宽度 × 该系数。\n" +
             "用宽度而不是高度：两台机器高度接近（22.9 / 19.8~23.2），按高度算两个环几乎一样大；" +
             "按宽度算才能让洗盘机的环明显更大（10.67 vs 6.02~7.11 世界单位）。")]
    [SerializeField] private float sizeRatio = 0.36f;
    [Tooltip("环**底边**与机器可见内容顶边之间的间隙（世界单位）。用固定值：尺寸不同的两个环有同样的悬浮高度。")]
    [SerializeField] private float bottomGap = 1f;

    [Header("Fade")]
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.1f;

    private Coroutine fadeRoutine;
    private float targetAlpha;
    private bool shown;

    // 跟随用的机器引用。机器只会**平移**（缩放变化走 SetMachineAnchor 重算），
    // 所以每帧直接吃位移增量就够了，不必重算包围盒，也不会累积误差。
    private Transform machine;
    private Vector3 lastMachinePosition;

    private void Awake()
    {
        if (ringImage != null)
        {
            ringImage.color = ringColor;
            ringImage.raycastTarget = false;
            ringImage.fillAmount = 0f;
        }
        shown = false;
        ApplyAlpha(0f);
    }

    // ---- 对外接口 ----------------------------------------------------------

    // 机器把自己的**可见内容包围盒**（世界空间）推过来：初始化 / 换等级 / 改尺寸时各调一次。
    // 传 SpriteRenderer 而不是裸 Transform，是为了拿到「机器是谁」用于每帧跟随。
    // contentCenter / contentSize 必须是**静止尺寸**下的值（不要用果冻放大后的包围盒），
    // 否则环会跟着悬停果冻的弹跳一起动。
    public void SetMachineAnchor(SpriteRenderer machineRenderer, Vector3 contentCenter, Vector2 contentSize)
    {
        machine = machineRenderer != null ? machineRenderer.transform : null;
        if (machine != null) lastMachinePosition = machine.position;

        float diameter = Mathf.Max(0.01f, contentSize.x) * sizeRatio;
        float top = contentCenter.y + contentSize.y * 0.5f;
        // 环心 = 内容顶边 + 间隙 + 半径 → 整个环完全落在机器之上，与机器零重叠。
        float centerY = top + bottomGap + diameter * 0.5f;

        // z 保持环自己原来的值：它是世界空间 Canvas 的所在平面，不该跟着机器走。
        transform.position = new Vector3(contentCenter.x, centerY, transform.position.z);

        // 世界直径 → Canvas 缩放：Image 的矩形宽度就是环贴图的像素宽（两台机器都是 128）。
        float rectWidth = ringImage != null ? ringImage.rectTransform.rect.width : 100f;
        float scale = diameter / Mathf.Max(1f, rectWidth);
        transform.localScale = new Vector3(scale, scale, 1f);
    }

    // 幂等：已经是显示态就不再重启淡入协程。
    // 原来 AutoScratcher 每帧调它一次（内部 StopCoroutine + StartCoroutine + SetProgress(0)），
    // 等于每秒 60 次协程重建，并且每秒 60 次把进度写回 0 再写回真值 —— 画面上看不出来，纯浪费。
    public void Show()
    {
        if (shown) return;
        shown = true;
        SetProgress(0f);
        FadeTo(1f, fadeInDuration);
    }

    public void Hide(bool immediate = false)
    {
        shown = false;
        if (immediate)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = null;
            ApplyAlpha(0f);
            return;
        }
        FadeTo(0f, fadeOutDuration);
    }

    public void SetProgress(float t)
    {
        if (ringImage == null) return;
        float clamped = Mathf.Clamp01(t);
        if (fillSteps > 1) clamped = Mathf.Round(clamped * fillSteps) / fillSteps;
        ringImage.fillAmount = clamped;
    }

    // ---- 跟随 --------------------------------------------------------------

    // 机身被拖动 / 惯性滑行 / 入场下落改动位置时，环跟着走同一个位移。
    // 位置没变就 early-return：静息状态下不写 RectTransform，避免把世界空间 Canvas 标脏。
    private void LateUpdate()
    {
        if (machine == null) return;
        Vector3 now = machine.position;
        if (now == lastMachinePosition) return;
        transform.position += now - lastMachinePosition;
        lastMachinePosition = now;
    }

    // ---- 淡入淡出 ----------------------------------------------------------

    private void FadeTo(float alpha, float duration)
    {
        targetAlpha = alpha;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(duration));
    }

    private IEnumerator FadeRoutine(float duration)
    {
        float from = canvasGroup != null ? canvasGroup.alpha : 1f;
        if (duration <= 0f)
        {
            ApplyAlpha(targetAlpha);
            yield break;
        }
        float time = 0f;
        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            ApplyAlpha(Mathf.Lerp(from, targetAlpha, Mathf.Clamp01(time / duration)));
            yield return null;
        }
        ApplyAlpha(targetAlpha);
    }

    private void ApplyAlpha(float alpha)
    {
        if (canvasGroup != null) canvasGroup.alpha = alpha;
        else if (ringImage != null)
        {
            Color color = ringColor;
            color.a = alpha;
            ringImage.color = color;
        }
    }
}
