using UnityEngine;

// 自动刮彩票机「正在运行」的像素特效，画在工作区（机身 sprite 的一段矩形）里：
//
//   ① 左右往复的刮擦头（竖条），扫过机内那叠迷你票
//   ② 从刮擦头位置不断溅出、受重力下落的纸屑
//   ③ 一张票结算完成时的爆点（向外飞散的碎屑）
//
// 与洗盘机的水流特效同一套做法：贴图运行时生成、按整数 texel 重画、零 Mask、零粒子系统、
// 零新增美术资源。特效是机身的**子物体**且 localScale = 1，才能继承机身缩放、共用像素格。
public class ScratcherEffect : MonoBehaviour
{
    [Header("Geometry")]
    [Tooltip("机身 sprite 的像素密度，必须与机身一致，否则特效格与机身格对不上。")]
    [SerializeField, Min(1f)] private float pixelsPerUnit = 100f;
    [Tooltip("排序在机身之上的偏移：0 = 贴着机身，要盖住迷你票就取正数。")]
    [SerializeField, Min(0f)] private float sortingOffset = 3f;

    [Header("Sweep")]
    [Tooltip("刮擦头一个来回的时长。")]
    [SerializeField, Min(0.05f)] private float sweepPeriod = 0.62f;
    [SerializeField, Min(1)] private int sweepWidth = 3;
    [SerializeField] private Color sweepColor = new Color32(226, 244, 255, 240);
    [SerializeField] private Color sweepEdgeColor = new Color32(104, 178, 235, 215);
    [Tooltip("刮擦头残留的横向划痕条数。")]
    [SerializeField, Min(0)] private int streakRows = 3;
    [SerializeField] private Color streakColor = new Color32(150, 205, 240, 120);

    [Header("Sparks")]
    [SerializeField, Min(0)] private int sparkCount = 12;
    [SerializeField] private Color sparkColor = new Color32(255, 238, 186, 255);
    [SerializeField] private Color sparkDimColor = new Color32(198, 166, 96, 220);
    [Tooltip("纸屑下落加速度（texel / 秒²）。")]
    [SerializeField] private float gravity = 46f;
    [Tooltip("纸屑单次寿命（秒）。")]
    [SerializeField, Min(0.05f)] private float sparkLife = 0.5f;
    [Tooltip("刮擦头每周期甩出的纸屑数。")]
    [SerializeField, Min(0)] private int sparkPerCycle = 6;

    [Header("Burst")]
    [SerializeField, Min(0)] private int burstCount = 18;
    [SerializeField] private float burstSpeed = 26f;
    [SerializeField, Min(0.05f)] private float burstDuration = 0.4f;

    [Header("Reduce Motion")]
    [SerializeField, Range(0.1f, 1f)] private float reducedSparkFactor = 0.5f;
    [Tooltip("Reduce Motion 下放慢的刮擦头速度系数。")]
    [SerializeField, Range(0.3f, 1f)] private float reducedSpeedFactor = 0.7f;

    private struct Spark
    {
        public float x, y, vx, vy, life;
        public bool bright;
    }

    private SpriteRenderer target;
    private Texture2D texture;
    private Sprite sprite;
    private Color32[] buffer;

    private int width = 8;
    private int height = 8;
    private bool running;
    private bool reduced;
    private float sweepTimer;
    private float burstTimer = -1f;

    private Spark[] sparks;
    private int nextSpark;

    private void Awake()
    {
        target = GetComponent<SpriteRenderer>();
        reduced = HoverJellySettings.ReducedMotion;
        Rebuild(width, height);
        target.sprite = sprite;
        target.enabled = false;
    }

    // ---- 对外接口 ----------------------------------------------------------

    // 由 AutoScratcher 调用：把特效对齐到当前等级的工作区。
    // localCenter / localSize 都是机身局部空间（机身 sprite 的局部单位，1 单位 = 1/PPU 像素）。
    public void SetWorkRect(Vector2 localCenter, Vector2 localSize, SpriteRenderer machineRenderer)
    {
        int w = Mathf.Max(2, Mathf.RoundToInt(localSize.x * pixelsPerUnit));
        int h = Mathf.Max(2, Mathf.RoundToInt(localSize.y * pixelsPerUnit));
        transform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);

        if (target == null) target = GetComponent<SpriteRenderer>();
        if (machineRenderer != null)
        {
            target.sortingLayerID = machineRenderer.sortingLayerID;
            target.sortingOrder = Mathf.RoundToInt(machineRenderer.sortingOrder + sortingOffset);
        }

