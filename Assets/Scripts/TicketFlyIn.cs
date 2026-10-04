using UnityEngine;

// 彩票入场动画：从屏幕右侧外飞入桌面，easeOut 减速停在出生点上，飞行途中做挤压拉伸的果冻形变。
//
// 关键约束：**飞行期间这张票不可被刮开、也不可被拖拽**。
// 做法是把交互开关全部关掉（ScratchCard.InputEnabled / TicketDragger.InteractionEnabled），
// 等落地回弹抖完最后一帧才交还给玩家 —— 而不是靠「位置没到就忽略输入」这种间接判断。
//
// 缩放全程写成 baseScale * (sx, sy, sz)：票 Prefab 的根 scale 是非等比的历史值
// （57.010, 54.175, 50），直接写 localScale 会把票拉变形。
[RequireComponent(typeof(SpriteRenderer))]
public class TicketFlyIn : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField, Min(0.05f)] private float flightTime = 0.25f;
    [Tooltip("出生点在屏幕右边界之外多少（视口宽度比例，0.12 = 边界外 12%）。")]
    [SerializeField, Range(0.02f, 0.6f)] private float spawnViewportMargin = 0.12f;
    [Tooltip("飞行途中的上抛弧度（世界单位）。")]
    [SerializeField, Min(0f)] private float arcHeight = 6f;

    [Header("Squash & Stretch")]
    [Tooltip("水平拉伸量 = 飞行速度 × 该系数。")]
    [SerializeField, Min(0f)] private float stretchGain = 0.085f;
    [SerializeField, Range(0f, 0.4f)] private float maxStretch = 0.15f;
    [Tooltip("垂直被压扁的比例（1 = 与水平拉伸等量）。")]
    [SerializeField, Range(0f, 1f)] private float squashRatio = 0.65f;

    [Header("Landing")]
    [SerializeField, Min(0.05f)] private float settleTime = 0.15f;
    [Tooltip("落地挤压的幅度。")]
    [SerializeField, Range(0f, 0.4f)] private float landingSquash = 0.15f;
    [Tooltip("落地后回弹的小跳高度（世界单位）。")]
    [SerializeField, Min(0f)] private float bounceHeight = 1.6f;
    [Tooltip("Reduce Motion 打开时时长乘以该系数。")]
    [SerializeField, Range(0.1f, 1f)] private float reducedTimeScale = 0.45f;

    [Header("Feedback")]
    [Tooltip("落地瞬间额外打一记果冻脉冲的对象（可空）。")]
    [SerializeField] private HoverJelly jelly;

    public bool IsFlying { get; private set; }

    // 桌面落点。注意不能直接读 transform.position：Awake 已经把物体搬到屏幕外了，
    // 而 LotteryTicket.Initialize 是在 Instantiate 之后才调用的，那时读到的会是出生点。
    public Vector3 Home => target;

    // 票的原始缩放（动画结束时 localScale 会回到它）。机器在半途收下这张票时要用它，
    // 否则会把「飞行中的挤压形变」当成基准缩放，迷你票的槽位比例就跟着歪了。
    public Vector3 BaseScale => baseScale;
    public event System.Action Landed;

    private SpriteRenderer body;
    private ScratchCard cover;
    private TicketDragger dragger;
    private Vector3 baseScale = Vector3.one;
    private Vector3 target;
    private Vector3 start;
    private float timer;
    private float flight;
    private float settle;
    private bool reduced;

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        cover = GetComponentInChildren<ScratchCard>(true);
        dragger = GetComponent<TicketDragger>();
        baseScale = transform.localScale;
        reduced = HoverJellySettings.ReducedMotion;

        // 目标就是出生位置：LotteryGame 已经把它摆到 TicketSpawnPoint 上了。
        target = transform.position;
        start = new Vector3(SpawnX(), target.y, target.z);

        flight = reduced ? flightTime * reducedTimeScale : flightTime;
        settle = reduced ? settleTime * reducedTimeScale : settleTime;

        transform.position = start;
        SetInteractable(false);
        // 飞行期间的挤压拉伸独占 localScale：果冻同帧再写一次就是两个源抢同一个属性。
        // 落地那一帧先写完 (1,1) 再把它开回来（见 Update 末尾）。
        if (jelly != null) jelly.enabled = false;
        IsFlying = true;
    }

    private void Update()
    {
        if (!IsFlying) return;
        timer += Time.deltaTime;

        if (timer < flight)
        {
            Flight();
            return;
        }
        if (timer < flight + settle)
        {
            Landing();
            return;
        }

        transform.position = target;
        ApplyScale(1f, 1f);
        IsFlying = false;
        SetInteractable(true);
        // 必须先写完最后一帧 scale 再开果冻，否则同一帧两个源都会写 localScale。
        if (jelly != null)
        {
            jelly.enabled = true;
            jelly.Pulse(0.9f, 0.22f);     // 落地这一记脉冲照旧
        }
        if (Landed != null) Landed();
    }

    private void Flight()
    {
        float k = Mathf.Clamp01(timer / flight);
        float ease = 1f - Mathf.Pow(1f - k, 3f);        // easeOutCubic：出发快、到位稳
        float x = Mathf.Lerp(start.x, target.x, ease);
        float y = Mathf.Lerp(start.y, target.y, ease) + Mathf.Sin(k * Mathf.PI) * arcHeight;
        transform.position = new Vector3(x, y, target.z);

        if (reduced)
        {
            ApplyScale(1f, 1f);
            return;
        }
        // easeOutCubic 的导数：把「还剩多少路」换算成速度，速度越快拉得越长。
        float speed = 3f * (1f - k) * (1f - k) / flight;
        float stretch = Mathf.Clamp(speed * stretchGain, 0f, maxStretch);
        ApplyScale(1f + stretch, 1f - stretch * squashRatio);
    }

    private void Landing()
    {
        if (reduced)
        {
            transform.position = target;
            ApplyScale(1f, 1f);
            return;
        }
        float u = Mathf.Clamp01((timer - flight) / settle);
        float decay = (1f - u) * (1f - u);
        float wave = Mathf.Sin(u * Mathf.PI * 2.5f);     // 2.5 个半周期：挤压 - 拉伸 - 挤压…逐渐收敛

        // 回弹是往上跳，不是往下陷桌面。
        transform.position = new Vector3(target.x,
            target.y + Mathf.Abs(wave) * decay * bounceHeight, target.z);

        float q = wave * decay;
        ApplyScale(1f + q * landingSquash, 1f - q * landingSquash);
    }

    private void ApplyScale(float sx, float sy)
    {
        transform.localScale = new Vector3(baseScale.x * sx, baseScale.y * sy, baseScale.z);
    }

    private void SetInteractable(bool on)
    {
        if (cover != null) cover.InputEnabled = on;
        if (dragger != null) dragger.InteractionEnabled = on;
    }

    // 用视口坐标算出生点：编辑器里 Screen.width 拿的是 Game 视图面板尺寸，
    // 只有 ViewportToWorldPoint 会把相机的真实 pixelRect 算进去。
    private float SpawnX()
    {
        Camera cam = Camera.main;
        if (cam == null) return target.x + 60f;
        float depth = Mathf.Abs(cam.transform.position.z - target.z);
        return cam.ViewportToWorldPoint(new Vector3(1f + spawnViewportMargin, 0.5f, depth)).x;
    }
}
