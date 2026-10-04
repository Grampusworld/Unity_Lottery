using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 把票拖进自动刮彩票机的手势。与「左键划过刮奖」共用一个鼠标键，靠**按在票面哪一块**分流：
//
//   按在票的**外框**（涂层外面那一圈）→ 立刻拿起（票抬起、放大、可以拖）
//   按在**涂层**上（票面中间那块，实测 96×40 texel）→ 只刮奖，这一按不归拖动
//
// 2026-09-29 改：原来靠「按住不动 0.12s」分流（0.12s 是响应速度与误拿率的折中），
// 玩家反馈拖不动。现在改成**空间分流**，拖动按下即抓（与海绵/盘子/机器一致），
// 而刮奖手感**完全不变** —— 涂层区本来就不该被拖走，边框区本来也刮不出东西。
// 「涂层实体的范围」由 ScratchCard 量出来（`ScratchCard.CoversPoint`），两边同一个来源；
// **不要在这里手抄一份尺寸**，涂层素材一改就对不上。
//
// 松手时指针若在机器上就投料；否则**留在松手的地方**（2026-09-29 改，原来飞回桌面落点）。
[RequireComponent(typeof(SpriteRenderer))]
public class TicketDragger : MonoBehaviour
{
    [Header("Hold to grab")]
    [Tooltip("按住多久才算「拿起」。**0 = 按下即抓**（2026-09-29 起）。\n" +
             "票与刮奖的分流现在靠**按在票面哪一块**（涂层 = 刮奖，外框 = 拖动），不靠时间，\n" +
             "所以这个值不再参与。保留字段与「按住不动」那条路，是为了万一还想加回时间判据。\n" +
             "⚠️ 若把刮奖改回「按下即刮」，这里就必须重新设一个 > 0 的值，否则两个手势会打架。")]
    [SerializeField, Min(0f)] private float holdTime = 0f;
    [Tooltip("按住期间允许的最大位移（世界单位），超过就判定成刮奖。只在 holdTime > 0 时参与判定。")]
    [SerializeField, Min(0.01f)] private float moveTolerance = 0.45f;

    [Header("Drag")]
    [Tooltip("拿起时的额外放大（纯视觉，命中区不变）。")]
    [SerializeField, Range(1f, 1.3f)] private float liftScale = 1.06f;
    [Tooltip("拖到机器可收范围上时的提示缩小（相对桌面基准）。判定与 TryFeedTicket 同源，缩了就一定收得下。")]
    [SerializeField, Range(0.4f, 1f)] private float dropHintScale = 0.75f;
    [Tooltip("提示缩放的跟随速度，越大过渡越快。")]
    [SerializeField, Min(1f)] private float hintLerp = 14f;
    [Tooltip("拖拽期间提升的渲染层数，保证票盖在机器上面。")]
    [SerializeField, Min(0)] private int dragSortingBoost = 12;

    [Header("References")]
    [SerializeField] private Camera inputCamera;
    [SerializeField] private HoverJelly jelly;
    [Tooltip("留空自动在本物体上找；找不到就没有惯性（松手即停）。")]
    [SerializeField] private DragInertia inertia;

    public bool InteractionEnabled { get; set; } = true;
    public bool IsGrabbed => grabbed;

