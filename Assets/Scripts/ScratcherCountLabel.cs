using TMPro;
using UnityEngine;

// 自动刮彩票机下方的计数牌：机内票数 / 容量（0/3、2/5）。
//
// 为什么是**屏幕空间**浮层，而不是像进度环那样建世界空间 Canvas：
//   像素字体的清晰度只由「Canvas 缩放是不是整数」决定，而整数缩放只有 PixelPerfectCanvasScaler
//   在做（它读 canvas.rootCanvas.pixelRect 反算整数 scaleFactor，只对屏幕空间 Canvas 生效）。
//   世界空间 Canvas 的每单位像素数 = 视图高 / (2 × orthoSize)，会随 Game 视图分辨率漂
//   （2560×1440 → 14.4，1786×906 → 9.06），8px 字体必然被拉成小数像素 ——
//   正是 PixelPerfectCanvasScaler 头注释里写的那个"A 变成 8"的缺陷。所以这里每帧把世界锚点
//   投影成屏幕坐标，文本本身住在主 Canvas 里，清晰度由那条整数缩放兜底。
//
// 也不能做成机器的子物体：机身 localScale=38，HoverJelly 还会按 x/y 不同幅度缩放，
// 子物体文本会被拉变形。位置每帧从 AutoScratcher 现取（和进度环同思路）。
public class ScratcherCountLabel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AutoScratcher scratcher;
    [Tooltip("计数文本（主 Canvas 的直接子物体，不能挂在被 SetActive(false) 的面板下面）。")]
    [SerializeField] private RectTransform labelRect;
    [SerializeField] private Canvas labelCanvas;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Camera worldCamera;

    [Header("Placement")]
    [Tooltip("锚点相对机身可见内容底边的额外下移量（世界单位）。机器底边 y=-36.5、" +
             "桌面顶边 y=-48.6，中间这条 12 单位的蓝色带正好放得下；2.2 单位 ≈ 32px。")]
    [SerializeField] private float belowOffset = 2.2f;

    [Header("Colors")]
    [SerializeField] private Color normalColor = new Color32(246, 235, 205, 255);
    [Tooltip("满仓（count == capacity）时的颜色，与满级按钮同一套金。")]
    [SerializeField] private Color fullColor = new Color32(198, 158, 64, 255);

    private string shownText;
    // 初值给 true：物体在场景里是开着的，第一次 ApplyVisible(false) 要真的把它关掉，
    // 否则未解锁时第一帧会闪一下 "0/1"。
    private bool shownVisible = true;
    private Vector2 placedPosition;
    private bool hasPlaced;

    private void Awake()
    {
        if (scratcher == null) scratcher = GetComponent<AutoScratcher>();
        if (worldCamera == null) worldCamera = Camera.main;
        if (labelCanvas == null && labelRect != null) labelCanvas = labelRect.GetComponentInParent<Canvas>();
        if (labelText == null && labelRect != null) labelText = labelRect.GetComponent<TMP_Text>();
        ApplyVisible(false);
    }

    private void LateUpdate()
    {
        if (scratcher == null || labelRect == null || labelText == null) return;
        if (worldCamera == null) worldCamera = Camera.main;

        // 未解锁时机身是隐藏的；下落途中锚点还在半空。这两种情况都不该有字悬在那儿。
        if (!scratcher.Unlocked || scratcher.Entering)
        {
            ApplyVisible(false);
            return;
        }

        PlaceLabel();
        ApplyVisible(true);

        int count = scratcher.Count;
        int capacity = Mathf.Max(1, scratcher.Capacity);
        string wanted = count + "/" + capacity;
        if (wanted != shownText)
        {
            labelText.text = wanted;
            shownText = wanted;
        }
        labelText.color = count >= capacity ? fullColor : normalColor;
    }

    private void PlaceLabel()
    {
        Vector3 world = scratcher.ContentBottomWorld - Vector3.up * belowOffset;
        Vector3 screen = worldCamera.WorldToScreenPoint(world);

        Camera uiCamera = labelCanvas != null && labelCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? worldCamera
            : null;
        RectTransform canvasRect = labelCanvas != null ? (RectTransform)labelCanvas.transform : null;
        if (canvasRect == null) return;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCamera, out local)) return;
        // 机器落定后这个值是常量。同值重复写会每帧把 Canvas 标脏重算一次批次，
        // 所以卡一个 0.1px 的容差再写（和 PixelPerfectCanvasScaler 里那条"只在变化时赋值"同理）。
        if (hasPlaced && (local - placedPosition).sqrMagnitude < 0.01f) return;
        placedPosition = local;
        hasPlaced = true;
        labelRect.anchoredPosition = local;
    }

    public Bounds GetWorldBounds()
    {
        Vector3 center = scratcher != null ? scratcher.ContentBottomWorld - Vector3.up * belowOffset : transform.position;
        if (labelRect == null || worldCamera == null) return new Bounds(center, Vector3.zero);
        float factor = labelCanvas != null ? labelCanvas.rootCanvas.scaleFactor : 1f;
        float unitsPerPixel = 2f * worldCamera.orthographicSize / Mathf.Max(1f, worldCamera.pixelHeight);
        Vector2 size = labelRect.rect.size * factor * unitsPerPixel;
        return new Bounds(center, new Vector3(size.x, size.y, 0f));
    }

    private void ApplyVisible(bool visible)
    {
        if (shownVisible == visible) return;
        shownVisible = visible;
        if (labelRect != null) labelRect.gameObject.SetActive(visible);
    }
}
