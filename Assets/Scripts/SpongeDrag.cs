using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Drag the sponge around the visible tabletop and leave it where it was released.
//
// 2026-09-29：接进「惯性 + 实体碰撞」。海绵现在也是可拖实体的一员 ——
// 机器撞不过它（不能碾到海绵身上），但它仍然**必须**能压在盘子上擦涂层，
// 所以「海绵 ↔ 盘子」在 DragBodyRegistry 里被强制排除。
//
// 抓取优先级：海绵是全场最高（按下即拖 + order 5）。压在盘子/票/机器上的那一按
// 同样归海绵 —— 不用让它，因为别人都在它下面（见 DragBodyRegistry.Claimant）。
[RequireComponent(typeof(SpriteRenderer))]
public class SpongeDrag : MonoBehaviour
{
    [SerializeField] private Sprite yellowSprite;
    [SerializeField] private Sprite purpleSprite;
    [SerializeField] private Camera inputCamera;
    [SerializeField] private SpriteRenderer tableSurface;
    [SerializeField] private RectTransform shopPanel;
    [Tooltip("Table.png 黑色内框之间的橙色桌面范围，单位为贴图像素（左下角为原点）。")]
    [SerializeField] private Rect tabletopPixels = new Rect(63f, 133f, 89f, 47f);
    [SerializeField, Min(1)] private int yellowBrushRadius = 4;
    [Tooltip("每笔擦掉的污渍透明度（0-255）。30 → 同一处约 9 笔见底（255/30 = 8.5）。\n" +
             "历史值 85 只要 3 笔，扫一遍就整片归零，等于没有「一层层变淡」这个过程。\n" +
             "高级海绵是碰到即整盘淡出，用不到这个值。")]
    [SerializeField, Range(1, 255)] private int scrubAlphaStep = 30;
    [Tooltip("留空自动在本物体上找；找不到就没有惯性（松手即停）。")]
    [SerializeField] private DragInertia inertia;

    // 桌面几何与机器共享的唯一一份来源。MachineDrag 找不到自己的参数时会来这里取
    // —— 手抄第二份，改桌面美术那天就会对不上。
    public SpriteRenderer TableSurface => tableSurface;
    public Rect TabletopPixels => tabletopPixels;
    public RectTransform ShopPanel => shopPanel;

