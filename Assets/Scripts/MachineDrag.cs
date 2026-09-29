using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 洗盘机 / 自动刮彩票机的拖动。和 SpongeDrag 共用「夹取 + 惯性 + 实体碰撞」三件事，
// 但不能直接复用那个组件 —— 三条硬约束对不上：
//
//   ① 刮票机的 pivot 在**可见内容底边中点**（三段素材都是），不是几何中心。
//      照抄海绵的 `sprite.bounds` + `transform.position` 会变成上边欠夹、下边过夹。
//      而且它的 footprint 随等级变宽（44×52 / 46×59 / 52×61 texel），必须现算。
//   ② 刮票机的 Update 在入场下落的 0.55s 里写 `transform.position`。
//      那段窗口禁止拖动：两个源同帧抢写同一个属性会把机身永久搁在半空。
//   ③ 机身内部全是「按 transform.position 现算的绝对坐标」：槽位里的迷你票、
//      机内盘子、进度环（都是同级物体或绝对坐标），拖动时必须每帧重排。
//
// 按下即拖（不像海绵/票需要「按住 0.22s」），所以按下那一帧必须先把这次点击让出去，
// 让路对象与理由写在 DragBodyRegistry 顶部的注释里（海绵 + 桌面上的票）。
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public class MachineDrag : MonoBehaviour
{
    [Header("Body")]
    [Tooltip("刮票机（留空自动在本物体上找）：footprint 按它的 contentRect 现算。")]
    [SerializeField] private AutoScratcher scratcher;
    [Tooltip("洗盘机（留空自动在本物体上找）：拖动时机内盘子与进度环要跟着重排。")]
    [SerializeField] private AutomaticDishWasher washer;

    [Header("Bounds")]
    [Tooltip("可拖范围按机身半宽内缩的余量系数。0.75 与海绵一致（给 HoverJelly 1.5 倍峰值留空间）。")]
    [SerializeField, Range(0.5f, 1f)] private float edgeMargin = 0.75f;
    [Tooltip("桌面贴图（Table）。留空自动从海绵实例读，保证两台机器与海绵用的是同一份参数。")]
    [SerializeField] private SpriteRenderer tableSurface;
    [Tooltip("Table.png 中有颜色的桌面范围，单位为贴图像素（左下角为原点）。")]
    [SerializeField] private Rect tabletopPixels = new Rect(58f, 128f, 99f, 57f);

    [Header("References")]
    [SerializeField] private Camera inputCamera;
    [Tooltip("留空自动在本物体上找；找不到就没有惯性（松手即停）。")]
    [SerializeField] private DragInertia inertia;
    [SerializeField] private HoverJelly jelly;

    private SpriteRenderer body;
    private Vector3 baseScale = Vector3.one;
    private Vector2 half = new Vector2(1f, 1f);
    private Vector3 centerOffset;
    private Vector2 registeredHalf;
    private Vector3 registeredOffset;
    private bool registered;

    private Vector3 designPosition;
    private Vector3 grabOffset;      // 鼠标世界点 → 机身 transform.position（pivot），**不是**可见内容中心
    private bool dragging;

    public bool IsDragging => dragging;

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
        designPosition = transform.position;
        if (inputCamera == null) inputCamera = Camera.main;
        if (scratcher == null) scratcher = GetComponent<AutoScratcher>();
        if (washer == null) washer = GetComponent<AutomaticDishWasher>();
        if (inertia == null) inertia = GetComponent<DragInertia>();
        if (jelly == null) jelly = GetComponent<HoverJelly>();
        if (tableSurface == null) AdoptTableFromSponge();
        if (inertia != null) inertia.Clamp = ClampDrag;
        RefreshBody(true);
    }

    // 桌面几何只认海绵实例那一份。这里自动沿用，避免手抄第二份参数以后对不上。
    private void AdoptTableFromSponge()
    {
        SpongeDrag sponge = UnityEngine.Object.FindAnyObjectByType<SpongeDrag>();
        if (sponge == null) return;
        tableSurface = sponge.TableSurface;
        tabletopPixels = sponge.TabletopPixels;
    }

    private void OnDisable()
    {
        if (dragging)
        {
            dragging = false;
            if (jelly != null) jelly.SetPressed(false);
        }
        DragBodyRegistry.Unregister(this);
        registered = false;
    }

    private void Update()
    {
        RefreshBody(false);

        if (inertia != null && inertia.Sliding) return;   // 滑行期间 position 归 DragInertia 独占

        // 入场下落：不拖、不夹、不推。这段窗口里 AutoScratcher.Update 在写 position，
        // 这边再写一次就是两个源抢同一个属性。
        if (scratcher != null && scratcher.Entering)
        {
            if (dragging)
            {
                dragging = false;
                if (jelly != null) jelly.SetPressed(false);
                if (inertia != null) inertia.Stop();
            }
            return;
        }

        if (inputCamera == null) return;
        if (!ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)) return;
        Vector3 world = ScreenToWorld(screen);

        if (!pressed)
        {
            if (dragging) EndDrag();
            else SettleIdle();
            return;
        }

        if (dragging)
        {
            transform.position = ClampDrag(world + grabOffset);
            FollowInternals();
            if (inertia != null) inertia.TrackDrag();
            return;
        }

        if (!justPressed) return;
        // 让路：海绵画在机器上层（order 5 > 4）。
        if (DragBodyRegistry.Hit(world, DragBodyKind.Sponge)) return;
        // 让路：票要按住 0.12s 才拿得起来，机器在按下这一帧就抢走了 ——
        // 票一旦被压在机器上就永远抓不回来。层次上票在机器下面，这里是刻意例外。
        if (DragBodyRegistry.Hit(world, DragBodyKind.Ticket)) return;
        if (!ContainsPoint(world)) return;

        dragging = true;
        // 抓取偏移必须和 ClampDrag 的入参语义对齐 —— 它的入参是「机身 transform.position」，
        // centerOffset 由它自己补。所以这里只能按 pivot 记。
        // 用可见内容中心记（`transform.position + centerOffset - world`）会多补一次 centerOffset，
        // 按下后第一帧整机向上跳 centerOffset：刮票机 9.9~11.6 世界单位（142~167px，随等级变）。
        // 洗盘机的 pivot 在贴图中心、centerOffset 恒为 0，同一个 bug 在它身上乘数是零，所以看不出来。
        grabOffset = transform.position - world;
        if (inertia != null) inertia.BeginDrag();
        if (jelly != null) jelly.SetPressed(true);
    }

    // 拖完回设计位（场景里手摆的位置）。启动与 DebugResetProgress 调。
    public void ResetToDesignPosition()
    {
        if (dragging)
        {
            dragging = false;
            if (jelly != null) jelly.SetPressed(false);
        }
        if (inertia != null) inertia.Stop();
        transform.position = designPosition;
        FollowInternals();
    }

    private void EndDrag()
    {
        dragging = false;
        if (jelly != null) jelly.SetPressed(false);
        if (inertia != null) inertia.Release();      // 滑行从这里接管
    }

    // 静止时把重叠推开：只会在「等级变宽导致 footprint 变大、顶进了洗盘机」这类
    // 无人操作的情况下真的改动位置，正常玩是空转。
    private void SettleIdle()
    {
        Vector3 settled = DragBodyRegistry.Resolve(this, transform.position, transform.position);
        if (settled == transform.position) return;
        transform.position = settled;
        FollowInternals();
    }

    // 机身在动 → 机内所有「按 transform.position 现算」的东西都得重排。
    private void FollowInternals()
    {
        if (scratcher != null) scratcher.FollowMachine();
        if (washer != null) washer.FollowMachine();
    }

    // 刷新「可见内容包围盒」并同步到碰撞表。刮票机的 footprint 随等级变，
    // 所以这里每帧算一次；值没变就不写表，避免每帧无意义地改登记项。
    private void RefreshBody(bool force)
    {
        Vector3 center;
        Vector2 size;
        // 刮票机走 contentRect（pivot 在内容底边、footprint 随等级变宽，sprite.bounds 两头都不对）；
        // 洗盘机走 sprite.bounds（它那张图的不透明区已经贴着子矩形边缘，不需要额外量内容）。
        bool hasRect = scratcher != null
            ? scratcher.TryGetContentRectWorld(out center, out size)
            : TryGetSpriteRect(out center, out size);
        if (!hasRect) return;

        Vector2 nextHalf = size * 0.5f;
        Vector3 nextOffset = center - transform.position;
        if (!force && registered
            && Mathf.Approximately(nextHalf.x, registeredHalf.x)
            && Mathf.Approximately(nextHalf.y, registeredHalf.y)
            && (nextOffset - registeredOffset).sqrMagnitude < 1e-8f)
        {
            return;
        }
        half = nextHalf;
        centerOffset = nextOffset;
        registeredHalf = nextHalf;
        registeredOffset = nextOffset;
        registered = true;
        DragBodyRegistry.Register(this, DragBodyKind.Machine, transform, centerOffset, half);
    }

    // 洗盘机的可见包围盒：sprite 的子矩形 × 静止缩放。
    // 用 baseScale（Awake 抓的）而不是 lossyScale —— HoverJelly 在写 localScale，
    // 用实时缩放会让夹取范围跟着果冻一起抖。
    private bool TryGetSpriteRect(out Vector3 center, out Vector2 size)
    {
        center = transform.position;
        size = Vector2.one;
        if (body == null || body.sprite == null) return false;
        Vector3 scaledCenter = Vector3.Scale(body.sprite.bounds.center, baseScale);
        Vector3 scaled = Vector3.Scale(body.sprite.bounds.size, baseScale);
        center = transform.position + scaledCenter;
        size = new Vector2(Mathf.Abs(scaled.x), Mathf.Abs(scaled.y));
        return true;
    }

    private bool ContainsPoint(Vector3 world)
    {
        Vector3 center = transform.position + centerOffset;
        if (Mathf.Abs(world.x - center.x) > half.x) return false;
        if (Mathf.Abs(world.y - center.y) > half.y) return false;
        return true;
    }

    // 拖动与滑行共用同一个夹取：先夹进可拖范围，再按实体碰撞推回。
    // DragInertia.Clamp 也指向它 —— 两条路必须完全同一套数学，否则滑行会滑进拖不进去的地方。
    //
    // 入参契约：`desired` 是**机身 transform.position（pivot）**，不是可见内容中心；
    // centerOffset 由这里补。抓取偏移（grabOffset）必须按同一套语义记，否则按下会跳一段。
    private Vector3 ClampDrag(Vector3 desired)
    {
        if (inputCamera == null) return desired;
        Bounds area = DragBounds.Playable(tableSurface, tabletopPixels, inputCamera, desired.z);
        Vector3 center = DragBounds.ClampCenter(desired + centerOffset, half, edgeMargin, area);
        Vector3 bordered = center - centerOffset;
        return DragBodyRegistry.Resolve(this, transform.position, bordered);
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
}
