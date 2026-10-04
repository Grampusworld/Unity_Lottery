using System.Collections.Generic;
using UnityEngine;

// 脏盘子入场动画：从屏幕右侧外飞入桌面，easeOut 减速停在桌面上一个**随机空位**，
// 飞行途中做速度驱动的挤压拉伸，落地再补一波回弹。
//
// 结构照抄 TicketFlyIn，但刻意不复用那份：票的入场和 ScratchCard / TicketDragger 耦合，
// 盘子的闸门对象是 DirtyPlate，强行合并只会让两边都变难改。
//
// 落点必须同时满足三条（世界坐标，2560x1440 相机 ortho 50 下实测）：
//   1. 整块盘子落在桌面内表面里（黑框以内 x[-28.3, 96.3] y[-37.3, 37.8]）—— 不会越过桌沿
//   2. 不压到右下角两台机器（洗盘机 + 刮彩票机，世界包围盒连贴图留白一起算）
//   3. 不压到**出票那一刻**桌子上的彩票；桌上没票时不参与
// 位置用拒绝采样：在允许矩形里随机撒中心点，压到任一禁区就重撒。
// 只避机器时可用率 78.5%，机器 + 桌上的票都在时 27.5%（离线采样 20 万次实测）；
// 48 次全被拒的概率 ~1e-7，之后还有一遍网格扫描兜底，不会再退回一个可能被占的点。
//
// 关键约束：**飞行期间这个盘子不能被海绵擦**。做法是 DirtyPlate.Ready 闸门，
// 等盘子接触桌面的那一帧才放行 —— 落地回弹还在抖的时候已经可以擦了。
//
// 缩放全程写成 baseScale * (sx, sy, sz)：Prefab 根 scale 是 (30, 30, 1)，
// 只写 x/y 才不会把 z 一起改掉。
[RequireComponent(typeof(SpriteRenderer))]
public class PlateFlyIn : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("基准时长，对应 referenceDistance 那么远的飞行距离。")]
    [SerializeField, Min(0.05f)] private float flightTime = 0.6f;
    [Tooltip("时长按 √(距离/基准) 缩放后再夹进这个区间：近距离不会拖成慢动作，远距离不会像射出去。")]
    [SerializeField, Min(0.05f)] private float minFlightTime = 0.4f;
    [SerializeField, Min(0.05f)] private float maxFlightTime = 0.71f;
    [Tooltip("基准飞行距离（世界单位）。")]
    [SerializeField, Min(1f)] private float referenceDistance = 100f;
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
    [SerializeField, Min(0.05f)] private float settleTime = 0.5f;
    [Tooltip("落地挤压的幅度。")]
    [SerializeField, Range(0f, 0.4f)] private float landingSquash = 0.15f;
    [Tooltip("落地后回弹的小跳高度（世界单位）。")]
    [SerializeField, Min(0f)] private float bounceHeight = 1.6f;
    [Tooltip("Reduce Motion 打开时时长乘以该系数。")]
    [SerializeField, Range(0.1f, 1f)] private float reducedTimeScale = 0.45f;

    [Header("Landing Zone")]
    [Tooltip("允许落点的桌面内表面（世界坐标，黑框以内）。盘子中心只在这个矩形里撒。")]
    [SerializeField] private Rect tableArea = new Rect(-28.3f, -37.3f, 124.6f, 75.1f);
    [Tooltip("盘子包围盒再乘这个系数当占位，留出果冻峰值与像素取整的余量。")]
    [SerializeField, Range(1f, 1.3f)] private float clearanceScale = 1.1f;
    [Tooltip("随机撒点次数。桌上同时有 10 张盘子 + 两台机器 + 一张票时可用率极低，\n" +
             "次数不够就会落到网格兜底、再落到出生点（多张盘子叠在一起）。")]
    [SerializeField, Range(1, 256)] private int maxAttempts = 96;

    [Header("Feedback")]
    [Tooltip("悬停果冻组件。飞行期间禁用，落地收敛那一帧才交还给玩家。")]
    [SerializeField] private HoverJelly jelly;

    public bool IsFlying { get; private set; }

    // 出生点（PlateSpawnPoint）。落点全被挡下或者拿不到盘子尺寸时退回这里。
    public Vector3 Home => home;

    // 这一帧之后的落点。给「后续盘子避开已有盘子」用：飞行中的盘子还在屏幕右边外，
    // 直接拿它的实时包围盒当禁区等于没算，必须平移到落点上才是它真实的占位。
    // 正常路径上 SetObstacles 会先调 EnsureLanding() 把落点定下来；万一还没定，
    // 这里退回当前位置让调用方算出零偏移（= 旧行为），不会更糟。
    public bool HasLanding { get; private set; }
    public Vector3 LandingPoint => HasLanding ? target : transform.position;

    // 盘子的原始缩放。以后要是有别的系统在飞行途中接手这个物体，
    // 必须用这个基准，而不是把飞行中的挤压形变当成基准。
    public Vector3 BaseScale => baseScale;

    public event System.Action Landed;

    private readonly List<Rect> obstacles = new List<Rect>();
    // 枚举空位用的临时表。做成字段而不是局部变量，是为了让这张表跨调用复用 ——
    // 每次出盘子都新建一个 List 是白扔一次分配。
    private readonly List<Vector2> cellBuffer = new List<Vector2>(64);

    private SpriteRenderer body;
    private DirtyPlate plate;
    private Vector3 baseScale = Vector3.one;
    private Vector2 footprintHalf;
    private Vector3 home;
    private Vector3 target;
    private Vector3 start;
    private float timer;
    private float flight;
    private float settle;
    private bool reduced;
    private bool started;
    private bool randomizable = true;

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        plate = GetComponent<DirtyPlate>();
        baseScale = transform.localScale;
        reduced = HoverJellySettings.ReducedMotion;

        // Instantiate 时物体还摆在 PlateSpawnPoint 上。真实落点要等 SetObstacles，
        // 那是在这一帧稍后由 LotteryGame 调的 —— 先记下出生点，等 Start 再定目标。
        home = transform.position;
        target = home;

        // 尺寸取 sprite 的子矩形（58x58，已扣掉纹理那 3px 边框），scale 30 → 17.4 世界单位。
        // 实际不透明区是 56x56 = 16.8，多出来的 2 texel 正好当安全边距 —— 和 HoverJelly
        // 的命中判定用同一套包围盒，两边不会对不上。
        Sprite sprite = body != null ? body.sprite : null;
        if (sprite == null)
        {
            Debug.LogError("[PlateFlyIn] SpriteRenderer 上没有 sprite，拿不到盘子尺寸，随机落点关闭。", this);
            randomizable = false;
        }
        else
        {
            Vector3 size = Vector3.Scale(sprite.bounds.size, baseScale);
            footprintHalf = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f * clearanceScale;
        }

        // 飞行期间既不能擦、也不能让悬停果冻来抢 localScale：
        // 同一个组件链上两个写 scale 的源同帧共存一定会互相覆盖，干脆整个飞行期把果冻关掉。
        if (plate != null) plate.Ready = false;
        if (jelly != null) jelly.enabled = false;

        IsFlying = true;
        // 先挪到屏幕外，避免在出生点上闪一帧。y 用出生点的，Start 里会按真实落点重算。
        transform.position = new Vector3(SpawnX(), home.y, home.z);
    }

    // 抽落点，**幂等**。Start 里调一次；但同一帧里被创建的多张盘子（Debug 面板连点）
    // 需要更早拿到彼此的落点 —— 后创建的那张在自己的 SetObstacles 阶段就会提前把
    // 前面几张的落点定下来。不这么做的话，同一帧创建的两张盘子都还停在屏幕外的
    // 出生点，互相看不见，会抽到同一个位置叠在一起。
    public bool TryEnsureLanding()
    {
        if (HasLanding) return true;
        if (!TryPickLandingPoint(out target)) return false;
        HasLanding = true;
        return true;
    }

    public void EnsureLanding()
    {
        if (!TryEnsureLanding())
        {
            target = home;
            HasLanding = true;
            Debug.LogWarning("[PlateFlyIn] No free landing position.", this);
        }
    }

    private void Start()
    {
        EnsureLanding();
        start = new Vector3(SpawnX(), target.y, target.z);

        float distance = Mathf.Abs(start.x - target.x);
        float scaled = Mathf.Clamp(
            flightTime * Mathf.Sqrt(distance / Mathf.Max(1f, referenceDistance)),
            minFlightTime, maxFlightTime);
        flight = reduced ? scaled * reducedTimeScale : scaled;
        settle = reduced ? settleTime * reducedTimeScale : settleTime;

        transform.position = start;
        started = true;
    }

    private void Update()
    {
        if (!started || !IsFlying) return;
        timer += Time.deltaTime;

        if (timer < flight)
        {
            Flight();
            return;
        }

        // 接触桌面这一帧就放行擦除 —— 落地回弹还在抖也不影响玩家上手擦。
        if (plate != null) plate.Ready = true;

        if (timer < flight + settle)
        {
            Landing();
            return;
        }

        transform.position = target;
        ApplyScale(1f, 1f);
        IsFlying = false;
        // 必须先写完最后一帧 scale 再开果冻，否则同一帧两个源都会写 localScale。
        if (jelly != null) jelly.enabled = true;
        if (Landed != null) Landed();
    }

    // 飞一半被打断（调试销毁、切场景）时不能把闸门卡在 false，否则这个盘子永远擦不掉。
    private void OnDisable()
    {
        if (plate != null) plate.Ready = true;
    }

    // ---- 落点 --------------------------------------------------------------

    // 由 LotteryGame 在 Instantiate 之后立刻调用：把「不该压到的世界物体」交给这里，
    // 各自取 Renderer 世界包围盒。传 null 会被跳过。
    // 机器一直算禁区（未解锁时看不见，但解锁后就在那儿，不能等那时候盘子已经压上去了）；
    // 彩票只在出票那一刻桌上有票时才排除 → 桌上空着的时候整个桌面都能用；
    // 是否把其他盘子传入由调用方决定；当前多盘玩法允许盘子互相重叠。
    public void SetObstacles(Transform[] roots)
    {
        obstacles.Clear();
        if (roots == null) return;
        for (int i = 0; i < roots.Length; i++)
        {
            Transform root = roots[i];
            if (root == null) continue;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            // 还在飞的那张盘子（MULTIPLE PLATES 解锁后桌上会有多张）：它的实时包围盒
            // 一在屏幕右边外、二带着飞行挤压的形变，两头都不对。直接用静止尺寸的占位
            // 平移到落点上 —— 那才是它落定后真正占的地方。
            PlateFlyIn sibling = root.GetComponent<PlateFlyIn>();
            if (sibling != null && sibling.IsFlying)
            {
                sibling.EnsureLanding();       // 同一帧创建的多张盘子：先把它的落点定下来
                obstacles.Add(sibling.FootprintAt(sibling.LandingPoint));
                continue;
            }

            bool any = false;
            Bounds bounds = new Bounds();
            for (int r = 0; r < renderers.Length; r++)
            {
                if (!any)
                {
                    bounds = renderers[r].bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[r].bounds);
                }
            }
            if (!any) continue;

            // 票还在飞的时候（买完票 1 秒内再点出盘子就会撞上）包围盒还在屏幕右边外，
            // 直接拿来当禁区等于没算。TicketFlyIn 暴露了落点 Home，把包围盒平移到落点上
            // 才是它真实的占位。已经落地或者被玩家拖走的票用实时包围盒 —— 那才是它现在的位置。
            Vector3 offset = Vector3.zero;
            TicketFlyIn ticket = root.GetComponent<TicketFlyIn>();
            if (ticket != null && ticket.IsFlying) offset = ticket.Home - root.position;

            obstacles.Add(new Rect(bounds.min.x + offset.x, bounds.min.y + offset.y,
                bounds.size.x, bounds.size.y));
        }
    }

    // 盘子落定后的占位矩形（世界空间），中心由参数给定。
    // 尺寸取**静止**尺寸：飞行中的挤压拉伸不参与 —— 每张盘子最终都是同一个大小，
    // 占位不该随它此刻飞得多快而变化。
    public Rect FootprintAt(Vector3 center)
    {
        return new Rect(center.x - footprintHalf.x, center.y - footprintHalf.y,
            footprintHalf.x * 2f, footprintHalf.y * 2f);
    }

    private bool TryPickLandingPoint(out Vector3 point)
    {
        point = home;
        if (!randomizable) return false;

        float xMin = tableArea.xMin + footprintHalf.x;
        float xMax = tableArea.xMax - footprintHalf.x;
        float yMin = tableArea.yMin + footprintHalf.y;
        float yMax = tableArea.yMax - footprintHalf.y;
        if (xMin >= xMax || yMin >= yMax)
        {
            Debug.LogError("[PlateFlyIn] 桌面可用区比盘子还小（桌面 " + tableArea +
                           " / 盘子占位 " + (footprintHalf * 2f) + "），退回出生点。", this);
            return false;
        }

        for (int i = 0; i < maxAttempts; i++)
        {
            float x = Random.Range(xMin, xMax);
            float y = Random.Range(yMin, yMax);
            if (Blocked(x, y)) continue;
            point = new Vector3(x, y, home.z);
            return true;
        }

        // 纯随机会被拒（桌上同时有票 + 两台机器 + 多张盘子时可用率极低），所以给一条
        // 确定性兜底：**把所有空位找出来再随机挑一个**。
        //
        // 为什么不是「拿第一个空位」：按行扫出来的第一个空位永远贴在左上角，
        // 连续加盘子会排成一条边 —— 看起来像 bug。先枚举、再随机挑，既拿到了
        // 网格扫描能到的地方（接近排布上限），又保住了「散落在桌上」的样子。
        //
        // 网格 24×16：格边 4.5×3.6 世界单位，比盘子占位（19.1）细得多，
        // 「格子空着」几乎等价于「放得下」。成本 384 次 Blocked，每次不超过
        // 18 个矩形的比较，只在出盘子那一下跑一次。
        const int GridX = 24;
        const int GridY = 16;
        cellBuffer.Clear();
        for (int gy = 0; gy < GridY; gy++)
        for (int gx = 0; gx < GridX; gx++)
        {
            float x = Mathf.Lerp(xMin, xMax, (gx + 0.5f) / GridX);
            float y = Mathf.Lerp(yMin, yMax, (gy + 0.5f) / GridY);
            if (Blocked(x, y)) continue;
            cellBuffer.Add(new Vector2(x, y));
        }

        if (cellBuffer.Count > 0)
        {
            Vector2 pick = cellBuffer[Random.Range(0, cellBuffer.Count)];
            point = new Vector3(pick.x, pick.y, home.z);
            return true;
        }

        return false;
    }

    private bool Blocked(float x, float y)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            Rect obstacle = obstacles[i];
            if (x + footprintHalf.x <= obstacle.xMin || x - footprintHalf.x >= obstacle.xMax) continue;
            if (y + footprintHalf.y <= obstacle.yMin || y - footprintHalf.y >= obstacle.yMax) continue;
            return true;
        }
        return false;
    }

    // ---- 动画 --------------------------------------------------------------

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

    // 用视口坐标算出生点：编辑器里 Screen.width 拿的是 Game 视图面板尺寸，
    // 只有 ViewportToWorldPoint 会把相机的真实 pixelRect 算进去。
    private float SpawnX()
    {
        Camera cam = Camera.main;
        if (cam == null) return home.x + 60f;
        float depth = Mathf.Abs(cam.transform.position.z - home.z);
        return cam.ViewportToWorldPoint(new Vector3(1f + spawnViewportMargin, 0.5f, depth)).x;
    }
}
