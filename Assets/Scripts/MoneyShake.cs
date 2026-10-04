using TMPro;
using UnityEngine;

// 金币不够时的反馈：余额文本颤抖、变红，并在连续触发时逐档放大。
//
// 触发源只有一个 —— LotteryGame.Spend() 返回 false 时抛出的 InsufficientFunds 事件。
// 所以「票买不起 / 海绵买不起 / 机器买不起 / 升级买不起」全都自动覆盖，
// 而「已经买过了、机器还没解锁、等级满了」这些非金钱原因不会误触发：它们根本走不到 Spend()。
//
// 像素字体按 8 点一档增长；震动位移始终按原字号换算，
// 连点只叠加字号，不叠加震动幅度。
[RequireComponent(typeof(TMP_Text))]
public class MoneyShake : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float duration = 0.35f;

    [Header("Shake")]
    [Tooltip("位移峰值，单位是 texel（不是画布单位）。4 texel 在 40 号字下等于 ±20 画布单位。")]
    [SerializeField, Range(1f, 12f)] private float shiftTexels = 4f;
    [Tooltip("每次触发增加的字号，按像素字体的 8 点网格取整。")]
    [SerializeField, Min(8f)] private float growthStep = 8f;
    [Tooltip("抖动期间的字色。结束后硬切回原色，不做渐变。")]
    [SerializeField] private Color shakeColor = new Color(1f, 0f, 0f, 1f);
    [Tooltip("连续触发的放大档数上限；40 点基准、8 点步长、2 档时最多 56 点。")]
    [SerializeField, Range(1, 4)] private int maxStack = 2;
    [Tooltip("Reduce Motion 降级档的幅度系数（红色保留，抖动幅度减半）。")]
    [SerializeField, Range(0f, 1f)] private float reducedAmplitudeScale = 0.5f;

    private TMP_Text label;
    private RectTransform rect;
    private Vector2 homePosition;
    private float homeFontSize = 40f;
    private Color homeColor = Color.white;
    private float timer;
    private int stack;
    private bool active;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        rect = transform as RectTransform;
        if (rect != null) homePosition = rect.anchoredPosition;
        if (label != null)
        {
            homeFontSize = label.enableAutoSizing ? label.fontSizeMax : label.fontSize;
            homeColor = label.color;
        }
    }

    // 由 LotteryGame.InsufficientFunds 驱动。抖动没结束时再触发会叠加字号并重置计时器，
    // 而不是排队 —— 排队会让连点变成一串慢动作。
    public void Play()
    {
        stack = active ? Mathf.Min(maxStack, stack + 1) : 1;
        timer = duration;
        active = true;
        Apply();
    }

    private void Update()
    {
        if (!active) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Restore();
            return;
        }
        Apply();
    }

    private void OnDisable()
    {
        // 面板被关掉、物体被销毁时不能把红色和放大字号留在场上。
        if (active) Restore();
    }

    private void Apply()
    {
        if (label == null || rect == null) return;

        float pixelStep = Mathf.Max(8f, Mathf.Round(growthStep / 8f) * 8f);
        // Grow only as far as the fixed money box allows.
        if (label.enableAutoSizing) label.fontSizeMax = homeFontSize + pixelStep * stack;
        else label.fontSize = homeFontSize + pixelStep * stack;
        label.color = shakeColor;

        float motionScale = HoverJellySettings.ReducedMotion ? reducedAmplitudeScale : 1f;
        int amplitude = Mathf.Max(1, Mathf.RoundToInt(shiftTexels * motionScale));

        // 每帧重新随机 = 逐帧满幅跳变，这才是「剧烈颤抖」；
        // 正弦波在 60fps 下会退化成看着像缓慢晃动的驻波。
        float texel = homeFontSize / 8f;
        int offsetX = Random.Range(-amplitude, amplitude + 1);
        int offsetY = Random.Range(-amplitude, amplitude + 1);
        rect.anchoredPosition = homePosition + new Vector2(offsetX * texel, offsetY * texel);
    }

    private void Restore()
    {
        active = false;
        timer = 0f;
        stack = 0;
        if (label != null)
        {
            if (label.enableAutoSizing) label.fontSizeMax = homeFontSize;
            else label.fontSize = homeFontSize;
            label.color = homeColor;
        }
        if (rect != null) rect.anchoredPosition = homePosition;
    }
}
