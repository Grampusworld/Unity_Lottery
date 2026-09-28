using TMPro;
using UnityEngine;

// 让「图标 + 文字」作为一个整体在票按钮里居中，间距恒定。
//
// 为什么必须做成运行时组件：Label 的文案是 LotteryGame.RefreshUI 每次都改的
// （金额、解锁状态），宽度从 264px（NEW LUCKY  $10）跳到 432px（UNLOCK NOVA  $1000）。
// 把图标固定钉在按钮左边的话，短文案时图标和文字之间会裂开一百多像素的空白。
//
// 做法（两条一起成立，才等价于「整组居中」）：
//   1) 文字：Label 保持**全宽拉伸 + Center 对齐**，用 TMP 的 margin 把文本块整体右移
//      (iconWidth + gap) / 2。这个位移是**常量**，与文案长度无关。
//   2) 图标：图标中心 = 按钮中心 − (gap + 文案宽) / 2，跟随文案宽度实时算。
//   两式相加 → 整组左右边界关于按钮中心对称。
//
// 为什么用 margin 而不是把 Label 的 rect 挪窄：
//   挪窄 rect 会让文本落在小矩形里，度量稍有偏差就贴边；margin 不动 rect（永远全宽），
//   没有任何裁剪风险，即使文案超长也只是视觉越界而不会被切（本项目全场无 Mask）。
//
// TMP margin 语义坑：margin.x 是「文本区左内缩」，Center 对齐下每 1 单位只把文字推 0.5，
//   所以要位移 shift 就必须写 margin.x = 2 * shift（右边距 margin.z 的贡献单独补上）。
[ExecuteAlways]
[DisallowMultipleComponent]
public class TicketButtonIconLayout : MonoBehaviour
{
    [SerializeField] private RectTransform icon;
    [SerializeField] private TMP_Text label;

    [Header("Layout")]
    [SerializeField, Min(0f)] private float iconWidth = 48f;
    [SerializeField, Min(0f)] private float iconGap = 12f;      // 图标右边缘到文字左边缘
    [SerializeField] private float iconRowY = 43f;              // 与 Label 主标题行同高
    [SerializeField, Min(0f)] private float sideMargin = 6f;    // 整组距按钮左右的最小留白
    [SerializeField] private bool layoutActive = true;

    private const float MeasureWidth = 9999f;   // 量单行宽度时给 TMP 的假约束

    private string lastText;
    private float lastRectWidth = float.NaN;
    private float lastParamKey = float.NaN;
    private bool everApplied;

    private void OnEnable() { Apply(); }
    private void LateUpdate() { Apply(); }
    private void OnRectTransformDimensionsChange() { Apply(); }

    [ContextMenu("重新布局")]
    public void Apply()
    {
        if (!layoutActive || icon == null || label == null) return;
        RectTransform self = transform as RectTransform;
        if (self == null) return;

        float width = self.rect.width;
        if (width <= 1f && Mathf.Approximately(self.anchorMax.x, self.anchorMin.x))
        {
            // 画布布局还没算出来时 rect.width 可能是 0（编辑器里改完 offset 立刻读就是这种），
            // 点锚点下 sizeDelta.x 就是真实宽度，用它兜底。
            width = Mathf.Abs(self.sizeDelta.x);
        }
        if (width <= 1f) return;   // 还是拿不到，下一帧再说

        string text = label.text ?? string.Empty;
        float paramKey = iconWidth * 4096f + iconGap * 64f + sideMargin * 8f + iconRowY * 0.0078125f;
        if (everApplied && text == lastText && Mathf.Approximately(width, lastRectWidth) &&
            Mathf.Approximately(paramKey, lastParamKey))
            return;   // 文案/尺寸/参数都没变 → 不碰 margin，不给 TMP 加脏标记

        lastText = text;
        lastRectWidth = width;
        lastParamKey = paramKey;
        everApplied = true;

        // 量文字单行宽度。先把 margin 归零再量，避免上一次写进去的位移混进度量结果。
        Vector4 original = label.margin;
        label.margin = Vector4.zero;
        float textWidth = MeasureSingleLineWidth(label);

        // 文案过长时先牺牲间距，再把图标钉在左边界，绝不让图标被顶出按钮。
        float gap = iconGap;
        if (iconWidth + gap + textWidth > width - sideMargin * 2f)
            gap = sideMargin;

        float groupWidth = iconWidth + gap + textWidth;
        float iconCenter = (width - groupWidth) * 0.5f + iconWidth * 0.5f;
        float minCenter = sideMargin + iconWidth * 0.5f;
        if (iconCenter < minCenter) iconCenter = minCenter;

        Vector2 iconPosition = icon.anchoredPosition;
        iconPosition.x = iconCenter;
        iconPosition.y = iconRowY;
        icon.anchoredPosition = iconPosition;

        // 文本块位移：让文字左边缘恰好落在图标右边缘 + gap 处。
        // margin.x = margin.z + 2 * shift 才是精确解（见文件头注释）。
        float textCenter = iconCenter + iconWidth * 0.5f + gap + textWidth * 0.5f;
        float shift = textCenter - width * 0.5f;
        label.margin = new Vector4(original.z + shift * 2f, original.y, original.z, original.w);
    }

    // 单行宽度：项目约定票按钮 Label 一律 NoWrap，直接量；
    // 万一被改成换行模式，临时关掉量一次再还原，避免拿到「按窄宽度折行」的结果。
    private static float MeasureSingleLineWidth(TMP_Text text)
    {
        TextWrappingModes mode = text.textWrappingMode;
        bool wrapped = mode == TextWrappingModes.Normal || mode == TextWrappingModes.PreserveWhitespace;
        if (!wrapped)
            return text.GetPreferredValues(text.text, MeasureWidth, MeasureWidth).x;

        text.textWrappingMode = TextWrappingModes.NoWrap;
        float width = text.GetPreferredValues(text.text, MeasureWidth, MeasureWidth).x;
        text.textWrappingMode = mode;
        return width;
    }
}