        if (w != width || h != height) Rebuild(w, h);
    }

    public void SetRunning(bool on)
    {
        if (running == on) return;
        running = on;
        if (target != null) target.enabled = on;
        if (on)
        {
            sweepTimer = 0f;
            SpawnBurst(0.35f);   // 开头甩一小撮，让「开机」这一下看得见
        }
        else
        {
            burstTimer = -1f;
            ClearBuffer();
        }
    }

    public void Burst()
    {
        SpawnBurst(1f);
    }

    // ---- 重画 --------------------------------------------------------------

    private void Rebuild(int w, int h)
    {
        // 换等级时工作区尺寸会变，旧贴图必须自己销毁，否则每次换级都漏一张。
        if (sprite != null) { if (Application.isPlaying) Destroy(sprite); else DestroyImmediate(sprite); }
        if (texture != null) { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }

        width = w;
        height = h;
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        buffer = new Color32[width * height];
        ClearBuffer();
        texture.SetPixels32(buffer);
        texture.Apply(false);

        sprite = Sprite.Create(texture, new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        if (target != null) target.sprite = sprite;

        int count = Mathf.Max(1, reduced ? Mathf.RoundToInt(sparkCount * reducedSparkFactor) : sparkCount);
        sparks = new Spark[count + Mathf.Max(1, burstCount)];
        for (int i = 0; i < sparks.Length; i++) sparks[i].life = 0f;
        nextSpark = 0;
    }

    private void ClearBuffer()
    {
        if (buffer == null) return;
        Color32 clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < buffer.Length; i++) buffer[i] = clear;
    }

    private void Update()
    {
        if (!running || texture == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        ClearBuffer();

        float speed = reduced ? reducedSpeedFactor : 1f;
        float period = sweepPeriod / Mathf.Max(0.05f, speed);
        sweepTimer += dt;

        float barT = Mathf.PingPong(sweepTimer / period, 1f);   // 0→1→0
        int barX = Mathf.Clamp(Mathf.RoundToInt(barT * (width - 1)), 0, width - 1);

        DrawStreaks(barX);
        DrawSweep(barX);
        EmitSparks(barX, dt);
        StepSparks(dt);

        texture.SetPixels32(buffer);
        texture.Apply(false);
    }

    private void DrawStreaks(int barX)
    {
        if (streakRows <= 0 || width <= 2) return;
        int half = Mathf.Max(1, width / 2 - 1);
        int from = Mathf.Max(0, barX - 1);
        int to = Mathf.Min(width - 1, barX + half);
        for (int r = 0; r < streakRows; r++)
        {
            // 划痕固定在几个高度上，画在刮擦头「扫过之后」的那一侧。
            int y = Mathf.RoundToInt((r + 1f) / (streakRows + 1f) * (height - 1));
            for (int x = from; x <= to; x++)
            {
                int sub = (x + r) % 2;
                if (sub != 0) continue;
                Set(x, y, streakColor);
            }
        }
    }

    private void DrawSweep(int barX)
    {
        int half = Mathf.Max(0, sweepWidth / 2);
        for (int y = 0; y < height; y++)
        {
            for (int dx = -half; dx <= half; dx++)
            {
                int x = barX + dx;
                if (x < 0 || x >= width) continue;
                Set(x, y, dx == 0 ? sweepColor : sweepEdgeColor);
            }
        }
    }

    private void EmitSparks(int barX, float dt)
    {
        if (sparks == null || sparkPerCycle <= 0) return;
        // 按时间密度发射，与帧率无关。
        float perSecond = sparkPerCycle / Mathf.Max(0.1f, sweepPeriod);
        int emit = Mathf.FloorToInt(perSecond * dt + Random.value);
        for (int i = 0; i < emit; i++)
        {
            int idx = NextFree();
            if (idx < 0) return;
            sparks[idx].x = barX + Random.Range(-1.5f, 1.5f);
            sparks[idx].y = Random.Range(height * 0.25f, height * 0.85f);
            sparks[idx].vx = Random.Range(-14f, 14f);
            sparks[idx].vy = Random.Range(6f, 22f);
            sparks[idx].life = sparkLife * Random.Range(0.6f, 1f);
            sparks[idx].bright = Random.value > 0.45f;
        }
    }

    private void SpawnBurst(float scale)
    {
        if (sparks == null || burstCount <= 0) return;
        int n = Mathf.Max(1, Mathf.RoundToInt(burstCount * scale));
        float cx = width * 0.5f;
        float cy = height * 0.5f;
        for (int i = 0; i < n; i++)
        {
            int idx = NextFree();
            if (idx < 0) return;
            float angle = (float)i / n * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            float speed = burstSpeed * Random.Range(0.5f, 1f) * scale;
            sparks[idx].x = cx;
            sparks[idx].y = cy;
            sparks[idx].vx = Mathf.Cos(angle) * speed;
            sparks[idx].vy = Mathf.Sin(angle) * speed;
            sparks[idx].life = burstDuration * Random.Range(0.7f, 1f);
            sparks[idx].bright = true;
        }
        burstTimer = burstDuration;
    }

    private void StepSparks(float dt)
    {
        if (sparks == null) return;
        for (int i = 0; i < sparks.Length; i++)
        {
            if (sparks[i].life <= 0f) continue;
            sparks[i].life -= dt;
            sparks[i].vy -= gravity * dt;
            sparks[i].x += sparks[i].vx * dt;
            sparks[i].y += sparks[i].vy * dt;

            if (sparks[i].life <= 0f) continue;
            int x = Mathf.RoundToInt(sparks[i].x);
            int y = Mathf.RoundToInt(sparks[i].y);
            if (x < 0 || x >= width || y < 0 || y >= height) continue;
            Set(x, y, sparks[i].bright ? sparkColor : sparkDimColor);
        }
        if (burstTimer > 0f) burstTimer -= dt;
    }

    private int NextFree()
    {
        for (int i = 0; i < sparks.Length; i++)
        {
            int idx = (nextSpark + i) % sparks.Length;
            if (sparks[idx].life <= 0f)
            {
                nextSpark = (idx + 1) % sparks.Length;
                return idx;
            }
        }
        return -1;
    }

    private void Set(int x, int y, Color32 color)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        buffer[y * width + x] = color;
    }

    private void OnDestroy()
    {
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
    }
}
