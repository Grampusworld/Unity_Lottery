using System.Collections.Generic;
using UnityEngine;

// 洗盘机运行时的水流特效：玻璃窗内的水体 + 上升气泡。
//
// 为什么这么做（三个关键取舍）：
// ① 挂成洗盘机的**子物体**（localScale = 1，继承父级 24）：贴图按 PPU 100 生成，
//    1 texel 正好等于机身 1 texel（0.24 世界单位），像素格完全对齐，不会出现
//    「水比机身像素大/小一档」的错位；并且跟随果冻缩放一起抖，像机器在震。
// ② 自由液面用「重画 Texture2D」而不是 Mask / Tiled：
//    窗口只有 90×34 = 3060 像素，而且只在**整数 texel 级别**变化时才重画
//    （实测约 10~30 次/秒），代价可以忽略。换来零 Mask（项目现在全场 0 个）、
//    零 Tiled 接缝风险，像素绝对精确。
// ③ 气泡是独立的小 sprite，画在盘子**前面**：盘子会挡住水体，但挡不住气泡。
//
// 几何来源：Automatic Dish Washer - Machine.png（128×96）里玻璃窗的深蓝区位于
// sprite 局部 x 12..101 / y 28..61，即 90×34 texel，且**正好以 sprite 中心为中心**，
// 所以 windowCenterOffset 只有半个 texel 的修正量。数值由 WasherSetup 一键菜单回填。
public class WasherWaterEffect : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpriteRenderer washerRenderer;

    [Header("Window (texel, PPU 100)")]
    [Tooltip("玻璃窗尺寸，单位是机身 texel。")]
    [SerializeField] private Vector2Int windowSize = new Vector2Int(90, 34);
    [Tooltip("玻璃窗中心相对机身轴心的偏移（机身 texel）。")]
    [SerializeField] private Vector2 windowCenterOffset = new Vector2(-0.5f, 0.5f);
    [SerializeField, Min(1f)] private float pixelsPerUnit = 100f;

    [Header("Sorting (相对机身 sortingOrder)")]
    [Tooltip("水体：要小于盘子，才在盘子后面。")]
    [SerializeField] private int bodySortingOffset = 1;
    [Tooltip("气泡：要大于盘子，才在盘子前面。")]
    [SerializeField] private int bubbleSortingOffset = 3;

    [Header("Water")]
    [SerializeField] private Color bodyColor = new Color32(72, 142, 178, 170);
    [SerializeField] private Color surfaceColor = new Color32(214, 232, 233, 255);
    [SerializeField] private Color surfaceGlowColor = new Color32(126, 196, 226, 205);
    [SerializeField] private Color streakColor = new Color32(150, 208, 232, 150);

    [Header("Surface motion")]
    [Tooltip("平均水位占窗高的比例。必须高于盘子顶部（实测盘子可见顶边在窗高的 74.5%），否则水线会整条被盘子挡住。")]
    [SerializeField, Range(0.05f, 0.95f)] private float levelBaseRatio = 0.84f;
    [Tooltip("水位上下波动幅度（texel）。")]
    [SerializeField, Min(0f)] private float levelAmplitude = 2f;
    [SerializeField, Min(0.05f)] private float levelPeriod = 2.4f;
    [Tooltip("液面每个像素列的起伏幅度（texel）。幅度越大水面越碎，1 texel 左右最像水。")]
    [SerializeField, Min(0f)] private float waveAmplitude = 1.2f;
    [Tooltip("液面波长（texel）。")]
    [SerializeField, Min(1f)] private float waveWavelength = 34f;
    [Tooltip("液面波形横向滚动速度（texel/秒）。")]
    [SerializeField] private float surfaceScrollSpeed = 13f;
    [Tooltip("水体内部条纹的流动速度（texel/秒）。")]
    [SerializeField] private float streakScrollSpeed = 21f;
    [Tooltip("每隔几行画一条流动条纹。")]
    [SerializeField, Min(2)] private int streakSpacing = 9;

    [Header("Bubbles")]
    [SerializeField, Min(0)] private int bubbleCount = 8;
    [Tooltip("上升速度范围（texel/秒）。")]
    [SerializeField] private Vector2 bubbleSpeedRange = new Vector2(3.5f, 9f);
    // 气泡画在盘子**前面**，而盘子是浅灰色 → 气泡必须偏暗才看得见。
    // 深蓝环在浅色盘子上是清晰的深色圈，在水体上比水略暗，两边都能读。
    [SerializeField] private Color bubbleColor = new Color32(46, 96, 130, 235);

    private struct BubbleState
    {
        public float baseX;
        public float speed;
        public float phase;
        public float wobblePhase;
    }

    private const int BubbleSizeTexels = 5;   // 气泡贴图边长（机身 texel）

    private Texture2D bodyTexture;
    private Color32[] pixels;
    private int[] columnLevels;
    private SpriteRenderer bodyRenderer;
    private readonly List<SpriteRenderer> bubbles = new List<SpriteRenderer>();
    private readonly List<BubbleState> bubbleStates = new List<BubbleState>();

    private int w = 90;
    private int h = 34;
    private float time;
    private bool running;
    private bool reduced;
    private int lastLevel = int.MinValue;
    private int lastSurfacePhase = int.MinValue;
    private int lastStreakPhase = int.MinValue;

    public bool IsRunning => running;

    // 1 texel 在机身局部空间里的长度（机身 sprite 是 PPU 100）。
    private float Unit => 1f / Mathf.Max(1f, pixelsPerUnit);

    private void Awake()
    {
        ResolveRenderer();
        reduced = HoverJellySettings.ReducedMotion;
        HoverJellySettings.Changed += OnSettingsChanged;

        w = Mathf.Max(2, windowSize.x);
        h = Mathf.Max(2, windowSize.y);
        pixels = new Color32[w * h];
        columnLevels = new int[w];

        BuildBodyTexture();
        BuildBubbles();
        ApplyGeometry();
        ApplySorting();

        // 先按当前水位画一帧，避免首次开启时闪一下空白。
        Rebuild(LevelAt(0f), 0, 0);
        running = false;
        ApplyVisibility();
    }

    private void OnDestroy() => HoverJellySettings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged()
    {
        reduced = HoverJellySettings.ReducedMotion;
    }

    // ---- 对外接口 ----------------------------------------------------------

    // 由 AutomaticDishWasher 驱动：只有 Washing 状态才开。
    public void SetRunning(bool value)
    {
        if (running == value) return;
        running = value;
        if (value)
        {
            time = 0f;
            lastLevel = int.MinValue;
            lastSurfacePhase = int.MinValue;
            lastStreakPhase = int.MinValue;
        }
        ApplyVisibility();
    }

    // 供一键菜单在改完 window 参数后调用，把几何/层级重新对齐。
    public void ApplyNow()
    {
        ResolveRenderer();
        ApplyGeometry();
        ApplySorting();
    }

    private void Update()
    {
        if (!running) return;

        time += Time.deltaTime;

        int level = LevelAt(time);
        int surfacePhase = Mathf.RoundToInt(surfaceScrollSpeed * time);
        // 条纹图案的周期是 3 texel，量化到 3 的倍数才不会让虚线抖动。
        int streakPhase = Mathf.FloorToInt(streakScrollSpeed * time / 3f) * 3;

        if (level != lastLevel || surfacePhase != lastSurfacePhase || streakPhase != lastStreakPhase)
        {
            lastLevel = level;
            lastSurfacePhase = surfacePhase;
            lastStreakPhase = streakPhase;
            Rebuild(level, surfacePhase, streakPhase);
        }

        UpdateBubbles(level);
    }

    // ---- 装配 --------------------------------------------------------------

    private void ResolveRenderer()
    {
        bodyRenderer = GetComponent<SpriteRenderer>();
        if (bodyRenderer == null) bodyRenderer = gameObject.AddComponent<SpriteRenderer>();
        if (washerRenderer == null) washerRenderer = GetComponentInParent<SpriteRenderer>();
    }

    private void ApplyGeometry()
    {
        transform.localScale = Vector3.one;
        transform.localPosition = new Vector3(
            windowCenterOffset.x * Unit, windowCenterOffset.y * Unit, 0f);
    }

    private void ApplySorting()
    {
        int baseLayer = washerRenderer != null ? washerRenderer.sortingLayerID : 0;
        int baseOrder = washerRenderer != null ? washerRenderer.sortingOrder : 0;

        if (bodyRenderer != null)
        {
            bodyRenderer.sortingLayerID = baseLayer;
            bodyRenderer.sortingOrder = baseOrder + bodySortingOffset;
        }
        for (int i = 0; i < bubbles.Count; i++)
        {
            if (bubbles[i] == null) continue;
            bubbles[i].sortingLayerID = baseLayer;
            bubbles[i].sortingOrder = baseOrder + bubbleSortingOffset;
        }
    }

    private void ApplyVisibility()
    {
        if (bodyRenderer != null) bodyRenderer.enabled = running;
        if (!running)
        {
            for (int i = 0; i < bubbles.Count; i++)
                if (bubbles[i] != null) bubbles[i].enabled = false;
        }
    }

    private void BuildBodyTexture()
    {
        bodyTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        bodyTexture.name = "WasherWaterBody";
        bodyTexture.filterMode = FilterMode.Point;
        bodyTexture.wrapMode = TextureWrapMode.Clamp;
        bodyTexture.hideFlags = HideFlags.DontSave;

        Sprite sprite = Sprite.Create(bodyTexture, new Rect(0f, 0f, w, h),
            new Vector2(0.5f, 0.5f), Mathf.Max(1f, pixelsPerUnit), 0, SpriteMeshType.FullRect);
        sprite.name = "WasherWaterBody";

        bodyRenderer.sprite = sprite;
        bodyRenderer.drawMode = SpriteDrawMode.Simple;
        bodyRenderer.color = Color.white;   // 颜色全部烘焙进贴图，避免整体染色
    }

    private void BuildBubbles()
    {
        Sprite bubbleSprite = BuildBubbleSprite();
        int count = Mathf.Max(1, bubbleCount);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Bubble" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = bubbleSprite;
            sr.color = bubbleColor;
            sr.enabled = false;
            bubbles.Add(sr);
            bubbleStates.Add(MakeBubbleState(i));
        }
    }

    private BubbleState MakeBubbleState(int index)
    {
        // 用下标做确定性伪随机：同一颗气泡每次运行位置一致，方便对比截图。
        float r1 = Mathf.Repeat(Mathf.Sin(index * 12.9898f) * 43758.5453f, 1f);
        float r2 = Mathf.Repeat(Mathf.Sin(index * 78.233f) * 43758.5453f, 1f);
        float r3 = Mathf.Repeat(Mathf.Sin(index * 39.425f) * 43758.5453f, 1f);
        float r4 = Mathf.Repeat(Mathf.Sin(index * 91.719f) * 43758.5453f, 1f);

        float min = Mathf.Min(bubbleSpeedRange.x, bubbleSpeedRange.y);
        float max = Mathf.Max(bubbleSpeedRange.x, bubbleSpeedRange.y);
        return new BubbleState
        {
            baseX = Mathf.Lerp(1f, w - BubbleSizeTexels - 1f, r1),
            speed = Mathf.Lerp(min, max, r2),
            phase = r3,
            wobblePhase = r4 * Mathf.PI * 2f
        };
    }

    // 5×5 环形气泡 + 一颗左上高光：像素风的经典小气泡。
    // 高光那一格是关键 —— 只有暗环会被读成「脏点」，带高光才读成气泡。
    private static Sprite BuildBubbleSprite()
    {
        const int S = BubbleSizeTexels;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.name = "WasherBubble";
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.DontSave;

        // 0 = 透明, 1 = 环体, 2 = 高光
        int[] mask =
        {
            0, 1, 1, 1, 0,
            1, 2, 0, 0, 1,
            1, 0, 0, 0, 1,
            1, 0, 0, 0, 1,
            0, 1, 1, 1, 0
        };
        Color ring = Color.white;
        Color highlight = new Color(1f, 1f, 1f, 0.55f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                int v = mask[y * S + x];
                Color c = v == 1 ? ring : (v == 2 ? highlight : new Color(1f, 1f, 1f, 0f));
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply(false);

        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, S, S),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = "WasherBubble";
        return sprite;
    }

    // ---- 水位与重画 --------------------------------------------------------

    private int LevelAt(float t)
    {
        float baseLevel = levelBaseRatio * h;
        float amplitude = levelAmplitude * (reduced ? 0.5f : 1f);
        float value = baseLevel + amplitude * Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.05f, levelPeriod));
        return Mathf.Clamp(Mathf.RoundToInt(value), 2, h);
    }

    private void Rebuild(int level, int surfacePhase, int streakPhase)
    {
        if (bodyTexture == null || pixels == null) return;

        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 body = bodyColor;
        Color32 surface = surfaceColor;
        Color32 glow = surfaceGlowColor;
        Color32 streak = streakColor;

        // 液面高度按列算：只在列级别做正弦，不是像素级别，省掉 h×w 次三角函数。
        float waveStep = Mathf.PI * 2f / Mathf.Max(1f, waveWavelength);
        for (int x = 0; x < w; x++)
        {
            float wave = waveAmplitude * Mathf.Sin((x + surfacePhase) * waveStep);
            columnLevels[x] = Mathf.Clamp(Mathf.RoundToInt(level + wave), 0, h);
        }

        int spacing = Mathf.Max(2, streakSpacing);
        for (int y = 0; y < h; y++)
        {
            int rowBase = y * w;
            bool streakRow = (y % spacing) == 0;
            for (int x = 0; x < w; x++)
            {
                int columnLevel = columnLevels[x];
                Color32 c;
                if (y >= columnLevel) c = clear;
                else if (y == columnLevel - 1) c = surface;
                // 液面下只留 1 texel 亮带：之前铺 2 texel 会变成一条很重的白杠，
                // 整扇窗子最上面 3 texel 全是亮的，看着像结冰而不是水。
                else if (y == columnLevel - 2) c = glow;
                else if (streakRow)
                {
                    // 长虚线 + 每行 2 texel 的水平错位 → 读起来是斜向流动的水流。
                    int dash = (x + streakPhase + y * 2) % 13;
                    c = (dash < 5) ? streak : body;
                }
                else c = body;
                pixels[rowBase + x] = c;
            }
        }

        bodyTexture.SetPixels32(pixels);
        bodyTexture.Apply(false);
    }

    private void UpdateBubbles(int level)
    {
        int active = reduced ? Mathf.Max(1, bubbleCount / 2) : bubbleCount;
        float unit = Unit;
        float span = Mathf.Max(2f, level - BubbleSizeTexels);

        for (int i = 0; i < bubbles.Count; i++)
        {
            SpriteRenderer sr = bubbles[i];
            bool on = running && i < active;
            if (sr == null) continue;
            sr.enabled = on;
            if (!on) continue;

            BubbleState st = bubbleStates[i];

            // 垂直：在 0..span 之间循环上升，取整到 texel 保持像素格
            float rise = Mathf.Repeat(time * st.speed / span + st.phase, 1f);
            float by = Mathf.Round(1f + rise * span);

            // 水平：左右轻微摆动，同样取整
            float bx = st.baseX + Mathf.Round(Mathf.Sin(time * 1.7f + st.wobblePhase) * 1.2f);
            bx = Mathf.Clamp(Mathf.Round(bx), 1f, w - BubbleSizeTexels - 1f);

            sr.transform.localPosition = new Vector3(
                (bx + BubbleSizeTexels * 0.5f - w * 0.5f) * unit,
                (by + BubbleSizeTexels * 0.5f - h * 0.5f) * unit,
                0f);

            // 底部淡入、接近液面淡出
            float fade = Mathf.Min(1f, rise * 6f) * Mathf.Min(1f, (1f - rise) * 5f);
            Color c = bubbleColor;
            c.a = bubbleColor.a * Mathf.Clamp01(fade);
            sr.color = c;
        }
    }
}
