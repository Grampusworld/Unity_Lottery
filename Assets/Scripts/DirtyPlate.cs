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

    // 海绵能不能擦到这个盘子。飞入动画期间为 false，接触桌面那一帧才置 true（见 PlateFlyIn）。
    //
    // 刻意不复用 enabled：那个开关已经被「脏图层贴图不可读」这条错误路径占用了，
    // 一旦共用，飞入动画的开关状态会和损坏标记互相覆盖。
    //
    // 默认 true：没挂飞入动画的盘子立刻可擦，不会因为漏挂组件就永久擦不掉。
    // 属性初始化而不是序列化字段 —— 序列化值会跟着 Prefab 走，那种默认值失效的坑项目里踩过三次。
    public bool Ready { get; set; } = true;

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

    // 参与实体碰撞：机器不能被拖到已经落桌的盘子上。
    // 盘子是运行时 Instantiate 的，编辑期没有连线对象，所以由它自己登记。
    // 不透明区就是 sprite 子矩形（58×58，已扣掉纹理那 3px 边框），与 HoverJelly 的命中区同一套。
    private void OnEnable()
    {
        SpriteRenderer body = GetComponent<SpriteRenderer>();
        if (body == null || body.sprite == null) return;
        Vector3 scale = transform.lossyScale;
        Vector3 size = Vector3.Scale(body.sprite.bounds.size, scale);
        Vector3 center = Vector3.Scale(body.sprite.bounds.center, scale);
        DragBodyRegistry.Register(this, DragBodyKind.Plate, transform, center,
            new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f);
    }

    private void OnDisable() => DragBodyRegistry.Unregister(this);

    public void ScrubAt(Vector3 worldPosition, int brushRadius)
    {
        if (!Ready || !enabled || completed || runtimeTexture == null || game == null) return;
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
