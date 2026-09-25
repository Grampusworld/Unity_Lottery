using UnityEngine;

// Erases only the separate dirt sprite. The clean plate sprite never changes.
public class DirtyPlate : MonoBehaviour
{
    [SerializeField] private SpriteRenderer dirtRenderer;
    [SerializeField, Range(0.1f, 1f)] private float cleanThreshold = 0.85f;

    private LotteryGame game;
    private Sprite originalSprite;
    private Sprite runtimeSprite;
    private Texture2D runtimeTexture;
    private Color32[] pixels;
    private int width, height, originalCount, erasedCount;
    private bool completed;

    private void Awake()
    {
        if (dirtRenderer == null || dirtRenderer.sprite == null)
        {
            Debug.LogError("DirtyPlate needs a separate dirt SpriteRenderer.", this);
            enabled = false;
            return;
        }

        originalSprite = dirtRenderer.sprite;
        if (originalSprite.packed || !originalSprite.texture.isReadable)
        {
            Debug.LogError("The plate dirt texture needs Read/Write enabled and must not be packed.", this);
            enabled = false;
            return;
        }

        Rect source = originalSprite.rect;
        width = Mathf.RoundToInt(source.width);
        height = Mathf.RoundToInt(source.height);
        runtimeTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        runtimeTexture.filterMode = FilterMode.Point;
        runtimeTexture.wrapMode = TextureWrapMode.Clamp;
        runtimeTexture.SetPixels(originalSprite.texture.GetPixels(
            Mathf.RoundToInt(source.x), Mathf.RoundToInt(source.y), width, height));
        runtimeTexture.Apply(false);
        pixels = runtimeTexture.GetPixels32();
        foreach (Color32 pixel in pixels) if (pixel.a > 0) originalCount++;
        if (originalCount == 0)
        {
            Debug.LogError("The plate dirt sprite has no visible pixels.", this);
            enabled = false;
            return;
        }

        Vector2 pivot = new Vector2(originalSprite.pivot.x / width, originalSprite.pivot.y / height);
        runtimeSprite = Sprite.Create(runtimeTexture, new Rect(0, 0, width, height),
            pivot, originalSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        dirtRenderer.sprite = runtimeSprite;
    }

    public void Initialize(LotteryGame owner) => game = owner;

    public void ScrubAt(Vector3 worldPosition, int brushRadius)
    {
        if (!enabled || completed || runtimeTexture == null || game == null) return;
        Vector3 local = dirtRenderer.transform.InverseTransformPoint(worldPosition);
        int cx = Mathf.FloorToInt(local.x * runtimeSprite.pixelsPerUnit + runtimeSprite.pivot.x);
        int cy = Mathf.FloorToInt(local.y * runtimeSprite.pixelsPerUnit + runtimeSprite.pivot.y);
        if (cx < -brushRadius || cy < -brushRadius || cx >= width + brushRadius || cy >= height + brushRadius) return;

        bool changed = false;
        for (int y = Mathf.Max(0, cy - brushRadius); y <= Mathf.Min(height - 1, cy + brushRadius); y++)
        for (int x = Mathf.Max(0, cx - brushRadius); x <= Mathf.Min(width - 1, cx + brushRadius); x++)
        {
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > brushRadius * brushRadius) continue;
            int index = y * width + x;
            if (pixels[index].a == 0) continue;
            pixels[index].a = 0;
            erasedCount++;
            changed = true;
        }
        if (!changed) return;
        runtimeTexture.SetPixels32(pixels);
        runtimeTexture.Apply(false);
        if ((float)erasedCount / originalCount < cleanThreshold) return;
        completed = true;
        dirtRenderer.enabled = false;
        game.CompletePlate(this);
    }

    private void OnDestroy()
    {
        if (dirtRenderer != null && dirtRenderer.sprite == runtimeSprite) dirtRenderer.sprite = originalSprite;
        if (runtimeSprite != null) Destroy(runtimeSprite);
        if (runtimeTexture != null) Destroy(runtimeTexture);
    }
}