    private LotteryGame game;
    private SpriteRenderer spriteRenderer;
    private HoverJelly hoverJelly;
    private Vector3 previousPosition;
    private Vector3 grabOffset;
    private Vector3 baseScale;
    private Vector3 designPosition;
    private bool dragging;
    private bool advanced;
    private System.Func<Vector3, Vector3> tableClamp;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
        designPosition = transform.position;
        if (inputCamera == null) inputCamera = Camera.main;
        hoverJelly = GetComponent<HoverJelly>();
        tableClamp = ClampToTable;
        if (inertia == null) inertia = GetComponent<DragInertia>();
        if (inertia != null) inertia.Clamp = ClampDrag;
        RefreshBody();
    }

    // 登记/刷新碰撞占位。黄紫两块海绵尺寸不同（SetAdvanced 会换贴图），所以换贴图后要重来一次。
    private void RefreshBody()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null) return;
        Vector3 size = Vector3.Scale(spriteRenderer.sprite.bounds.size, baseScale);
        Vector3 center = Vector3.Scale(spriteRenderer.sprite.bounds.center, baseScale);
        DragBodyRegistry.Register(this, DragBodyKind.Sponge, transform, center,
            new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f);
    }

    public void Initialize(LotteryGame owner, bool usePurple)
    {
        game = owner;
        SetAdvanced(usePurple);
    }

    public void SetAdvanced(bool usePurple)
    {
        advanced = usePurple;
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = usePurple ? purpleSprite : yellowSprite;
        RefreshBody();
        transform.position = ClampDrag(transform.position);
    }

    // 拖完回设计位（场景里手摆的位置）。启动与 DebugResetProgress 调。
    public void ResetToDesignPosition()
    {
        if (dragging)
        {
            dragging = false;
            if (hoverJelly != null) hoverJelly.SetPressed(false);
        }
        if (inertia != null) inertia.Stop();
        transform.position = ClampDrag(designPosition);
    }

    private void Update()
    {
        if (MainMenuScreen.GameplayBlocked)
        {
            if (dragging)
            {
                dragging = false;
                if (hoverJelly != null) hoverJelly.SetPressed(false);
                if (inertia != null) inertia.Stop();
            }
            return;
        }
        // 自检：登记表是 static，Play 中一次脚本重编译（域重载）就会把它清空。
        // 丢表的海绵**不会**报错，只是按下没反应 —— Claimant 再也找不到 Sponge 这一类，
        // 而海绵的抓取现在必须先过 Claimant（见下）。缺了就当场补登记，自愈成本 O(n)。
        // OnDisable 仍负责注销，这条负责回填，两者配平；MachineDrag 的 RefreshBody 是同一套。
        if (!DragBodyRegistry.IsRegistered(this)) RefreshBody();

        if (game == null || inputCamera == null || !ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)) return;

        // 滑行期间松手了也不能碰 position：position 归 DragInertia 独占。
        // 同帧两个源写同一个属性一定会互相覆盖，症状是滑到一半被拽回原地。
        if (dragging == false && inertia != null && inertia.Sliding) return;

        if (!pressed)
        {
            if (dragging)
            {
                dragging = false;
                if (hoverJelly != null) hoverJelly.SetPressed(false);
                if (inertia != null) inertia.Release();   // 松手 → 交给惯性滑行
                LotterySfx.Play(LotterySfx.Sound.Drop);
            }
            if (inertia != null && inertia.Sliding) return;
            transform.position = ClampDrag(transform.position);
            return;
        }

        Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y,
            Mathf.Abs(inputCamera.transform.position.z - transform.position.z)));
        world.z = transform.position.z;
        if (!dragging)
        {
            if (spriteRenderer.sprite == null) return;
            // 仲裁：这次按下得归海绵才算数。海绵是最高优先级，所以只要命中就是它赢 ——
            // 这句话的真正作用是「压在盘子/票/机器上的那一按同样归海绵」，而不是让海绵让路。
            if (DragBodyRegistry.Claimant(world) != DragBodyKind.Sponge) return;
            // 判定基准是静止尺寸的包围盒，hover 果冻缩放时命中区域保持不变。
            if (!justPressed || !HoverJelly.ContainsPointUnscaled(spriteRenderer.transform,
                    spriteRenderer.sprite.bounds, world, baseScale)) return;
            if (inertia != null) inertia.BeginDrag();   // 新按下取消正在进行的滑行
            dragging = true;
            LotterySfx.Play(LotterySfx.Sound.DragStart);
            grabOffset = transform.position - world;
            previousPosition = transform.position;
            if (hoverJelly != null) hoverJelly.SetPressed(true);
        }

        world = ClampDrag(world + grabOffset);
        transform.position = world;
        if (inertia != null) inertia.TrackDrag();
        ScrubPlates(previousPosition, world);
        previousPosition = world;
    }

    // 擦**所有**活着的盘子。桌面可以同时有多张（MULTIPLE PLATES 解锁后），
    // 海绵压在哪张上就擦哪张 —— ScrubAt 自带越界早退，擦不到的盘子成本可以忽略。
    // 插值步数按这一帧的位移算一次，所有盘子共用，避免每张盘都重算一遍。
    // Plates 是 LotteryGame 内部那个 List 本身，不是每帧新数组 —— 这里不产生分配。
    private void ScrubPlates(Vector3 from, Vector3 to)
    {
        var plates = game.Plates;
        if (plates == null || plates.Count == 0) return;
        int brush = yellowBrushRadius * (advanced ? 2 : 1);
        int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / 0.5f), 1, 50);
        for (int p = 0; p < plates.Count; p++)
        {
            DirtyPlate plate = plates[p];
            if (plate == null) continue;
            for (int i = 0; i <= steps; i++)
                plate.ScrubAt(Vector3.Lerp(from, to, (float)i / steps), brush, scrubAlphaStep, advanced);
        }
    }

    // 拖动与滑行共用同一个夹取：先夹进桌面范围，再按实体碰撞推回。
    // DragInertia.Clamp 也指向它 —— 两条路必须是同一套数学，否则滑行会滑进拖不进去的地方。
    private Vector3 ClampDrag(Vector3 desired)
    {
        Vector3 bordered = ClampToTable(desired);
        return DragBodyRegistry.Resolve(this, ClampToTable(transform.position), bordered, tableClamp);
    }

    private Vector3 ClampToTable(Vector3 world)
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null || inputCamera == null) return world;

        Bounds area = DragBounds.Playable(tableSurface, tabletopPixels, inputCamera, world.z, shopPanel);
        Bounds relative = spriteRenderer.sprite.bounds;
        relative.center = Vector3.Scale(relative.center, baseScale) * HoverJelly.MaxScale;
        relative.size = Vector3.Scale(relative.size, baseScale) * HoverJelly.MaxScale;
        return DragBounds.ClampVisual(world, relative, area);
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
        if (dragging)
        {
            if (hoverJelly != null) hoverJelly.SetPressed(false);
        }
        dragging = false;
        DragBodyRegistry.Unregister(this);
    }
}
