using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 把票拖进自动刮彩票机的手势。与「左键划过刮奖」共用一个鼠标键，靠**按住不动 0.12 秒**分流：
//
//   按住 → 指针几乎不动满 0.12s  →  拿起（票抬起、放大、可以拖）
//   按住 → 一旦移动超过容差     →  判定为刮奖，本组件立刻放手，ScratchCard 照常工作
//
// 0.12s 是响应速度与误拿率的折中：再短的话，玩家刮到涂层边角或来回蹭时的无意识
// 停顿就会被误判成「想拿起」，票突然抬起把刮奖输入抢走。
// 松手时指针若在机器上就投料，否则飞回桌面落点。
[RequireComponent(typeof(SpriteRenderer))]
public class TicketDragger : MonoBehaviour
{
    [Header("Hold to grab")]
    [Tooltip("按住多久才算「拿起」。")]
    [SerializeField, Min(0.05f)] private float holdTime = 0.12f;
    [Tooltip("按住期间允许的最大位移（世界单位），超过就判定成刮奖。")]
    [SerializeField, Min(0.01f)] private float moveTolerance = 0.45f;

    [Header("Drag")]
    [Tooltip("跟随指针的收敛速度，越大越紧跟。")]
    [SerializeField, Min(1f)] private float followLerp = 26f;
    [Tooltip("拿起时的额外放大（纯视觉，命中区不变）。")]
    [SerializeField, Range(1f, 1.3f)] private float liftScale = 1.06f;
    [Tooltip("拖到机器可收范围上时的提示缩小（相对桌面基准）。判定与 TryFeedTicket 同源，缩了就一定收得下。")]
    [SerializeField, Range(0.4f, 1f)] private float dropHintScale = 0.75f;
    [Tooltip("提示缩放的跟随速度，越大过渡越快。")]
    [SerializeField, Min(1f)] private float hintLerp = 14f;
    [Tooltip("拖拽期间提升的渲染层数，保证票盖在机器上面。")]
    [SerializeField, Min(0)] private int dragSortingBoost = 12;
    [SerializeField, Min(0.05f)] private float returnTime = 0.26f;

    [Header("References")]
    [SerializeField] private Camera inputCamera;
    [SerializeField] private HoverJelly jelly;

    public bool InteractionEnabled { get; set; } = true;
    public bool IsGrabbed => grabbed;

    private SpriteRenderer body;
    private ScratchCard cover;
    private SpriteRenderer coverRenderer;
    private LotteryTicket ticket;
    private LotteryGame game;
    private TicketFlyIn flyIn;
    private Vector3 home;
    private Vector3 baseScale = Vector3.one;
    private Vector3 pressStart;
    private float holdTimer;
    private float returnTimer;
    private bool holding;
    private bool grabbed;
    private bool returning;
    private Vector3 returnFrom;
    private Vector3 returnFromScale;
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
        home = transform.position;
        RegisterBody();
    }

    // 登记成「票」。机器在按下那一帧必须给票让路 —— 机器按下即拖，而票要按住 0.12s
    // 才拿得起来，不让路的话票一旦被压在机器上就永远抓不回来（软锁）。
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
        // 有入场动画时以它的桌面落点为准（Awake 里物体已经被搬到屏幕外了）。
        home = flyIn != null ? flyIn.Home : transform.position;
        InteractionEnabled = !(flyIn != null && flyIn.IsFlying);
    }

    private void Update()
    {
        if (game == null || inputCamera == null) return;
        if (!ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)) return;
        Vector3 world = ScreenToWorld(screen);

        if (returning)
        {
            StepReturn();
            return;
        }

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
            // 命中基准是静止尺寸的包围盒，入场动画的缩放不会撑大命中区。
            if (!HoverJelly.ContainsPointUnscaled(body.transform, body.sprite.bounds, world, baseScale)) return;
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
        if (holdTimer >= holdTime) Grab();
    }

    private void Grab()
    {
        grabbed = true;
        holding = false;
        if (cover != null) cover.InputEnabled = false;
        // 顺序要紧：先挂起果冻**再**写 scale。`jelly.enabled = false` 会同步触发 OnDisable →
        // ResetToRest 写一次 localScale（复位成基准值），写在抬起之后就会把 1.06 抹掉。
        // 抬起期间的 localScale 归本组件独占：1.06 是个**持续**状态，弹簧脉冲表达不了它，
        // 果冻一写就会把它覆盖掉（症状是「抬起又马上缩回去」）。
        // 那一记「拿起来弹一下」挪到松手时（Drop）—— 语义上正好是放下时落一下。
        if (jelly != null) jelly.enabled = false;
        if (body != null) body.sortingOrder = bodyOrder + dragSortingBoost;
        if (coverRenderer != null) coverRenderer.sortingOrder = coverOrder + dragSortingBoost;
        transform.localScale = new Vector3(baseScale.x * liftScale, baseScale.y * liftScale, baseScale.z);
    }

    private void Follow(Vector3 world)
    {
        Vector3 target = new Vector3(world.x, world.y, home.z);
        float k = 1f - Mathf.Exp(-followLerp * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, target, k);

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
            // 已被机器接收：机器会接管这个 GameObject（AutoScratcher.TryAccept 会把果冻关掉），
            // 本组件到此为止，不要再把果冻开回来。
            enabled = false;
            return;
        }
        // 交还给果冻：先写完 baseScale 再开，同帧两个源写 localScale 一定会互相覆盖。
        if (jelly != null)
        {
            jelly.enabled = true;
            jelly.Pulse(1f, 0.16f);
        }
        BeginReturn();
    }

    private void BeginReturn()
    {
        returning = true;
        returnTimer = 0f;
        returnFrom = transform.position;
        returnFromScale = transform.localScale;   // 可能还带着 0.75 的投放提示缩放，飞回途中平滑长回去
    }

    private void StepReturn()
    {
        returnTimer += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(returnTimer / returnTime);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        transform.position = Vector3.Lerp(returnFrom, home, ease);
        transform.localScale = Vector3.Lerp(returnFromScale, baseScale, ease);
        if (k >= 1f)
        {
            returning = false;
            transform.position = home;
            transform.localScale = baseScale;
        }
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
        holding = grabbed = returning = false;
        RestoreOrders();
        transform.localScale = baseScale;
        if (cover != null) cover.InputEnabled = true;
        DragBodyRegistry.Unregister(this);
    }
}
