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
    [SerializeField] private WasherProgressRing ring;
    [Tooltip("进度环的根物体。与洗盘机**同级**（不是子物体），位置里没有任何脚本在改它 ——" +
             "拖动时必须自己跟。留空会自动取 ring 所在的物体。")]
    [SerializeField] private Transform ringRoot;
    [SerializeField] private WasherWaterEffect water;

    private SpriteRenderer display;
    private LotteryGame game;
    private Vector3 ringOffset;
    private Vector3 ringAnchor;
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
        SetWater(false);
        if (ringRoot == null && ring != null) ringRoot = ring.transform;
        CaptureRingOffset();
    }

    // 进度环与机身是**同级**物体（做子物体会被机身 26 倍缩放带跑、还会被果冻抖），
    // 编辑器里手摆出来的那个位置差就是它的挂载偏移。记下来，机身之后动到哪儿它跟到哪儿。
    private void CaptureRingOffset()
    {
        if (ringRoot == null) return;
        ringOffset = ringRoot.position - transform.position;
        ringAnchor = transform.position;
    }

    // 机身位置被拖动/滑行改动时每帧调用。机内盘子与进度环都是绝对坐标，不重排就留在原地。
    public void FollowMachine()
    {
        if (feeder != null) feeder.RefreshSlotPositions();
        SyncRing();
    }

    // 只在机身真的动了才写 —— 每帧无条件写 transform 会把 Canvas 标脏。
    private void SyncRing()
    {
        if (ringRoot == null) return;
        if (transform.position == ringAnchor) return;
        ringAnchor = transform.position;
        ringRoot.position = transform.position + ringOffset;
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
        if (ring != null) ring.Hide();
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
        SyncRing();
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
