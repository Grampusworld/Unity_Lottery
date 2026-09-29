using UnityEngine;

// Automatic income is separate from the single manual plate on the table.
//
// 状态机（入场期间不累计 elapsed，所以「所有盘子都进完之后才开始计时」）：
//   Locked  → Feeding（盘子依次飞入，每个到位触发一次果冻；进度环与水流都隐藏）
//   Feeding →  Washing（进度环淡入并开始转，水流特效开启）
//   Washing →  结算收钱 → 清空机内 → 回到 Feeding（快放版）
//
// 外观只有一个 sprite：不再区分 idle / working。
// 「正在洗」这件事由**水流特效 + 进度环**表达，而不是换一张机身贴图 ——
// 这样只需要维护一张图，也不会再出现「某一帧的图里烤进一个盘子」这种问题。
//
// 入场的三种触发由 LotteryGame 显式调用：
//   BeginWithFullEntry() 买下洗盘机 → 完整版
//   BeginFromSave()      读档启动   → 不播动画，直接摆好开洗
//   AddPlates(n)        容量升级   → 只补飞新增的 n 个
[RequireComponent(typeof(SpriteRenderer))]
public class AutomaticDishWasher : MonoBehaviour
{
    private enum State
    {
        Locked,
        Feeding,
        Washing
    }

    [SerializeField] private Sprite displaySprite;
    [SerializeField] private WasherPlateFeeder feeder;
    [Tooltip("进度环。尺寸与位置都归它自己算（环在机器**正上方**，直径 = 可见内容宽 × sizeRatio）。")]
    [SerializeField] private WasherProgressRing ring;
    [SerializeField] private WasherWaterEffect water;

    private SpriteRenderer display;
    private LotteryGame game;
    private Vector3 baseLossyScale = Vector3.one;
    private Vector3 lastLayoutPosition;
    private float elapsed;
    private float secondsPerCycle = 10f;
    private int platesPerCycle = 5;
    private bool unlocked;
    private State state = State.Locked;

    private void Awake()
    {
        display = GetComponent<SpriteRenderer>();
        display.sprite = displaySprite;
        display.enabled = false;
        // 静止尺寸只取一次：Configure 会在升级、读档、Debug 重置时随时被调，
        // 那时机身可能正被悬停果冻放大（最多 9%），现读 lossyScale 会把环永久性放大。
        baseLossyScale = transform.lossyScale;
        lastLayoutPosition = transform.position;
        SetWater(false);
        UpdateRingAnchor();
    }

    // 机身只在**真的动了**的时候才重排机内盘子。拖动 / 惯性滑行 / 入场三条路径都会经过这里 ——
    // 以前靠 MachineDrag 显式调 FollowMachine()，而它在滑行分支直接 return，
    // 「甩一把洗盘机，机内那叠盘子留在原地」就是这么来的。
    private void LateUpdate()
    {
        Vector3 now = transform.position;
        if (now == lastLayoutPosition) return;
        lastLayoutPosition = now;
        if (feeder != null) feeder.RefreshSlotPositions();
    }

    // 把「机身可见内容包围盒」推给进度环。洗盘机只有一个 sprite、不透明区贴着子矩形边缘，
    // 所以 sprite.bounds（乘静止缩放）就是可见内容。位置与尺寸由环自己算：
    // 直径 = 可见宽 × sizeRatio，环心 = 内容顶边 + 间隙 + 半径。
    private void UpdateRingAnchor()
    {
        if (ring == null || display == null || display.sprite == null) return;
        Vector3 size = Vector3.Scale(display.sprite.bounds.size, baseLossyScale);
        Vector3 center = transform.position + Vector3.Scale(display.sprite.bounds.center, baseLossyScale);
        ring.SetMachineAnchor(display, center, new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)));
    }

    // 只更新参数与显示：不播动画，也不打断正在跑的周期（速度升级时不能重置进度）。
    public void Configure(LotteryGame owner, bool active, float seconds, int plates)
    {
        game = owner;
        secondsPerCycle = Mathf.Max(1f, seconds);
        platesPerCycle = Mathf.Max(1, plates);
        unlocked = active;
        if (display == null) display = GetComponent<SpriteRenderer>();
        display.sprite = displaySprite;
        display.enabled = active;
        UpdateRingAnchor();
        if (active) return;

        state = State.Locked;
        elapsed = 0f;
        SetWater(false);
        if (feeder != null) feeder.ClearPlates();
        if (ring != null) ring.Hide(true);
    }

    public void BeginWithFullEntry()
    {
        if (!unlocked) return;
        StartFeed(false);
    }

    public void BeginFromSave()
    {
        if (!unlocked) return;
        if (feeder != null) feeder.PlaceInstantly(platesPerCycle);
        StartWashing();
    }

    public void AddPlates(int added)
    {
        if (!unlocked || feeder == null) return;
        feeder.AddPlates(added);
    }

    private void StartFeed(bool fast)
    {
        state = State.Feeding;
        elapsed = 0f;
        SetWater(false);
        // 收钱那一刻直接归零、不淡出：满圈 = 这一轮完成、空 = 新一轮，本身就够直白；
        // 再叠一层「满圈淡出 0.1s」只会被看成掉帧。装盘的 0.8s 里环保持不可见，
        // 开始洗时再从 0 平滑填（fillSteps <= 1 时是逐帧连续，没有 1/24 的台阶）。
        if (ring != null)
        {
            ring.Hide(true);
            ring.SetProgress(0f);
        }
        if (feeder == null)
        {
            StartWashing();
            return;
        }
        feeder.Feed(platesPerCycle, fast, OnFeedComplete);
    }

    private void OnFeedComplete()
    {
        if (!unlocked) return;
        StartWashing();
    }

    private void StartWashing()
    {
        state = State.Washing;
        elapsed = 0f;
        SetWater(true);
        if (ring != null)
        {
            ring.Show();
            ring.SetProgress(0f);
        }
    }

    // 水流只在真的在洗的时候开：盘子飞入阶段保持干燥。
    private void SetWater(bool on)
    {
        if (water != null) water.SetRunning(on);
    }

    private void Update()
    {
        if (!unlocked || game == null || state != State.Washing) return;

        elapsed += Time.deltaTime;
        if (ring != null) ring.SetProgress(elapsed / secondsPerCycle);
        if (elapsed < secondsPerCycle) return;

        elapsed -= secondsPerCycle;
        game.AwardWashedPlates(platesPerCycle);
        // 洗完即清空，下一轮重新装：与「每周期都快放入场」自洽。
        if (feeder != null) feeder.ClearPlates();
        StartFeed(true);
    }
}
