using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 脏盘子的拖拽手势。与 TicketDragger 同一套骨架：拿起后抬起 ×1.06、果冻挂起
// （localScale 归本组件独占）、跟随指针。
//
// 2026-09-29：`holdTime` 从 0.12 改成 **0（按下即抓）**。票之所以必须保留等待，
// 是因为它和「左键划过刮奖」共用一个键、要靠时间分流；盘子没有第二个手势要分流，
// 那个 0.12s 纯粹是白等，玩家能明显感到「拖不动」。
// 保留 `holdTime` 字段与「按住不动」那条路，是为了以后真需要分流时不必改结构。
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
    [Tooltip("按住多久才算「拿起」。**<= 0 表示按下即抓**（不走「按住不动」那条路）。\n" +
             "2026-09-29 定为 0：盘子没有别的鼠标手势要分流（刮奖是票的事），\n" +
             "而 0.12s 的等待玩家能明显感到「拖不动」。票仍保留等待 —— 见 TicketDragger。")]
    [SerializeField, Min(0f)] private float holdTime = 0f;
    [Tooltip("按住期间允许的最大位移（世界单位），超过就判定成误按，放弃拿起。\n" +
             "只在 holdTime > 0 时参与判定：按下即抓的话这一帧就已经拿起来了，没有「按住期间」。")]
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

    // 拖拽时要把**整组**渲染层一起抬。只抬根是不够的：污渍是子物体
    // （Dirt - separate layer，order 3），根一抬到 14，不透明的盘子就把它整个盖住 ——
    // 症状是「拖起来污渍消失、松手又出现」。票没这个毛病，因为 TicketDragger 抬的是成组的。
    private Renderer[] sortingRenderers;
    private int[] sortingOrders;

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        plate = GetComponent<DirtyPlate>();
        flyIn = GetComponent<PlateFlyIn>();
        baseScale = transform.localScale;
        if (inputCamera == null) inputCamera = Camera.main;
        CacheSortingOrders();
    }

    // 记下根 + 全部子渲染器的原始层数，之后靠同一个偏移整体平移，相对层次不变。
    private void CacheSortingOrders()
    {
        sortingRenderers = GetComponentsInChildren<Renderer>(true);
        sortingOrders = new int[sortingRenderers.Length];
        for (int i = 0; i < sortingRenderers.Length; i++)
            sortingOrders[i] = sortingRenderers[i] != null ? sortingRenderers[i].sortingOrder : 0;
    }

    private void ApplySortingBoost(bool boosted)
    {
        if (sortingRenderers == null) return;
        int delta = boosted ? dragSortingBoost : 0;
        for (int i = 0; i < sortingRenderers.Length; i++)
        {
            if (sortingRenderers[i] == null) continue;
            sortingRenderers[i].sortingOrder = sortingOrders[i] + delta;
        }
    }

    // 由 LotteryGame 在 Instantiate 之后调用。桌面参数从场景里的海绵实例取
    // （盘子是运行时生成的，Prefab 连不了场景引用）。
    public void Initialize(LotteryGame owner)
    {
        if (sponge == null) sponge = UnityEngine.Object.FindAnyObjectByType<SpongeDrag>();
    }

    private void Update()
    {
        if (MainMenuScreen.GameplayBlocked)
        {
            holding = false;
            holdTimer = 0f;
            if (grabbed) Drop();
            return;
        }
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
            if (grabbed)
            {
                Drop();
                LotterySfx.Play(LotterySfx.Sound.Drop);
            }
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
            // 仲裁：盘子是最低优先级。海绵（按下即抓）、票、机器任何一方压在上面时，
            // 这次按下都不归盘子 —— 否则盘子和它一起被拖（2026-09-29 报的 bug：
            // 海绵与盘子重叠时，两个一起跟着手跑）。
            if (DragBodyRegistry.Claimant(world) != DragBodyKind.Plate) return;
            if (!DragBodyRegistry.OwnsPlatePress(plate, world)) return;
            // 命中基准是静止尺寸的包围盒，果冻缩放不会撑大命中区。
            if (!HoverJelly.ContainsPointUnscaled(body.transform, body.sprite.bounds, world, baseScale)) return;
            // holdTime <= 0 = 按下即抓，**必须**在这里直接拿起来、不进下面那条路：
            // 否则会先 holding、下一帧才判位移，而 moveTolerance 只有 0.45 世界单位
            // （≈6.5 屏幕像素），下一帧只要动过一点就被判成误按、静默放弃 ——
            // 拖拽会变成「有时拖得动有时拖不动」，比不响应更难排查。
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
            // 盘子上没有「划一下」的备选手势，超位移就是误按，直接放弃。
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
        // 顺序要紧：先挂起果冻**再**写 scale（jelly.enabled=false 会同步触发
        // OnDisable → ResetToRest 写一次复位，写在抬起之后会把 1.06 抹掉）。
        if (jelly != null) jelly.enabled = false;
        ApplySortingBoost(true);
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
        ApplySortingBoost(false);
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
        ApplySortingBoost(false);
        transform.localScale = baseScale;
    }
}
