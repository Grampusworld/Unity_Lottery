using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 教学版：单张、未加入 Sprite Atlas 的涂层，使用 SpriteRenderer 的 Simple 模式。
// 只修改运行时副本，不会把原始 PNG 擦掉。支持桌面鼠标。
[RequireComponent(typeof(SpriteRenderer))]
public class ScratchCard : MonoBehaviour
{
    [SerializeField] private Camera inputCamera;
    [SerializeField, Min(1)] private int brushRadius = 5;
    [SerializeField, Range(0.1f, 1f)] private float revealThreshold = 0.8f;
    [SerializeField] private UnityEvent onRevealed = new UnityEvent();

    private SpriteRenderer cover;
    private Sprite originalSprite;
    private Sprite runtimeSprite;
    private Texture2D runtimeTexture;
    private Color32[] pixels;
    private int width, height, originalCount, erasedCount;
    private Vector2? previousPixel;
    private bool revealed;

    public float Progress => originalCount == 0 ? 0f : (float)erasedCount / originalCount;

    private void Start()
    {
        cover = GetComponent<SpriteRenderer>();
        originalSprite = cover.sprite;
        if (inputCamera == null) inputCamera = Camera.main;

        if (originalSprite == null || inputCamera == null)
        {
            Debug.LogError("ScratchCard：请设置涂层 Sprite 和 Input Camera。", this);
            enabled = false;
            return;
        }
        if (originalSprite.packed || !originalSprite.texture.isReadable)
        {
            Debug.LogError("ScratchCard：涂层须开启 Read/Write，且暂时不要加入 Sprite Atlas。", this);
            enabled = false;
            return;
        }

        // 复制 Sprite 所在的像素区域，保留原来的锚点和像素密度。
        Rect rect = originalSprite.rect;
        width = Mathf.RoundToInt(rect.width);
        height = Mathf.RoundToInt(rect.height);
        runtimeTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        runtimeTexture.filterMode = FilterMode.Point;
        runtimeTexture.wrapMode = TextureWrapMode.Clamp;
        runtimeTexture.SetPixels(originalSprite.texture.GetPixels(
            Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y), width, height));
        runtimeTexture.Apply(false);
        pixels = runtimeTexture.GetPixels32();
        foreach (Color32 pixel in pixels)
            if (pixel.a > 0) originalCount++;

        Vector2 pivot = new Vector2(originalSprite.pivot.x / width, originalSprite.pivot.y / height);
        runtimeSprite = Sprite.Create(runtimeTexture, new Rect(0, 0, width, height),
            pivot, originalSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        cover.sprite = runtimeSprite;

        if (originalCount == 0)
        {
            Debug.LogError("ScratchCard：涂层图片完全透明，请检查素材。", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (revealed || runtimeTexture == null || !cover.enabled) return;

        if (!ReadMouse(out Vector2 screenPosition) ||
            !TryGetPixel(screenPosition, out Vector2 currentPixel))
        {
            previousPixel = null;
            return;
        }

        // 在上一帧和这一帧之间补点，避免快速移动时出现断续的圆点。
        Vector2 from = previousPixel ?? currentPixel;
        int radius = Mathf.Max(1, brushRadius);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, currentPixel)
            / Mathf.Max(1f, radius * 0.5f)));
        bool changed = false;
        for (int i = 0; i <= steps; i++)
            changed |= EraseCircle(Vector2.Lerp(from, currentPixel, (float)i / steps), radius);
        previousPixel = currentPixel;

        if (!changed) return;
        runtimeTexture.SetPixels32(pixels);
        runtimeTexture.Apply(false);

        if (Progress >= revealThreshold)
        {
            revealed = true;
            cover.enabled = false;
            Debug.Log("刮奖完成！", this);
            onRevealed.Invoke(); // 以后在这里连接结算或播放音效。
        }
    }

    private bool ReadMouse(out Vector2 position)
    {
        position = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        position = Mouse.current.position.ReadValue();
        return Mouse.current.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        position = Input.mousePosition;
        return Input.GetMouseButton(0);
#else
        return false;
#endif
    }

    private bool TryGetPixel(Vector2 screenPosition, out Vector2 pixel)
    {
        pixel = Vector2.zero;
        if (inputCamera == null || !inputCamera.pixelRect.Contains(screenPosition)) return false;
        Ray ray = inputCamera.ScreenPointToRay(screenPosition);
        Plane plane = new Plane(transform.forward, transform.position);
        if (!plane.Raycast(ray, out float distance)) return false;

        Vector3 local = transform.InverseTransformPoint(ray.GetPoint(distance));
        if (cover.flipX) local.x = -local.x;
        if (cover.flipY) local.y = -local.y;
        pixel = new Vector2(local.x, local.y) * runtimeSprite.pixelsPerUnit + runtimeSprite.pivot;
        return pixel.x >= 0 && pixel.x < width && pixel.y >= 0 && pixel.y < height;
    }

    private bool EraseCircle(Vector2 center, int radius)
    {
        bool changed = false;
        int cx = Mathf.FloorToInt(center.x), cy = Mathf.FloorToInt(center.y);
        for (int y = Mathf.Max(0, cy - radius); y <= Mathf.Min(height - 1, cy + radius); y++)
        for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(width - 1, cx + radius); x++)
        {
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > radius * radius) continue;
            int index = y * width + x;
            if (pixels[index].a == 0) continue;
            pixels[index].a = 0;
            erasedCount++;
            changed = true;
        }
        return changed;
    }

    private void OnDisable() => previousPixel = null;

    private void OnDestroy()
    {
        if (cover != null && cover.sprite == runtimeSprite) cover.sprite = originalSprite;
        if (runtimeSprite != null) Destroy(runtimeSprite);
        if (runtimeTexture != null) Destroy(runtimeTexture);
    }
}
