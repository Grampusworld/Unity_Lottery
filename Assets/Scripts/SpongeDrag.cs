using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Drag the sponge around the visible tabletop and leave it where it was released.
[RequireComponent(typeof(SpriteRenderer))]
public class SpongeDrag : MonoBehaviour
{
    [SerializeField] private Sprite yellowSprite;
    [SerializeField] private Sprite purpleSprite;
    [SerializeField] private Camera inputCamera;
    [SerializeField] private SpriteRenderer tableSurface;
    [Tooltip("Table.png 中有颜色的桌面范围，单位为贴图像素（左下角为原点）。")]
    [SerializeField] private Rect tabletopPixels = new Rect(58f, 128f, 99f, 57f);
    [SerializeField, Min(1)] private int yellowBrushRadius = 4;

    private LotteryGame game;
    private SpriteRenderer spriteRenderer;
    private HoverJelly hoverJelly;
    private Vector3 previousPosition;
    private Vector3 grabOffset;
    private Vector3 baseScale;
    private bool dragging;
    private bool advanced;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
        if (inputCamera == null) inputCamera = Camera.main;
        hoverJelly = GetComponent<HoverJelly>();
    }

    public void Initialize(LotteryGame owner, bool usePurple)
    {
        game = owner;
        SetAdvanced(usePurple);
    }

    public void SetAdvanced(bool usePurple)
    {
        advanced = usePurple;
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = usePurple ? purpleSprite : yellowSprite;
        transform.position = ClampToTable(transform.position);
    }

    private void Update()
    {
        if (game == null || inputCamera == null || !ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)) return;
        if (!pressed)
        {
            if (dragging)
            {
                if (hoverJelly != null) hoverJelly.SetPressed(false);
            }
            dragging = false;
            transform.position = ClampToTable(transform.position);
            return;
        }

        Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y,
            Mathf.Abs(inputCamera.transform.position.z - transform.position.z)));
        world.z = transform.position.z;
        if (!dragging)
        {
            if (spriteRenderer.sprite == null) return;
            // 判定基准是静止尺寸的包围盒，hover 果冻缩放时命中区域保持不变。
            if (!justPressed || !HoverJelly.ContainsPointUnscaled(spriteRenderer.transform,
                    spriteRenderer.sprite.bounds, world, baseScale)) return;
            dragging = true;
            grabOffset = transform.position - world;
            previousPosition = transform.position;
            if (hoverJelly != null) hoverJelly.SetPressed(true);
        }

        world = ClampToTable(world + grabOffset);
        transform.position = world;
        DirtyPlate plate = game.CurrentPlate;
        if (plate != null)
        {
            int brush = yellowBrushRadius * (advanced ? 2 : 1);
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(previousPosition, world) / 0.5f), 1, 50);
            for (int i = 0; i <= steps; i++) plate.ScrubAt(Vector3.Lerp(previousPosition, world, (float)i / steps), brush);
        }
        previousPosition = world;
    }

    private Vector3 ClampToTable(Vector3 world)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null || inputCamera == null) return world;

        float depth = Mathf.Abs(inputCamera.transform.position.z - world.z);
        Vector3 cameraMin = inputCamera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 cameraMax = inputCamera.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
        Bounds visible = new Bounds((cameraMin + cameraMax) * 0.5f,
            new Vector3(Mathf.Abs(cameraMax.x - cameraMin.x), Mathf.Abs(cameraMax.y - cameraMin.y), 1f));
        Bounds area = GetTabletopBounds(visible);

        // HoverJelly 最多放大到原尺寸的 1.5 倍，留足空间避免边缘被裁掉。
        Vector3 spriteSize = spriteRenderer.sprite.bounds.size;
        float halfWidth = spriteSize.x * Mathf.Abs(baseScale.x) * 0.75f;
        float halfHeight = spriteSize.y * Mathf.Abs(baseScale.y) * 0.75f;
        float minX = Mathf.Max(area.min.x, visible.min.x) + halfWidth;
        float maxX = Mathf.Min(area.max.x, visible.max.x) - halfWidth;
        float minY = Mathf.Max(area.min.y, visible.min.y) + halfHeight;
        float maxY = Mathf.Min(area.max.y, visible.max.y) - halfHeight;
        world.x = minX <= maxX ? Mathf.Clamp(world.x, minX, maxX) : (minX + maxX) * 0.5f;
        world.y = minY <= maxY ? Mathf.Clamp(world.y, minY, maxY) : (minY + maxY) * 0.5f;
        return world;
    }

    private Bounds GetTabletopBounds(Bounds fallback)
    {
        if (tableSurface == null || tableSurface.sprite == null) return fallback;
        Sprite tableSprite = tableSurface.sprite;
        Vector2 pivot = tableSprite.pivot;
        float ppu = tableSprite.pixelsPerUnit;
        Vector3 low = tableSurface.transform.TransformPoint(new Vector3(
            (tabletopPixels.xMin - pivot.x) / ppu, (tabletopPixels.yMin - pivot.y) / ppu));
        Vector3 high = tableSurface.transform.TransformPoint(new Vector3(
            (tabletopPixels.xMax - pivot.x) / ppu, (tabletopPixels.yMax - pivot.y) / ppu));
        Bounds bounds = new Bounds();
        bounds.SetMinMax(Vector3.Min(low, high), Vector3.Max(low, high));
        return bounds;
    }

    private static bool ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)
    {
        screen = Vector2.zero;
        pressed = justPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        screen = Mouse.current.position.ReadValue();
        pressed = Mouse.current.leftButton.isPressed;
        justPressed = Mouse.current.leftButton.wasPressedThisFrame;
        return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        screen = Input.mousePosition;
        pressed = Input.GetMouseButton(0);
        justPressed = Input.GetMouseButtonDown(0);
        return true;
#else
        return false;
#endif
    }

    private void OnDisable()
    {
        if (dragging)
        {
            if (hoverJelly != null) hoverJelly.SetPressed(false);
        }
        dragging = false;
    }
}
