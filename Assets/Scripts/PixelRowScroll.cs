using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 两个商店列表共用的像素对齐滚动：滚轮按行步进，拖动条按整像素移动。
// RectMask2D 负责裁剪。只由本组件移动 Content，避免与另一套 ScrollRect 争抢位置。
[RequireComponent(typeof(RectTransform))]
public class PixelRowScroll : MonoBehaviour, IScrollHandler
{
    [SerializeField] private RectTransform content;
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField, Min(1f)] private float rowPitch = 130f;
    [SerializeField, Min(0f)] private float rowHeight = 108f;
    [SerializeField, Min(0f)] private float rowGap = 22f;
    [SerializeField, Min(0f)] private float topMargin = 10f;
    [SerializeField, Min(0f)] private float bottomMargin = 10f;
    [SerializeField, Min(0)] private int rowCount = 8;
    [SerializeField, Min(0f)] private float stepCooldown = 0.06f;

    private float lastStepTime = float.NegativeInfinity;

    public float ContentHeight
    {
        get
        {
            int rows = Mathf.Max(1, rowCount);
            return topMargin + bottomMargin + rows * rowHeight + (rows - 1) * Mathf.Max(0f, rowGap);
        }
    }
    private float ViewportHeight => ((RectTransform)transform).rect.height;
    private float MaxScroll => Mathf.Max(0f, ContentHeight - ViewportHeight);
    private float Offset => content != null ? content.anchoredPosition.y : 0f;

    private void Awake() => ApplyLayout();
    private void OnEnable()
    {
        if (scrollbar != null) scrollbar.onValueChanged.AddListener(OnScrollbarChanged);
        RefreshScrollbar();
    }
    private void OnDisable()
    {
        if (scrollbar == null) return;
        scrollbar.onValueChanged.RemoveListener(OnScrollbarChanged);
        scrollbar.gameObject.SetActive(false);
    }
    private void OnRectTransformDimensionsChange()
    {
        if (content != null) SetOffset(Offset);
    }

    public void ApplyLayout()
    {
        if (content == null) return;
        content.sizeDelta = new Vector2(content.sizeDelta.x, ContentHeight);
        SetOffset(0f);
    }

    // 按调用方给定的**顺序**重排行，并把 visible[i] 为 false 的行整行 SetActive(false)，
    // 后面的行依次顶上 —— 隐藏的行不留空档，列表始终是紧凑的。
    //
    // 顺序必须由调用方给出，不能靠 anchoredPosition.y 或兄弟顺序现推：行会被反复隐藏/显示，
    // 「刚恢复的那一行」的 y 还是它被隐藏之前的旧值（和某个可见行撞在一起），用 y 排序会排错位。
    // 隐藏期间不改它们的坐标，因为它们不参与排布，坐标已经无意义。
    public void SetRows(RectTransform[] orderedRows, bool[] visible)
    {
        if (content == null || orderedRows == null) return;
        float pitch = Mathf.Max(1f, rowPitch);
        // 首行中心 y = -(上留白 + 行高一半)。锚点在上边缘、y 向下为负，
        // 所以这个值就是 GadgetRowTop 那种负数（10 + 108/2 = 64 → -64），别丢符号。
        float firstY = -(topMargin + rowHeight * 0.5f);
        int shown = 0;
        for (int i = 0; i < orderedRows.Length; i++)
        {
            RectTransform row = orderedRows[i];
            if (row == null) continue;
            bool show = visible == null || i >= visible.Length || visible[i];
            if (row.gameObject.activeSelf != show) row.gameObject.SetActive(show);
            if (!show) continue;
            row.anchoredPosition = new Vector2(row.anchoredPosition.x, firstY - shown * pitch);
            shown++;
        }
        rowCount = Mathf.Max(1, shown);
        ApplyLayout();
    }
    public void OnScroll(PointerEventData eventData)
    {
        if (eventData == null || content == null || Mathf.Abs(eventData.scrollDelta.y) < .01f) return;
        if (Time.unscaledTime - lastStepTime < stepCooldown) return;
        lastStepTime = Time.unscaledTime;
        Step(eventData.scrollDelta.y > 0f ? -1 : 1);
    }
    private void Step(int rows)
    {
        float pitch = Mathf.Max(1f, rowPitch);
        float index = rows > 0 ? Mathf.Floor(Offset / pitch) : Mathf.Ceil(Offset / pitch);
        SetOffset((index + rows) * pitch);
    }
    private void OnScrollbarChanged(float value) => SetOffset((1f - value) * MaxScroll);
    private void SetOffset(float y)
    {
        if (content == null) return;
        // 像素字体只落在整数位置；滚轮和拖动都遵守同一个边界。
        float rounded = Mathf.Round(Mathf.Clamp(y, 0f, MaxScroll));
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, rounded);
        RefreshScrollbar();
    }
    private void RefreshScrollbar()
    {
        if (scrollbar == null) return;
        bool visible = isActiveAndEnabled && MaxScroll > .5f;
        if (scrollbar.gameObject.activeSelf != visible) scrollbar.gameObject.SetActive(visible);
        scrollbar.size = Mathf.Clamp01(ViewportHeight / Mathf.Max(1f, ContentHeight));
        scrollbar.SetValueWithoutNotify(MaxScroll > 0f ? 1f - Offset / MaxScroll : 1f);
    }
}