    private SpriteRenderer body;
    private ScratchCard cover;
    private SpriteRenderer coverRenderer;
    private LotteryTicket ticket;
    private LotteryGame game;
    private TicketFlyIn flyIn;
    private SpongeDrag sponge;            // 桌面几何的唯一来源（TableSurface / TabletopPixels）
    private Vector3 baseScale = Vector3.one;
    private Vector3 pressStart;
    private Vector3 grabOffset;           // 按下点 → 票 transform.position，保证拿起时不跳
    private float holdTimer;
    private bool holding;
    private bool grabbed;
    private int bodyOrder, coverOrder;

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        cover = GetComponentInChildren<ScratchCard>(true);
        coverRenderer = cover != null ? cover.GetComponent<SpriteRenderer>() : null;
        flyIn = GetComponent<TicketFlyIn>();
        baseScale = transform.localScale;
        if (inputCamera == null) inputCamera = Camera.main;
        if (body != null) bodyOrder = body.sortingOrder;
        if (coverRenderer != null) coverOrder = coverRenderer.sortingOrder;
        if (inertia == null) inertia = GetComponent<DragInertia>();
        // 夹取回调与 SpongeDrag / MachineDrag 同一套语义：入参是 transform.position，
        // 所以位移也必须按 pivot 记（机器踩过「用可见内容中心记 → 按下后整机跳一段」的坑）。
        if (inertia != null) inertia.Clamp = ClampToTable;
        RegisterBody();
    }

    // 登记成「票」。票必须压过按下即拖的机器与海绵 ——
    // 不让路的话票一旦被压在机器上就永远抓不回来（软锁）。
    // 这里只登记、**不参与碰撞**：票不与任何东西互斥（投喂判定用的是光标位置，不受阻挡影响）。
    private void RegisterBody()
    {
        if (body == null || body.sprite == null) return;
        Vector3 size = Vector3.Scale(body.sprite.bounds.size, baseScale);
        Vector3 center = Vector3.Scale(body.sprite.bounds.center, baseScale);
        DragBodyRegistry.Register(this, DragBodyKind.Ticket, transform, center,
            new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f);
    }

    private void OnEnable() => RegisterBody();

    public void Initialize(LotteryGame owner, LotteryTicket ownerTicket)
    {
        game = owner;
        ticket = ownerTicket;
        // 2026-09-29：这里原来会缓存 home（飞入落点），因为松手要飞回去。
        // 现在松手留原地、漂移只在 DragInertia 里，home 已经没有任何读者 —— 删掉而不是留着骗人。
        InteractionEnabled = !(flyIn != null && flyIn.IsFlying);
    }

    private void Update()
    {
        if (MainMenuScreen.GameplayBlocked)
        {
            holding = false;
            holdTimer = 0f;
            if (grabbed)
            {
                grabbed = false;
                RestoreOrders();
                transform.localScale = baseScale;
                if (cover != null) cover.InputEnabled = true;
                if (jelly != null) jelly.enabled = true;
                if (inertia != null) inertia.Stop();
            }
            return;
        }
        if (game == null || inputCamera == null) return;
        if (!ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)) return;
        Vector3 world = ScreenToWorld(screen);

        // 滑行期间松手了也不能碰 position：position 归 DragInertia 独占。
        // 同帧两个源写同一个属性一定会互相覆盖，症状是滑到一半被拽回原地。（与 SpongeDrag 一致）
        if (!grabbed && inertia != null && inertia.Sliding) return;

        if (!pressed)
        {
            if (grabbed) Drop(world);
            holding = false;
            holdTimer = 0f;
            return;
        }

        if (grabbed)
        {
            Follow(world);
            return;
        }

        if (!holding)
        {
            if (!justPressed || !InteractionEnabled) return;
            if (body == null || body.sprite == null) return;
            // 仲裁：票必须压过按下的机器与海绵（票画在机器下面，不让就会被机器压死、抓不回来）。
            // 机器/海绵那边也必须让给票 —— 缺一边就会两个一起被拖。判据集中在 DragBodyRegistry.Claimant。
            if (DragBodyRegistry.Claimant(world) != DragBodyKind.Ticket) return;
            // 命中基准是静止尺寸的包围盒，入场动画的缩放不会撑大命中区。
            if (!HoverJelly.ContainsPointUnscaled(body.transform, body.sprite.bounds, world, baseScale)) return;
            // ★ 与刮奖的分流：按在**涂层实体**上就是「想刮奖」，这一按不归拖动。
            // 把手 = 涂层外面那一圈（票的边框）。范围问 ScratchCard 自己（它是量不透明区的那一方），
            // 这里不手抄尺寸 —— 涂层素材一改，两边就会对不上。
            if (cover != null && cover.CoversPoint(world)) return;
            // holdTime <= 0 = 按下即抓（当前就是 0，分流已由上面那条空间判定承担）。
            if (holdTime <= 0f)
            {
                Grab(world);
                return;
            }
            holding = true;
            holdTimer = 0f;
            pressStart = world;
            return;
        }

        holdTimer += Time.deltaTime;
        if (Vector3.Distance(world, pressStart) > moveTolerance)
        {
            // 是刮奖，不是搬运。
            holding = false;
            return;
        }
        if (holdTimer >= holdTime) Grab(world);
    }

    private void Grab(Vector3 world)
    {
        grabbed = true;
        LotterySfx.Play(LotterySfx.Sound.DragStart);
        holding = false;
        if (inertia != null) inertia.BeginDrag();     // 新按下取消正在进行的滑行
        if (cover != null) cover.InputEnabled = false;
        // 顺序要紧：先挂起果冻**再**写 scale。`jelly.enabled = false` 会同步触发 OnDisable →
        // ResetToRest 写一次 localScale（复位成基准值），写在抬起之后就会把 1.06 抹掉。
        // 抬起期间的 localScale 归本组件独占：1.06 是个**持续**状态，弹簧脉冲表达不了它，
        // 果冻一写就会把它覆盖掉（症状是「抬起又马上缩回去」）。
        // 那一记「拿起来弹一下」挪到松手时（Drop）—— 语义上正好是放下时落一下。
        if (jelly != null) jelly.enabled = false;
        if (body != null) body.sortingOrder = bodyOrder + dragSortingBoost;
        if (coverRenderer != null) coverRenderer.sortingOrder = coverOrder + dragSortingBoost;
        // 抓住的**点**保持在光标下（与 SpongeDrag 同一套）。不记这个偏移的话，
        // 拿起那一刻票心会瞬移到光标上 —— 就是玩家报的「按下突然居中跳到鼠标位置」。
        grabOffset = transform.position - world;
        transform.localScale = new Vector3(baseScale.x * liftScale, baseScale.y * liftScale, baseScale.z);
    }

    private void Follow(Vector3 world)
    {
        // 直接跟手，不做平滑（与 SpongeDrag 一致）：原来这里是 Lerp(…, 1-exp(-26·dt))，
        // 60fps 下每帧只走 34%，快速拖动会明显拖尾。偏移按 pivot 记，所以全程贴着光标不跳。
        transform.position = ClampToTable(world + grabOffset);
        if (inertia != null) inertia.TrackDrag();

        // 投放提示：指针在机器的可收范围上 → 票平滑缩到 dropHintScale 示意「可以松手」；
        // 离开 → 回到抬起尺寸。判定与 TryFeedTicket 完全同源（同 margin、同容量/解锁检查），
        // 所以「缩着」就等于「松手必收」。缩放写者仍归本组件独占（果冻在 Grab 时已挂起）。
        bool hint = game.CanFeedAt(world);
        float f = 1f - Mathf.Exp(-hintLerp * Time.unscaledDeltaTime);
        Vector3 goal = baseScale * (hint ? dropHintScale : liftScale);
        transform.localScale = Vector3.Lerp(transform.localScale, goal, f);
    }

    private void Drop(Vector3 world)
    {
        grabbed = false;
        RestoreOrders();
        transform.localScale = baseScale;
        if (cover != null) cover.InputEnabled = true;

        if (game.TryFeedTicket(ticket, world))
        {
            LotterySfx.Play(LotterySfx.Sound.ManualFeed);
            // 已被机器接收：机器会接管这个 GameObject（AutoScratcher.TryAccept 会把果冻关掉），
            // 本组件到此为止，不要再把果冻开回来。惯性必须先停 —— 机器随后自己写 position，
            // 留着滑行就是两个源抢同一个属性。
            if (inertia != null) inertia.Stop();
            enabled = false;
            return;
        }

        LotterySfx.Play(LotterySfx.Sound.Drop);
        // 没投进机器 → **留在松手的地方**（2026-09-29 改：原来飞回 home，等于把玩家刚才那次拖动
        // 整个否掉，玩家报「松手就弹回原位、停不住」）。但与海绵一致：先交给惯性滑行。
        if (inertia != null) inertia.Release();
        // 滑不动（速度低于 stopSpeed）就地夹一次；滑行时夹取由 inertia.Clamp 每帧做。
        // 不夹的话票能被丢到桌子外面 —— 它在 DragBodyRegistry 里不和任何东西碰撞。
        if (inertia == null || !inertia.Sliding) transform.position = ClampToTable(transform.position);

        // 交还给果冻：先写完 baseScale 再开，同帧两个源写 localScale 一定会互相覆盖。
        if (jelly != null)
        {
            jelly.enabled = true;
            jelly.Pulse(1f, 0.16f);
        }
    }

    // 桌面几何只有一份来源（SpongeDrag 实例），与 PlateDragger / MachineDrag 同一套，
    // 不手抄第二份 —— 抄了以后改桌面美术就会对不上。
    private Vector3 ClampToTable(Vector3 world)
    {
        if (body == null || body.sprite == null || inputCamera == null) return world;
        if (sponge == null) sponge = UnityEngine.Object.FindAnyObjectByType<SpongeDrag>();

        SpriteRenderer table = sponge != null ? sponge.TableSurface : null;
        Rect pixels = sponge != null ? sponge.TabletopPixels : default(Rect);
        Bounds area = DragBounds.Playable(table, pixels, inputCamera, world.z);

        Vector3 size = Vector3.Scale(body.sprite.bounds.size, baseScale);
        Vector2 half = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f;
        // margin 用 0，只保证「票心在桌面上」（永远抓得回来），不做整卡内缩。
        // 票宽 62.2 而桌面才 124.6：照海绵那套 0.75 内缩会把票心的可用范围压到 ~76 宽，
        // 连洗盘机（x≈78）都够不着，落在边上会整卡瞬移一段。
        return DragBounds.ClampCenter(world, half, 0f, area);
    }

    private void RestoreOrders()
    {
        if (body != null) body.sortingOrder = bodyOrder;
        if (coverRenderer != null) coverRenderer.sortingOrder = coverOrder;
    }

    private Vector3 ScreenToWorld(Vector2 screen)
    {
        Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y,
            Mathf.Abs(inputCamera.transform.position.z - transform.position.z)));
        world.z = transform.position.z;
        return world;
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
        holding = grabbed = false;
        // 被关掉就不要再让惯性推 position：接管方（机器槽位动画 / ShrinkOut 的退场）
        // 会自己写这个属性，两个源同帧写同一个属性就是互相覆盖。
        if (inertia != null) inertia.Stop();
        RestoreOrders();
        transform.localScale = baseScale;
        if (cover != null) cover.InputEnabled = true;
        DragBodyRegistry.Unregister(this);
    }
}
