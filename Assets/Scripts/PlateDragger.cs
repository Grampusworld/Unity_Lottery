using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 脏盘子的拖拽手势。与 TicketDragger 同一套分流：**按住不动 0.12 秒**拿起，
// 拿起后抬起 ×1.06、果冻挂起（localScale 归本组件独占）、跟随指针。
//
// 与票的两处刻意不同：
//   ① 盘子没有投放目标（不进任何机器），所以松手**留在原地** —— 夹取进桌面内表面、
//      再按实体碰撞推回（机器会绕开盘子），然后落定。不飞回原位。
//   ② 碰撞登记在 DirtyPlate 名下（盘子是它自己在 OnEnable 注册成 Plate 实体的），
//      Resolve 必须用同一个 owner 去找自己，否则查不到自身条目、碰撞直接失效。
//
// 飞入 + 落地回弹期间（PlateFlyIn.IsFlying）禁止拿起：那段时间 localScale 归
// PlateFlyIn 独占，且果冻还没交还，抢进来一定是同帧双写。
[RequireComponent(typeof(SpriteRenderer))]
public class PlateDragger : MonoBehaviour
{
    [Header("Hold to grab")]
    [Tooltip("按住多久才算「拿起」。与票的 TicketDragger.holdTime 保持一致。")]
    [SerializeField, Min(0.05f)] private float holdTime = 0.12f;
    [Tooltip("按住期间允许的最大位移（世界单位），超过就判定成误按，放弃拿起。")]
    [SerializeField, Min(0.01f)] private float moveTolerance = 0.45f;

    [Header("Drag")]
    [Tooltip("跟随指针的收敛速度，越大越紧跟。")]
    [SerializeField, Min(1f)] private float followLerp = 26f;
    [Tooltip("拿起时的额外放大（纯视觉）。")]
    [SerializeField, Range(1f, 1.3f)] private float liftScale = 1.06f;
    [Tooltip("拖拽期间提升的渲染层数，保证盘子盖在机器和海绵上面。")]
    [SerializeField, Min(0)] private int dragSortingBoost = 12;

    [Header("References")]
    [SerializeField] private Camera inputCamera;
    [SerializeField] private HoverJelly jelly;
    [SerializeField] private PlateFlyIn flyIn;

    public bool IsGrabbed => grabbed;

