using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 把票拖进自动刮彩票机的手势。与「左键划过刮奖」共用一个鼠标键，靠**按住不动 0.22 秒**分流：
//
//   按住 → 指针几乎不动满 0.22s  →  拿起（票抬起、放大、可以拖）
//   按住 → 一旦移动超过容差     →  判定为刮奖，本组件立刻放手，ScratchCard 照常工作
//
// 这样刮奖手感一点没变（划一下就出结果），只有「刻意按住」才会进拖拽。
// 松手时指针若在机器上就投料，否则飞回桌面落点。
[RequireComponent(typeof(SpriteRenderer))]
public class TicketDragger : MonoBehaviour
{
    [Header("Hold to grab")]
    [Tooltip("按住多久才算「拿起」。")]
    [SerializeField, Min(0.05f)] private float holdTime = 0.22f;
    [Tooltip("按住期间允许的最大位移（世界单位），超过就判定成刮奖。")]
    [SerializeField, Min(0.01f)] private float moveTolerance = 0.45f;

    [Header("Drag")]
    [Tooltip("跟随指针的收敛速度，越大越紧跟。")]
    [SerializeField, Min(1f)] private float followLerp = 26f;
    [Tooltip("拿起时的额外放大（纯视觉，命中区不变）。")]
    [SerializeField, Range(1f, 1.3f)] private float liftScale = 1.06f;
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
    }

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
            // 机器没解锁时票不需要被搬走，干脆不抢占按下事件。
            if (!game.ScratcherUnlocked) return;
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
        if (body != null) body.sortingOrder = bodyOrder + dragSortingBoost;
        if (coverRenderer != null) coverRenderer.sortingOrder = coverOrder + dragSortingBoost;
        transform.localScale = new Vector3(baseScale.x * liftScale, baseScale.y * liftScale, baseScale.z);
        if (jelly != null) jelly.Pulse(1f, 0.16f);
    }

    private void Follow(Vector3 world)
    {
        Vector3 target = new Vector3(world.x, world.y, home.z);
        float k = 1f - Mathf.Exp(-followLerp * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, target, k);
    }

    private void Drop(Vector3 world)
    {
        grabbed = false;
        RestoreOrders();
        transform.localScale = baseScale;
        if (cover != null) cover.InputEnabled = true;

        if (game.TryFeedTicket(ticket, world))
        {
            // 已被机器接收：机器会接管这个 GameObject，本组件到此为止。
            enabled = false;
            return;
        }
        BeginReturn();
    }

    private void BeginReturn()
    {
        returning = true;
        returnTimer = 0f;
        returnFrom = transform.position;
    }

    private void StepReturn()
    {
        returnTimer += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(returnTimer / returnTime);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        transform.position = Vector3.Lerp(returnFrom, home, ease);
        if (k >= 1f)
        {
            returning = false;
            transform.position = home;
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
    }
}