    private SpriteRenderer body;
    private DirtyPlate plate;
    private SpongeDrag sponge;      // 桌面几何的唯一来源（TableSurface/TabletopPixels）
    private Vector3 baseScale = Vector3.one;
    private Vector3 pressStart;
    private Vector3 grabOffset;
    private float holdTimer;
    private bool holding;
    private bool grabbed;
    private int bodyOrder;

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        plate = GetComponent<DirtyPlate>();
        flyIn = GetComponent<PlateFlyIn>();
        baseScale = transform.localScale;
        if (inputCamera == null) inputCamera = Camera.main;
        if (body != null) bodyOrder = body.sortingOrder;
    }

    // 由 LotteryGame 在 Instantiate 之后调用。桌面参数从场景里的海绵实例取
    // （盘子是运行时生成的，Prefab 连不了场景引用）。
    public void Initialize(LotteryGame owner)
    {
        if (sponge == null) sponge = UnityEngine.Object.FindAnyObjectByType<SpongeDrag>();
    }

    private void Update()
    {
        if (plate == null || inputCamera == null) return;

        // 飞行 / 落地回弹期间 scale 归 PlateFlyIn：连「按住计 时」都不开始，避免跨状态接管。
        if (flyIn != null && flyIn.IsFlying)
        {
            holding = false;
            holdTimer = 0f;
            return;
        }

        if (!ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)) return;
        Vector3 world = ScreenToWorld(screen);

        if (!pressed)
        {
            if (grabbed) Drop();
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
            if (!justPressed) return;
            if (body == null || body.sprite == null) return;
            // 票与盘子重叠时票优先：票同样按住才能拿，两边同时抢一个按下会一起抬起。
            if (DragBodyRegistry.Hit(world, DragBodyKind.Ticket)) return;
            // 命中基准是静止尺寸的包围盒，果冻缩放不会撑大命中区。
            if (!HoverJelly.ContainsPointUnscaled(body.transform, body.sprite.bounds, world, baseScale)) return;
            holding = true;
            holdTimer = 0f;
            pressStart = world;
            return;
        }

        holdTimer += Time.deltaTime;
        if (Vector3.Distance(world, pressStart) > moveTolerance)
        {
            // 盘子上没有「划一下」的备选手势，超位移就是误按，直接放弃。
            holding = false;
            return;
        }
        if (holdTimer >= holdTime) Grab(world);
    }

    private void Grab(Vector3 world)
    {
        grabbed = true;
        holding = false;
        // 顺序要紧：先挂起果冻**再**写 scale（jelly.enabled=false 会同步触发
        // OnDisable → ResetToRest 写一次复位，写在抬起之后会把 1.06 抹掉）。
        if (jelly != null) jelly.enabled = false;
        if (body != null) body.sortingOrder = bodyOrder + dragSortingBoost;
        // ClampDrag 的入参是 transform.position，偏移也必须按 pivot 记（机器踩过的坑）。
        grabOffset = transform.position - world;
        transform.localScale = new Vector3(baseScale.x * liftScale, baseScale.y * liftScale, baseScale.z);
    }

    private void Follow(Vector3 world)
    {
        Vector3 target = ClampDrag(world + grabOffset);
        float k = 1f - Mathf.Exp(-followLerp * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, target, k);
    }

    private void Drop()
    {
        grabbed = false;
        if (body != null) body.sortingOrder = bodyOrder;
        // 落定前再夹一次：抬起放大多出来的 6% 也要算进桌面与碰撞余量。
        transform.position = ClampDrag(transform.position);
        transform.localScale = baseScale;
        // 交还给果冻：先写完 baseScale 再开，同帧两个源写 localScale 一定会互相覆盖。
        if (jelly != null && (flyIn == null || !flyIn.IsFlying))
        {
            jelly.enabled = true;
            jelly.Pulse(1f, 0.16f);
        }
    }

    // 与 SpongeDrag / MachineDrag 同一套：先夹桌面，再按实体碰撞推回。
    // Resolve 的 self 必须传 DirtyPlate —— 登记表里盘子的条目在它名下。
    private Vector3 ClampDrag(Vector3 desired)
    {
        Vector3 bordered = ClampToTable(desired);
        return DragBodyRegistry.Resolve(plate, transform.position, bordered);
    }

    private Vector3 ClampToTable(Vector3 world)
    {
        if (body == null || body.sprite == null || inputCamera == null) return world;
        SpriteRenderer table = sponge != null ? sponge.TableSurface : null;
        Rect pixels = sponge != null ? sponge.TabletopPixels : default(Rect);
        Bounds area = DragBounds.Playable(table, pixels, inputCamera, world.z);

        // 抬起放大 1.06 + 果冻峰值，留 0.75 系数的余量（与海绵同一档）。
        Vector3 size = body.sprite.bounds.size;
        float halfWidth = Mathf.Abs(size.x * baseScale.x) * 0.75f;
        float halfHeight = Mathf.Abs(size.y * baseScale.y) * 0.75f;
        float minX = area.min.x + halfWidth;
        float maxX = area.max.x - halfWidth;
        float minY = area.min.y + halfHeight;
        float maxY = area.max.y - halfHeight;
        world.x = minX <= maxX ? Mathf.Clamp(world.x, minX, maxX) : (minX + maxX) * 0.5f;
        world.y = minY <= maxY ? Mathf.Clamp(world.y, minY, maxY) : (minY + maxY) * 0.5f;
        return world;
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
        if (body != null) body.sortingOrder = bodyOrder;
        transform.localScale = baseScale;
    }
}
