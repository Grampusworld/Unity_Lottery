using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static LotteryEconomy;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Owns money, unlocks and the active ticket/plate in this prototype.
public class LotteryGame : MonoBehaviour
{
    [Header("Tickets")]
    [SerializeField] private LotteryTicket luckyTicketPrefab;
    [SerializeField] private LotteryTicket goldTicketPrefab;
    [SerializeField] private LotteryTicket novaTicketPrefab;
    [SerializeField] private LotteryTicket heartMatchTicketPrefab;
    [SerializeField] private LotteryTicket crossCodeTicketPrefab;
    [SerializeField] private LotteryTicket zigzagRunTicketPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Button luckyTicketButton;
    [SerializeField] private Button goldTicketButton;
    [SerializeField] private Button novaTicketButton;
    [SerializeField] private Button heartMatchTicketButton;
    [SerializeField] private Button crossCodeTicketButton;
    [SerializeField] private Button zigzagRunTicketButton;
    [SerializeField] private Image luckyProgressFill;
    [SerializeField] private Image goldProgressFill;
    [SerializeField] private Image novaProgressFill;
    [SerializeField] private Image heartMatchProgressFill;
    [SerializeField] private Image crossCodeProgressFill;
    [SerializeField] private Image zigzagRunProgressFill;
    [SerializeField, Min(0)] private float disappearDelay = 0.35f;

    [Header("Dishes")]
    [SerializeField] private DirtyPlate platePrefab;
    [SerializeField] private Transform plateSpawnPoint;
    [SerializeField] private SpongeDrag sponge;
    [SerializeField] private AutomaticDishWasher washer;
    [SerializeField] private Button plateButton;
    [SerializeField] private Button multiplePlatesButton;
    [SerializeField] private Button plateValueButton;
    [SerializeField] private Button purpleSpongeButton;
    [SerializeField] private Button washerUnlockButton;
    [SerializeField] private Button speedUpgradeButton;
    [SerializeField] private Button capacityUpgradeButton;

    [Header("Auto Scratcher")]
    [SerializeField] private AutoScratcher scratcher;
    [SerializeField] private Button scratcherUnlockButton;
    [SerializeField] private Button scratcherSpeedButton;
    [SerializeField] private Button scratcherCapacityButton;
    [Tooltip("自动投喂升级按钮。留空则这个升级在场景里不存在，不会崩，只是买不到。")]
    [SerializeField] private Button autoFeedButton;

    // GADGETS 行的左侧图标。留空则这一行不画图标（RefreshGadgetIcons 自取，不用连引用）。
    [Header("Gadget row icons")]
    [SerializeField] private GadgetRowIcon plateValueIcon;
    [SerializeField] private GadgetRowIcon multiplePlatesIcon;
    [SerializeField] private GadgetRowIcon purpleSpongeIcon;
    [SerializeField] private GadgetRowIcon washerUnlockIcon;
    [SerializeField] private GadgetRowIcon washerSpeedIcon;
    [SerializeField] private GadgetRowIcon washerCapacityIcon;
    [SerializeField] private GadgetRowIcon scratcherUnlockIcon;
    [SerializeField] private GadgetRowIcon scratcherSpeedIcon;
    [SerializeField] private GadgetRowIcon scratcherCapacityIcon;
    [SerializeField] private GadgetRowIcon autoFeedIcon;

    [Header("Shop")]
    [SerializeField] private TMP_Text balanceText;
    [Tooltip("金币不足时抖动的组件。留空会自动到 balanceText 所在的物体上找。")]
    [SerializeField] private MoneyShake moneyShake;
    private CoinGainFeedback coinFeedback;
    [SerializeField] private GameObject ticketsPanel;
    [SerializeField] private GameObject gadgetsPanel;
    [SerializeField] private Button ticketsTabButton;
    [SerializeField] private Button gadgetsTabButton;
    [SerializeField] private GameObject debugPanel;

    // v4：2026-10-04 数值重标定（40 分钟节奏+ 里程碑改倍率 + 通关目标 200 万）。
    // v3：2026-10-01 数值整体重标定（票价 / 洗盘机 / 刮票机 / 全局倍率 / 通关目标）。
    // 旧档的余额与等级在新表下没有意义，直接作废，不做迁移。
    // **里程碑倍率必须存档**：v4 里它是持续生效的全局倍率，
    // 不存的话重开游戏会退回v3 语义（现金奖励），等价于白送一次倍率重置。
    // **Won 同理必须存档**（2026-10-04）：它是「结算画面只弹一次」的唯一依据。
    // 不存的话玩家通关后退出 → 存档里余额仍≥ 目标 → 下次进游戏立刻再弹一次结算。
    private const string SavePrefix = "LotteryPrototype.v4.";
    private bool sessionStarted;
    // Balance is the fallback for existing v4 saves made before the main menu was added.
    public static bool HasSavedGame => PlayerPrefs.GetInt(SavePrefix + "HasStarted", 0) != 0 ||
        PlayerPrefs.HasKey(SavePrefix + "Balance");
    private static readonly string[] SaveKeys = { "Balance", "Gold", "Nova", "Purple", "Washer", "MultiPlate", "MultiPlateLevel", "PlateValueLevel", "Speed", "Capacity", "Scratched0", "Scratched1", "Scratched2", "Scratched3", "Scratched4", "Scratched5", "HeartMatch", "CrossCode", "ZigzagRun", "Scratcher", "ScratchSpeed", "ScratchCapacity", "AutoFeed", "HasStarted", "Milestone0", "Milestone1", "Milestone2", "Milestone3", "Milestone4", "Milestone5", "Won" };


    public void BeginSession() { sessionStarted = true; SaveState(); }
    public void SaveProgress() => SaveState();
    public void HideDebugMenu() { if (debugPanel != null) debugPanel.SetActive(false); }
    public void DiscardSessionForNewGame()
    {
        sessionStarted = false;
        DeleteSavedProgress();
    }
    private static void DeleteSavedProgress()
    {
        foreach (string key in SaveKeys) PlayerPrefs.DeleteKey(SavePrefix + key);
        PlayerPrefs.Save();
    }
    // 多盘解锁后是 5 / 10 / 15 / 20；价格与升级成本读取统一经济表。
    public const int MultiPlateCost = LotteryEconomy.MultiPlateCost;
    public const int MultiPlateBaseCapacity = 5;
    public const int MultiPlateMax = 20;
    private const float PlateSpawnInterval = 0.03f;
    // 自动刮彩票机：基础容量 1 张。周期与各级成本都是数组，等级上限 = 数组长度，
    // 不要再写死 5 —— 经济表改级数时写死的那一份不会跟着变。
    private const int ScratcherUnlockCost = LotteryEconomy.ScratcherUnlockCost;
    private const int ScratcherBaseCapacity = 1;
    private const int AutoFeedCost = LotteryEconomy.AutoFeedCost;
    private const int WinTarget = LotteryEconomy.WinTarget;
    private int balance;
    private readonly int[] scratched = new int[6];
    // 每票已达成的里程碑档数（0..Milestones.Length）。驱动 MilestoneMultiplier()。
    private readonly int[] milestoneStages = new int[6];
    private bool goldUnlocked, novaUnlocked, purpleUnlocked, washerUnlocked;
    private bool heartMatchUnlocked, crossCodeUnlocked, zigzagRunUnlocked;
    private bool multiPlateUnlocked;
    private int multiPlateLevel;
    private int plateValueLevel;
    private Coroutine plateBatch;
    private bool spawningPlates;
    private bool plateSpaceBlocked;
    private int plateStackOrder;
    private int speedLevel, capacityLevel;
    private bool scratcherUnlocked;
    private int scratcherSpeedLevel, scratcherCapacityLevel;
    // 自动投喂：买下的票跳过桌面直接进刮票机。
    private bool autoFeedUnlocked;
    // 通关：持有余额首次达到 WinTarget 时置位。只用于「已经通关」的表现，不拦玩法。
    private bool hasWon;
    // 桌子正上方的 GOAL 牌子（文字 + 进度条 + 百分比）。留空则不显示。
    [SerializeField] private GoalBanner goalBanner;
    // 本次会话的游玩秒数（不含暂停/主菜单）。**刻意不存档**：
    // 它是「这一局花了多久」的报数，不是进度 —— 退出重进应当从0 重新计。
    private float sessionSeconds;
    // 达成瞬间的快照。结算画面显示的是这些值，而不是它被打开那一刻的实时值——
    // 玩家达成后可能已经花钱到目标以下，面板上的战绩不该跟着跳。
    private int victoryBalance;
    private int victoryTickets;
    private float victoryMultiplier;
    private float victorySeconds;
    private LotteryTicket currentTicket;
    // 桌上的脏盘子。MULTIPLE PLATES 解锁前这里最多只有 1 个 —— 也就是旧的 currentPlate 语义。
    // 改成集合之后，「一次只能一个盘子」这件事变成 plateCapacity 的取值，而不是写死的守卫。
    private readonly List<DirtyPlate> plates = new List<DirtyPlate>();
    private int currentTicketKind;
    private bool showingTickets = true;
    // Gadgets 列表里 9 行的**固定语义顺序**（与 ScratcherSetup.GadgetRowNames 逐项对应）。
    // 行按这个顺序传给 PixelRowScroll 排布 —— 不靠场景里的 y / 兄弟顺序推。
    private RectTransform[] gadgetRows;
    // 上一次应用的「解锁可见性」掩码（bit0 = 洗盘机、bit1 = 刮票机）。
    // -1 = 还没应用过，保证 Start 里那一遍一定会跑。
    private int lastGadgetRowMask = -1;
    // TICKETS 列表 6 行的**固定语义顺序**（LUCKY / GOLD / NOVA / HEARTMATCH / CROSSCODE / ZIGZAG）。
    // 与 gadgetRows 同样的道理：顺序由代码给定，不靠场景里的 y 或兄弟顺序推。
    private RectTransform[] ticketRows;
    // 上一次应用的票行可见性掩码（bit0..4 = GOLD / NOVA / HEARTMATCH / CROSSCODE / ZIGZAG 已解锁）。
    // 五个全进掩码：可见性判据里每一票的 unlocked 都可能是「或」的后半截，漏一个就会漏刷新。
    private int lastTicketRowMask = -1;

    // 桌上最多能同时摆几张脏盘子。解锁前后只差这一个数。
    public int PlateCapacity => multiPlateUnlocked ? MultiPlateBaseCapacity * (multiPlateLevel + 1) : 1;
    public int PlateValue => PlateValues[plateValueLevel];
    public int PlateValueLevel => plateValueLevel;

    // 桌上盘子（只读视图，**直接就是内部那个 List**，不是每帧新数组）。
    // SpongeDrag 每帧遍历它擦涂，所以这里不能有分配。
    public IReadOnlyList<DirtyPlate> Plates => plates;
    public int PlateCount => plates.Count;
    public bool MultiPlateUnlocked => multiPlateUnlocked;
    public AutoScratcher Scratcher => scratcher;
    public bool ScratcherUnlocked => scratcherUnlocked;

    // 花了钱但钱不够。只在真正买不起的时候抛 —— 「已经买过 / 没解锁 / 等级满了」
    // 这些原因在各自的入口就 return 了，压根走不到 Spend()，所以不会误报。
    public event System.Action InsufficientFunds;
    // 机器等级：速度与容量各 5 级、合计 0~10，分三档对应三段素材（0 = Retro CRT 最低）。
    private int ScratcherTier
    {
        get
        {
            int total = scratcherSpeedLevel + scratcherCapacityLevel;
            return total <= 3 ? 0 : (total <= 6 ? 1 : 2);
        }
    }
    private int ScratcherCycleSeconds =>
        LotteryEconomy.ScratcherSeconds[Mathf.Clamp(scratcherSpeedLevel, 0, LotteryEconomy.ScratcherSeconds.Length - 1)];
    private int ScratcherCapacitySlots => ScratcherBaseCapacity + scratcherCapacityLevel;

    // ---- 全局产出倍率 ----------------------------------------------------
    //
    // **本局唯一的指数增长源。** 每买下任意一级升级（含各种"解锁"——它们同样是购买），
    // 所有收入线（票结算、手动擦盘、洗盘机、刮票机）都乘一次 BoostPerLevel。
    //
    // 为什么必须集中在一处：各链自带的倍数（洗盘机容量 ×3、盘价值 ×8、票种 ×50）本身
    // 已经是一个不小的跨度，如果链自身也指数化，两者相乘就是双重膨胀 —— 实测会把终局
    // 速率推到目标值的几十倍，通关时长直接崩掉。所以链只负责「功能 + 节奏」，
    // 增长全部由这里承担。改 BoostPerLevel 必须重跑一次校准模拟。
    public int TotalUpgradeLevels
    {
        get
        {
            int levels = 0;
            if (goldUnlocked) levels++;
            if (novaUnlocked) levels++;
            if (heartMatchUnlocked) levels++;
            if (crossCodeUnlocked) levels++;
            if (zigzagRunUnlocked) levels++;
            if (purpleUnlocked) levels++;
            if (multiPlateUnlocked) levels++;
            if (washerUnlocked) levels++;
            if (scratcherUnlocked) levels++;
            if (autoFeedUnlocked) levels++;
            levels += multiPlateLevel + plateValueLevel + speedLevel + capacityLevel;
            levels += scratcherSpeedLevel + scratcherCapacityLevel;
            return levels;
        }
    }

    public float OutputMultiplier => Mathf.Pow(BoostPerLevel, TotalUpgradeLevels);

    // 里程碑倍率（v4）。加性累加：M = 1 + Σ Δ[ticket][已达成档数]。
    // 与 OutputMultiplier **相乘** —— 两者是并列的指数源，不相加。
    // MilestoneMultipliers 是 static 数组，而达成档数是实例状态，所以必须是实例方法。
    public float MilestoneMultiplier()
    {
        float m = 1f;
        for (int i = 0; i < milestoneStages.Length && i < MilestoneMultipliers.Length; i++)
            m += MilestoneMultipliers[i] * milestoneStages[i];
        return m;
    }

    // 总倍率：升级链 × 里程碑。终局约 30.9 × 4.73 = 146x（求解器实测 114.7x，
    // 因为求解器里里程碑按概率逐步命中，不是开局就满）。
    public float TotalMultiplier => OutputMultiplier * MilestoneMultiplier();

    // 所有收入的唯一出口。任何"给钱"的地方都必须走它，否则那条线就不吃倍率，
    // 症状是"买了半天升级，只有一部分数字在涨"。
    private int Gain(int amount) => Mathf.Max(0, Mathf.RoundToInt(amount * TotalMultiplier));

    // 通关：持有余额达到目标。用"持有"而不是"累计赚取"是为了给剧情一个明确的结算点。
    public bool HasWon => hasWon;
    public int WinTargetMoney => WinTarget;
    public bool AutoFeedUnlocked => autoFeedUnlocked;
    // 本局累计刮出的票数（六票合计）。结算面板的 TICKETS 行读它。
    public int TotalTicketsScratched
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < scratched.Length; i++) sum += scratched[i];
            return sum;
        }
    }
    public float SessionSeconds => sessionSeconds;
    private string ScratcherTierName
    {
        get
        {
            if (scratcher != null && !string.IsNullOrEmpty(scratcher.TierName)) return scratcher.TierName;
            return ScratcherTier == 0 ? "RETRO CRT" : (ScratcherTier == 1 ? "CLASSIC BLUE" : "GOLD DELUXE");
        }
    }

    private void Start()
    {
        // Purchases play their result at Spend, without a second generic button click.
        Button[] purchaseButtons =
        {
            luckyTicketButton, goldTicketButton, novaTicketButton, heartMatchTicketButton,
            crossCodeTicketButton, zigzagRunTicketButton, multiplePlatesButton, plateValueButton,
            purpleSpongeButton, washerUnlockButton, speedUpgradeButton, capacityUpgradeButton,
            scratcherUnlockButton, scratcherSpeedButton, scratcherCapacityButton, autoFeedButton
        };
        foreach (Button button in purchaseButtons)
            if (button != null) ButtonSfx.Attach(button).PlayClickSound = false;
        LoadState();
        coinFeedback = GetComponent<CoinGainFeedback>();
        if (coinFeedback != null) coinFeedback.Initialize(balanceText, balance);
        if (sponge != null) sponge.Initialize(this, purpleUnlocked);
        RefreshWasher();
        RefreshScratcher();
        // 必须先回位再 BeginFromSave：PlaceInstantly 是拿机身当前坐标算槽位的。
        ResetDragPositions();
        // 读档时洗盘机已经在跑：直接摆好机内盘子开始洗，不重播解锁入场。
        if (washerUnlocked && washer != null) washer.BeginFromSave();
        if (debugPanel != null) debugPanel.SetActive(false);
        // 不用去场景里连线：MoneyShake 就挂在余额文本上，和 balanceText 是同一个物体。
        if (moneyShake == null && balanceText != null) moneyShake = balanceText.GetComponent<MoneyShake>();
        if (moneyShake != null) InsufficientFunds += moneyShake.Play;
        ShowTickets();
        // 牌子：目标文本只写一次，进度由 RefreshUI 每帧推。
        // Configure 必须在 hasWon 判定之前 —— 否则读档进来的已通关玩家
        // 会先看到 0% 的条，下一帧才跳到 100%（一帧的闪）。
        if (goalBanner != null)
        {
            goalBanner.Configure(WinTarget);
            // 已通关的存档：直接补上完成态。不这么做的症状是重进游戏后
            // 进度条停在 0%，而下面的 hasWon 判定因为 hasWon 已经是 true 不会触发 ——
            // 达成态丢了，且没有任何报错。
            if (hasWon) goalBanner.MarkComplete();
        }
        RefreshUI();
    }

    private void OnDestroy()
    {
        if (moneyShake != null) InsufficientFunds -= moneyShake.Play;
    }

    private void Update()
    {
        // 计时用 unscaled：达成转场 / 暂停期间 timeScale = 0，
        // 用 scaled 会让「这一局玩了多久」在玩家暂停时也停 —— 那正是我们要的，
        // 但达成那一刻 timeScale 已经被 Victory 视图置 0，scaled 会在同一帧
        // 少累计一帧，读数差 0.016s（无所谓，但 unscaled 更直白且不受转场影响）。
        // 累计条件是「没被阻塞」，所以主菜单期间不计时。
        if (!MainMenuScreen.GameplayBlocked) sessionSeconds += Time.unscaledDeltaTime;
        if (MainMenuScreen.GameplayBlocked) return;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) ToggleDebugMenu();
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.F1)) ToggleDebugMenu();
#endif
    }

    public void NewLuckyTicket() => SpawnTicket(luckyTicketPrefab, 0, TicketPrices[0]);
    public void NewGoldTicket() => BuyNewTicket(goldTicketPrefab, 1, ref goldUnlocked);
    public void NewNovaTicket() => BuyNewTicket(novaTicketPrefab, 2, ref novaUnlocked);

    public void NewHeartMatchTicket() => BuyNewTicket(heartMatchTicketPrefab, 3, ref heartMatchUnlocked);
    public void NewCrossCodeTicket() => BuyNewTicket(crossCodeTicketPrefab, 4, ref crossCodeUnlocked);
    public void NewZigzagRunTicket() => BuyNewTicket(zigzagRunTicketPrefab, 5, ref zigzagRunUnlocked);

    private void BuyNewTicket(LotteryTicket prefab, int kind, ref bool unlocked)
    {
        if (!unlocked)
        {
            if (!Spend(UnlockPrices[kind])) return;
            unlocked = true;
            Commit();
            return;
        }
        SpawnTicket(prefab, kind, TicketPrices[kind]);
    }

    private void SpawnTicket(LotteryTicket prefab, int kind, int price)
    {
        if (currentTicket != null || prefab == null || spawnPoint == null || !Spend(price)) return;
        currentTicket = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
        currentTicketKind = kind;
        currentTicket.Initialize(this, kind, currentTicket.RollPrize());
        Commit();
        // 买了自动投喂就直接送进机器。放在 Commit 之后：即使机器满了收不下，
        // 票也已经正常落在桌面上、存档也已写入，不会出现"扣了钱没拿到票"。
        if (autoFeedUnlocked) TryAutoFeed();
    }

    // 自动投喂：买下即送进刮票机，消掉"买票 → 拖进机器"里那段纯体力的拖拽。
    //
    // **刻意不解除 currentTicket 单槽限制**：这一张被机器吞掉之前买不了下一张，
    // 于是瓶颈从"玩家手速 + 拖拽"回到"机器周期" —— 这正是我们要的：机器成为节奏的
    // 决定者，玩家只要按机器节奏点一下买票。配合 ScratcherSeconds（20s→12s）使用。
    private void TryAutoFeed()
    {
        LotteryTicket ticket = currentTicket;
        if (ticket == null || scratcher == null || !scratcherUnlocked || !autoFeedUnlocked) return;
        // 走机器自己的收纳判定，**不经过指针位置检查** —— 自动投喂根本没有指针。
        // 用 CanFeedAt 会因为世界坐标对不上而永远返回 false。
        if (!scratcher.TryAccept(ticket)) return;
        currentTicket = null;
        RefreshUI();
    }

    public void BuyAutoFeed()
    {
        if (autoFeedUnlocked || !Spend(AutoFeedCost)) return;
        autoFeedUnlocked = true;
        Commit();
    }

    // 投放提示与投料共用同一套判定（同 margin、同容量/解锁检查）：
    // 「票缩小了」就等于「松手必收下」，玩家只需学一条规则。
    public bool CanFeedAt(Vector3 worldPoint)
    {
        if (scratcher == null || !scratcherUnlocked) return false;
        if (!scratcher.CanAcceptTicket) return false;
        return scratcher.ContainsPoint(worldPoint, 0.25f);
    }

    // 手动刮开的结算。「已结算」标记统一在这里置位（机器那条路走 LotteryTicket.MarkSettled）。
    public void CompleteTicket(LotteryTicket ticket)
    {
        if (ticket == null || ticket.Settled) return;
        ticket.MarkSettled();
        PayTicket(ticket.Kind, ticket.Prize, ticket.transform);
        // 刮完烟花：双层金白烟火，寿命比停留时间短一截，先于票消失结束。
        // 只有**中奖**（奖金额 > 0）才炸 —— 0 奖也是常见结果，给空奖放烟花等于骗玩家。
        if (ticket.Prize > 0) TicketFirework.Play(ticket.transform.position);
        // 桌子上的那张仍然交给 RemoveTicket 收尾：它要等 disappearDelay 再播「缩小 → 销毁」，
        // 而且 currentTicket 必须留到那时才清空，否则玩家在这几秒里再买一张会被它抢先置空。
        if (ticket == currentTicket) StartCoroutine(RemoveTicket(ticket));
        else RefreshUI();
    }

    // 把票拖进自动刮彩票机时调用：机器收下就把它从桌面摘掉，玩家可以立刻再买一张。
    public bool TryFeedTicket(LotteryTicket ticket, Vector3 worldPoint)
    {
        if (!CanFeedAt(worldPoint)) return false;
        if (!scratcher.TryAccept(ticket)) return false;
        if (ticket == currentTicket)
        {
            currentTicket = null;
            RefreshUI();
        }
        return true;
    }

    // 自动刮彩票机的结算入口：票已经被机器吞走，这里只发钱与计数。
    public void AwardMachineTicket(int kind, int prize)
    {
        PayTicket(kind, prize, scratcher != null ? scratcher.transform : null);
        RefreshUI();
    }

    private void PayTicket(int kind, int prize, Transform source)
    {
        int index = Mathf.Clamp(kind, 0, scratched.Length - 1);
        int oldBalance = balance;
        // 奖金与里程碑奖励都吃全局倍率 —— 里程碑如果漏乘，后期会比一张票的零头还小。
        balance += Gain(prize);
        int count = ++scratched[index];
        // 里程碑从「发一次性现金」改成「提升全局倍率」（2026-10-04，v4）。
        // 所以这里**不能**再调 Gain —— 倍率是持续生效的，不是即时入账。
        // 每达到一个门槛就把该票的 Δ 累加进 MilestoneCount（乘性叠加在倍率上）。
        for (int stage = 0; stage < Milestones.Length; stage++)
        {
            if (count != Milestones[stage]) continue;
            // **先**取旧倍率再改计数，否则 delta 恒为 0（两者都读同一个静态字段）。
            float before = MilestoneMultiplier();
            milestoneStages[index] = Mathf.Max(milestoneStages[index], stage + 1);
            float delta = MilestoneMultiplier() - before;
            if (coinFeedback != null)
                coinFeedback.ShowMultiplier(MilestoneMultipliers[index], delta, source);
        }
        if (balance > oldBalance) LotterySfx.Play(LotterySfx.Sound.CoinGain);
        if (coinFeedback != null) coinFeedback.ShowGain(balance - oldBalance, source);
        Commit();
    }

    private IEnumerator RemoveTicket(LotteryTicket ticket)
    {
        yield return new WaitForSeconds(disappearDelay);
        // 播「缩小 → 自毁」，而不是直接 Destroy —— 原来那一帧凭空消失就是玩家报的问题。
        // currentTicket 仍然占着到**真正销毁**那一刻：玩家在这 0.3s 里买不了新的，
        // 否则旧协程醒来会把玩家刚买那张票的引用当成自己的清掉。
        yield return ShrinkAndWait(ticket != null ? ticket.gameObject : null);
        currentTicket = null;
        RefreshUI();
    }

    // 让物体播完缩小退场再返回。没有挂 ShrinkOut 的物体（或已经没了）退回直接 Destroy，
    // 所以这条路径对「Prefab 漏挂组件」是安全的，不会把物体永久留在场上。
    private IEnumerator ShrinkAndWait(GameObject target)
    {
        if (target == null) yield break;
        ShrinkOut shrink = target.GetComponent<ShrinkOut>();
        if (shrink == null)
        {
            Destroy(target);
            yield break;
        }
        shrink.Play();
        // ShrinkOut 播完会自毁，所以「引用变成 null」和「Playing 变 false」都能结束等待。
        while (shrink != null && shrink.Playing) yield return null;
    }

    public void OneMorePlate()
    {
        if (platePrefab == null || plateSpawnPoint == null || spawningPlates) return;
        PrunePlates();
        int missing = PlateCapacity - plates.Count;
        if (missing <= 0) return;
        plateSpaceBlocked = false;
        spawningPlates = true; // 先占住批次，重复点击不会重复排队。
        plateBatch = StartCoroutine(FillPlates(missing));
    }

    private IEnumerator FillPlates(int missing)
    {
        for (int i = 0; i < missing && plates.Count < PlateCapacity; i++)
        {
            Vector3 spawn = plateSpawnPoint.position;
            spawn.z -= ++plateStackOrder * .00001f; // 同排序层内新盘更靠前，海绵和机器的排序层不变。
            DirtyPlate plate = Instantiate(platePrefab, spawn, plateSpawnPoint.rotation);
            plate.StackOrder = plateStackOrder;
            plate.Initialize(this);
            PlateDragger dragger = plate.GetComponent<PlateDragger>();
            if (dragger != null) dragger.Initialize(this);
            ConfigurePlateLanding(plate);
            PlateFlyIn fly = plate.GetComponent<PlateFlyIn>();
            if (fly != null && !fly.TryEnsureLanding())
            {
                // 玩家移动物件可能堵住空位。绝不退回同一个出生点堆叠。
                Destroy(plate.gameObject);
                plateSpaceBlocked = true;
                break;
            }
            plates.Add(plate); // 包括还在飞的盘子，后续落点按它的目标位置避让。
            RefreshUI();
            if (i + 1 < missing) yield return new WaitForSeconds(PlateSpawnInterval);
        }
        spawningPlates = false;
        plateBatch = null;
        RefreshUI();
    }

    // 摘掉列表里已经被销毁的条目。正常路径（RemovePlate）会自己摘干净，但外部 Destroy
    // （Debug 工具、场景卸载）不会 —— 留着脏条目会白占一个容量名额，
    // 症状是「桌上明明没盘子，却点不出新的」。
    private void PrunePlates()
    {
        for (int i = plates.Count - 1; i >= 0; i--)
            if (plates[i] == null) plates.RemoveAt(i);
    }

    // 同一商店按钮负责首次解锁与后续三次容量升级。出盘仍然免费。
    public void BuyMultiplePlates()
    {
        if (!multiPlateUnlocked)
        {
            if (!Spend(MultiPlateCost)) return;
            multiPlateUnlocked = true;
            multiPlateLevel = 0;
        }
        else
        {
            if (multiPlateLevel >= MultiPlateUpgradeCosts.Length || !Spend(MultiPlateUpgradeCosts[multiPlateLevel])) return;
            multiPlateLevel++;
        }
        plateSpaceBlocked = false;
        Commit();
    }

    // 手洗和洗盘机共用的每盘价值，结算时读取最新等级。
    public void UpgradePlateValue()
    {
        if (plateValueLevel >= PlateValueCosts.Length || !Spend(PlateValueCosts[plateValueLevel])) return;
        plateValueLevel++;
        Commit();
    }

    // 盘子落在桌面哪个位置是随机的，这里有唯一一处「哪些东西不能压」的定义。
    // 两台机器**永远**算禁区：未解锁时它们看不见，但解锁后就在那儿，
    // 不能等那时候才发现盘子已经压在机器身上了。
    // 彩票只在出票那一刻桌上有票时才排除 —— 桌上空着的时候整个桌面都能用。
    private void ConfigurePlateLanding(DirtyPlate plate)
    {
        if (plate == null) return;
        PlateFlyIn flyIn = plate.GetComponent<PlateFlyIn>();
        if (flyIn == null) return;

        var roots = new List<Transform>(plates.Count + 3);
        if (washer != null) roots.Add(washer.transform);
        if (scratcher != null) roots.Add(scratcher.transform);
        if (currentTicket != null) roots.Add(currentTicket.transform);
        // 按最新设计，盘子可以互相堆叠；彩票与机器仍作为出盘禁区。
        flyIn.SetObstacles(roots.ToArray());
    }

    public void CompletePlate(DirtyPlate plate)
    {
        if (!IsPlateOnTable(plate)) return;
        int reward = Gain(PlateValue);
        balance += reward;
        if (reward > 0) LotterySfx.Play(LotterySfx.Sound.CoinGain);
        if (coinFeedback != null) coinFeedback.ShowGain(reward, plate.transform);
        Commit();
        StartCoroutine(RemovePlate(plate));
    }

    // 每张盘子按完成时的当前价值结算，不做「全部擦完才算」的门槛。
    private bool IsPlateOnTable(DirtyPlate plate)
    {
        if (plate == null) return false;
        for (int i = 0; i < plates.Count; i++)
            if (ReferenceEquals(plates[i], plate)) return true;
        return false;
    }

    private IEnumerator RemovePlate(DirtyPlate plate)
    {
        yield return new WaitForSeconds(0.75f);
        yield return ShrinkAndWait(plate != null ? plate.gameObject : null);
        // **到销毁这一刻才算退场**（2026-09-29 定）：缩的那 0.3s 里它仍然占着容量名额、
        // 仍然是机器与新盘落点的禁区。摘列表必须按 ReferenceEquals 找 —— 这时 plate 已经被
        // ShrinkOut 销毁了，而 List.Remove 走的是 Unity 重载过的相等比较，对「已销毁」的引用不可靠
        // （DragBodyRegistry 里为同一个原因也是全程用 ReferenceEquals）。
        for (int i = plates.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(plates[i], plate)) continue;
            plates.RemoveAt(i);
            break;
        }
        if (plate != null) Destroy(plate.gameObject);
        RefreshUI();
    }

    // 清空桌上的盘子（Debug 重置进度用）。part 计费的那条路由 RemovePlate 走。
    private void ClearPlates()
    {
        if (plateBatch != null) StopCoroutine(plateBatch);
        plateBatch = null;
        spawningPlates = plateSpaceBlocked = false;
        for (int i = 0; i < plates.Count; i++)
            if (plates[i] != null) Destroy(plates[i].gameObject);
        plates.Clear();
        plateStackOrder = 0;
    }

    public void BuyPurpleSponge()
    {
        if (purpleUnlocked || !Spend(PurpleSpongeCost)) return;
        purpleUnlocked = true;
        if (sponge != null) sponge.SetAdvanced(true);
        Commit();
    }
    public void BuyWasher()
    {
        if (washerUnlocked || !Spend(WasherUnlockCost)) return;
        washerUnlocked = true;
        RefreshWasher();
        // 买下洗盘机：播一次完整版盘子入场。
        if (washer != null) washer.BeginWithFullEntry();
        Commit();
    }
    public void UpgradeSpeed()
    {
        if (!washerUnlocked || speedLevel >= WasherSpeedCosts.Length || !Spend(WasherSpeedCosts[speedLevel])) return;
        speedLevel++;
        RefreshWasher();
        Commit();
    }
    public void UpgradeCapacity()
    {
        if (!washerUnlocked || capacityLevel >= WasherCapacityCosts.Length || !Spend(WasherCapacityCosts[capacityLevel])) return;
        int previousCapacity = WasherCapacities[capacityLevel];
        capacityLevel++;
        RefreshWasher();
        // 扩容：只补飞容量差对应的新增盘子，机内已有的不动。
        if (washer != null) washer.AddPlates(WasherCapacities[capacityLevel] - previousCapacity);
        Commit();
    }
    public void AwardWashedPlates(int count)
    {
        if (!washerUnlocked || count <= 0) return;
        int reward = Gain(count * PlateValue);
        balance += reward;
        if (reward > 0) LotterySfx.Play(LotterySfx.Sound.CoinGain);
        if (coinFeedback != null && washer != null) coinFeedback.ShowGain(reward, washer.transform);
        Commit();
    }
    private void RefreshWasher()
    {
        if (washer != null) washer.Configure(this, washerUnlocked, WasherSeconds[speedLevel], WasherCapacities[capacityLevel]);
    }

    // ---- 自动刮彩票机 ------------------------------------------------------

    public void BuyScratcher()
    {
        if (scratcherUnlocked || !Spend(ScratcherUnlockCost)) return;
        scratcherUnlocked = true;
        RefreshScratcher(playEntry: true);
        Commit();
    }
    public void UpgradeScratcherSpeed()
    {
        if (!scratcherUnlocked || scratcherSpeedLevel >= ScratcherSpeedCosts.Length || !Spend(ScratcherSpeedCosts[scratcherSpeedLevel])) return;
        scratcherSpeedLevel++;
        RefreshScratcher();
        Commit();
    }
    public void UpgradeScratcherCapacity()
    {
        if (!scratcherUnlocked || scratcherCapacityLevel >= ScratcherCapacityCosts.Length || !Spend(ScratcherCapacityCosts[scratcherCapacityLevel])) return;
        scratcherCapacityLevel++;
        RefreshScratcher();
        Commit();
    }

    // playEntry 只在「刚买下」那一次为 true：速度/容量升级不能重播入场，
    // 否则每点一次升级机器都会从天上重新掉一遍。
    private void RefreshScratcher(bool playEntry = false)
    {
        if (scratcher != null)
            scratcher.Configure(this, scratcherUnlocked, ScratcherCycleSeconds, ScratcherCapacitySlots,
                ScratcherTier, playEntry);
    }

    public void ShowTickets() => SetShopTab(true);
    public void ShowGadgets() => SetShopTab(false);
    private void SetShopTab(bool tickets)
    {
        bool changed = showingTickets != tickets;
        showingTickets = tickets;
        if (ticketsPanel != null) ticketsPanel.SetActive(tickets);
        if (gadgetsPanel != null) gadgetsPanel.SetActive(!tickets);
        RefreshUI();
        if (changed) LotterySfx.Play(LotterySfx.Sound.ShopTab);
    }

    // 未解锁的设备，它那一组「升级」行整行不显示（洗盘机 SPEED/CAPACITY、刮票机 SPEED/CAPACITY）。
    // 解锁按钮（UNLOCK WASHER / UNLOCK SCRATCHER）本身**不算**升级行 —— 那是解锁入口，要一直在。
    //
    // 隐藏不是「留个空档」，而是让 PixelRowScroll 把剩下的行重新紧凑排一遍（见 SetRows）。
    // 只在掩码变化时动一次：RefreshUI 每帧都跑，每帧重排会跟滚动抢位置。
    private void RefreshGadgetRows()
    {
        // 只在运行时排。解锁状态来自存档（LoadState），编辑器里没读过存档、两个 flag 都是默认 false，
        // 一旦让它跑（EconomyShopSetup 会用反射调一次 RefreshUI 来刷商店预览），
        // 就会把「全都没解锁」那一版布局**烘进场景**：4 行被 SetActive(false)、
        // Content 高度 1038→518、rowCount 8→4。那之后 ScratcherSetup 再跑又把高度写回 1038，
        // 两套装配菜单互相打架，0 差异验收也就不成立了。
        if (!Application.isPlaying) return;

        int mask = (washerUnlocked ? 1 : 0) | (scratcherUnlocked ? 2 : 0);
        if (mask == lastGadgetRowMask) return;
        lastGadgetRowMask = mask;

        if (gadgetRows == null)
        {
            Button[] buttons =
            {
                plateValueButton, multiplePlatesButton, purpleSpongeButton, washerUnlockButton, speedUpgradeButton,
                capacityUpgradeButton, scratcherUnlockButton, scratcherSpeedButton, scratcherCapacityButton,
                autoFeedButton
            };
            gadgetRows = new RectTransform[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
                gadgetRows[i] = buttons[i] != null ? buttons[i].transform as RectTransform : null;
        }

        // 顺序必须与上面 buttons 数组逐项对齐（含末尾的 autoFeedButton）。
        // PixelRowScroll.SetRows 会跳过 null 行，所以场景里没挂 autoFeedButton 也不会崩。
        bool[] visible =
        {
            true, true, true, true,
            washerUnlocked, washerUnlocked,
            true,
            scratcherUnlocked, scratcherUnlocked,
            // 自动投喂依附于刮票机：没有机器就无处可投，整行隐藏。
            scratcherUnlocked
        };

        if (gadgetsPanel == null) return;
        PixelRowScroll scroll = gadgetsPanel.GetComponent<PixelRowScroll>();
        if (scroll != null) scroll.SetRows(gadgetRows, visible);
    }

    // 票种按「低阶先解锁」逐级显现：GOLD 没解锁就不显示 NOVA，NOVA 没解锁就不显示 HEARTMATCH，
    // 以此类推。玩家只看见「当前能推进的那一档 + 已经拿下的」，不会被5 个 UNLOCK 按钮同时砸脸。
    // LUCKY 是白送的（UnlockPrices[0] = 0），GOLD 是第一道解锁入口 —— 这两行**永远显示**，
    // 否则玩家看不到任何可点的东西。
    //
    // 隐藏不是「留个空档」，而是让 PixelRowScroll 把剩下的行重新紧凑排一遍（见 SetRows）。
    // 只在掩码变化时动一次：RefreshUI 每帧都跑，每帧重排会跟滚动抢位置。
    private void RefreshTicketRows()
    {
        // 与 RefreshGadgetRows 同一条理由：解锁状态来自存档，编辑器里跑会把「全都没解锁」
        // 那一版布局烘进场景（票行被 SetActive(false)、Content 高度被改）。
        if (!Application.isPlaying) return;

        int mask = (goldUnlocked ? 1 : 0) | (novaUnlocked ? 2 : 0)
            | (heartMatchUnlocked ? 4 : 0) | (crossCodeUnlocked ? 8 : 0)
            | (zigzagRunUnlocked ? 16 : 0);
        if (mask == lastTicketRowMask) return;
        lastTicketRowMask = mask;

        if (ticketRows == null)
        {
            Button[] buttons =
            {
                luckyTicketButton, goldTicketButton, novaTicketButton,
                heartMatchTicketButton, crossCodeTicketButton, zigzagRunTicketButton
            };
            ticketRows = new RectTransform[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
                ticketRows[i] = buttons[i] != null ? buttons[i].transform as RectTransform : null;
        }

        // 顺序与上面 buttons 数组逐项对齐。SetRows 跳过 null 行，场景里少挂一个也不崩。
        //
        // 每行的判据是「自己已解锁**或**前一档已解锁」。后半截是保险：万一存档里出现
        // novaUnlocked && !goldUnlocked（改档 / 旧档迁移），只写前半截会把已经买过的
        // NOVA 那一行藏起来 —— 玩家从此再也点不到它买过的票种，那比多显示一行严重得多。
        bool[] visible =
        {
            true, true,                                    // LUCKY / GOLD：入口，永远在
            goldUnlocked || novaUnlocked,                  // NOVA        ← 要先有 GOLD
            novaUnlocked || heartMatchUnlocked,            // HEARTMATCH  ← 要先有 NOVA
            heartMatchUnlocked || crossCodeUnlocked,       // CROSSCODE   ← 要先有 HEARTMATCH
            crossCodeUnlocked || zigzagRunUnlocked         // ZIGZAG      ← 要先有 CROSSCODE
        };

        if (ticketsPanel == null) return;
        PixelRowScroll scroll = ticketsPanel.GetComponent<PixelRowScroll>();
        if (scroll != null) scroll.SetRows(ticketRows, visible);
    }

    private void RefreshUI()
    {
        PrunePlates();          // 别让已销毁的条目占着容量名额（见 PrunePlates）
        // 通关判定放在刷新入口：所有改余额的路径最后都会走到 RefreshUI，一处覆盖全场。
        if (!hasWon && balance >= WinTarget) TriggerVictory();
        // 进度条每帧推，但达成后GoalBanner 内部锁死不回头 ——
        // 玩家通关后继续花钱，余额掉到目标以下时条不会倒退（那看起来像目标被撤销）。
        if (goalBanner != null)
        {
            if (hasWon) goalBanner.MarkComplete();
            else goalBanner.SetProgress(balance, false);
            // 倍率走**独立通路**，不挂在 SetProgress / MarkComplete 里：
            // 那两个方法在 completed 时会早退（进度条达成后要锁死），
            // 搭车的后果是玩家通关后再升级，倍率文本永久冻结在通关那一刻。
            // GoalBanner 保持纯显示—— 只收参数，不反向读游戏状态。
            goalBanner.SetMultiplier(TotalMultiplier);
        }
        if (coinFeedback != null) coinFeedback.SyncBalance(balance);
        else if (balanceText != null) balanceText.text = "MONEY  " + MoneyFormat.Money(balance);
        RefreshGadgetRows();    // 未解锁的设备不显示它那一组升级行（洗盘机 / 刮票机）
        RefreshTicketRows();    // 票种逐级显现：GOLD 没解锁就不显示 NOVA，以此类推
        RefreshNewTicket(luckyTicketButton, luckyProgressFill, "LUCKY", 0, true);
        RefreshNewTicket(goldTicketButton, goldProgressFill, "GOLD", 1, goldUnlocked);
        RefreshNewTicket(novaTicketButton, novaProgressFill, "NOVA", 2, novaUnlocked);
        RefreshNewTicket(heartMatchTicketButton, heartMatchProgressFill, "HEARTMATCH", 3, heartMatchUnlocked);
        RefreshNewTicket(crossCodeTicketButton, crossCodeProgressFill, "CROSSCODE", 4, crossCodeUnlocked);
        RefreshNewTicket(zigzagRunTicketButton, zigzagRunProgressFill, "ZIGZAG", 5, zigzagRunUnlocked);
        // 盘子按钮：解锁前沿用「一次一张」的旧提示；解锁后到上限时明确说桌子满了。
        // 上限写成具体的 X/12 而不是干巴巴的 FULL —— 玩家一眼知道是「暂时不能再加」，
        // 而不是「按钮坏了」。
        bool plateFull = plates.Count >= PlateCapacity;
        SetLabel(plateButton, PlateButtonLabel(plateFull));
        SetButtonState(plateButton, BuyableState(!plateFull && !spawningPlates));
        bool valueMaxed = plateValueLevel >= PlateValueCosts.Length;
        SetLabel(plateValueButton, valueMaxed
            ? "PLATE VALUE  MAX\n" + MoneyFormat.Money(PlateValue) + " / PLATE" + LevelTag(plateValueLevel, PlateValueCosts)
            : "PLATE VALUE  " + MoneyFormat.Money(PlateValueCosts[plateValueLevel])
                + "\n" + MoneyFormat.Money(PlateValue) + " -> " + MoneyFormat.Money(PlateValues[plateValueLevel + 1])
                + LevelTag(plateValueLevel, PlateValueCosts));
        SetButtonState(plateValueButton, UpgradeState(true, plateValueLevel, PlateValueCosts));
        bool platesMaxed = multiPlateUnlocked && multiPlateLevel >= MultiPlateUpgradeCosts.Length;
        SetLabel(multiplePlatesButton, !multiPlateUnlocked
            ? "MULTIPLE PLATES  $" + MultiPlateCost + "\nPLATES UP TO " + MultiPlateBaseCapacity
            : platesMaxed             ? "MULTIPLE PLATES  MAX\nPLATES UP TO " + PlateCapacity
            : "MULTIPLE PLATES  " + MoneyFormat.Money(MultiPlateUpgradeCosts[multiPlateLevel])
                + "\nUP TO " + (PlateCapacity + MultiPlateBaseCapacity) + LevelTag(multiPlateLevel, MultiPlateUpgradeCosts));
        SetButtonState(multiplePlatesButton, platesMaxed ? ButtonState.Completed
            : BuyableState(balance >= (multiPlateUnlocked ? MultiPlateUpgradeCosts[multiPlateLevel] : MultiPlateCost)));
        SetLabel(purpleSpongeButton, purpleUnlocked ? "PURPLE SPONGE EQUIPPED\nONE-WIPE CLEAN" : "PURPLE SPONGE  " + MoneyFormat.Money(PurpleSpongeCost) + "\nONE-WIPE CLEAN");
        SetButtonState(purpleSpongeButton, purpleUnlocked
            ? ButtonState.Completed
            : BuyableState(balance >= PurpleSpongeCost));
        // 洗盘机这一行显示的产出已经乘过全局倍率 —— 显示"名义值"会让玩家觉得升级没用。
        SetLabel(washerUnlockButton, washerUnlocked
            ? "WASHER RUNNING\nAUTO +" + MoneyFormat.Money(Gain(WasherCapacities[capacityLevel] * PlateValue))
                + " / " + WasherSeconds[speedLevel] + "s"
            : "UNLOCK WASHER  " + MoneyFormat.Money(WasherUnlockCost)
                + "\nAUTO +" + MoneyFormat.Money(WasherCapacities[0] * PlateValue) + " / " + WasherSeconds[0] + "s");
        SetButtonState(washerUnlockButton, washerUnlocked
            ? ButtonState.Completed
            : BuyableState(balance >= WasherUnlockCost));
        // 每行都是两行文案，格式统一成「现值 / 等级 / 价格」——按钮只有 108px 高，三行会挤。
        // 等级上限取数组长度，不再写死 /5；经济表改级数时这里要跟着变。
        SetLabel(speedUpgradeButton, "WASHER SPEED\n" + WasherSeconds[speedLevel] + "s"
            + LevelTag(speedLevel, WasherSpeedCosts) + UpgradePrice(speedLevel, WasherSpeedCosts));
        SetLabel(capacityUpgradeButton, "WASHER CAPACITY\nMAX " + WasherCapacities[capacityLevel]
            + LevelTag(capacityLevel, WasherCapacityCosts) + UpgradePrice(capacityLevel, WasherCapacityCosts));
        SetButtonState(speedUpgradeButton, UpgradeState(washerUnlocked, speedLevel, WasherSpeedCosts));
        SetButtonState(capacityUpgradeButton, UpgradeState(washerUnlocked, capacityLevel, WasherCapacityCosts));

        SetLabel(scratcherUnlockButton, scratcherUnlocked
            ? "SCRATCHER  LV " + (ScratcherTier + 1) + "/3\n" + ScratcherTierName + "  " + ScratcherCycleSeconds + "s  X" + ScratcherCapacitySlots
            : "UNLOCK SCRATCHER  " + MoneyFormat.Money(ScratcherUnlockCost) + "\nAUTO-SCRATCH TICKETS");
        // 解锁后这一行没有任何可点的东西（BuyScratcher 第一句就 return），
        // 所以它不是"暂时用不了"，而是**已完成的终态**：走金色状态牌、摘掉果冻，
        // 别让它继续以"能点"的样子骗玩家去点。
        SetButtonState(scratcherUnlockButton, scratcherUnlocked
            ? ButtonState.Completed
            : BuyableState(balance >= ScratcherUnlockCost));
        SetLabel(scratcherSpeedButton, "SCRATCHER SPEED\n" + ScratcherCycleSeconds + "s"
            + LevelTag(scratcherSpeedLevel, ScratcherSpeedCosts) + UpgradePrice(scratcherSpeedLevel, ScratcherSpeedCosts));
        SetLabel(scratcherCapacityButton, "SCRATCHER CAPACITY\nQUEUE " + ScratcherCapacitySlots
            + LevelTag(scratcherCapacityLevel, ScratcherCapacityCosts) + UpgradePrice(scratcherCapacityLevel, ScratcherCapacityCosts));
        SetButtonState(scratcherSpeedButton, UpgradeState(scratcherUnlocked, scratcherSpeedLevel, ScratcherSpeedCosts));
        SetButtonState(scratcherCapacityButton, UpgradeState(scratcherUnlocked, scratcherCapacityLevel, ScratcherCapacityCosts));

        // 自动投喂：解锁刮票机之后才有意义（没机器就无处可投）。未解锁刮票机时整行隐藏，
        // 见 RefreshGadgetRows 的 visible 表 —— 那里是唯一的行可见性来源。
        SetLabel(autoFeedButton, autoFeedUnlocked
            ? "AUTO FEED ON\nTICKETS GO STRAIGHT IN"
            : "AUTO FEED  " + MoneyFormat.Money(AutoFeedCost) + "\nSKIP THE DRAG");
        SetButtonState(autoFeedButton, autoFeedUnlocked
            ? ButtonState.Completed
            : BuyableState(scratcherUnlocked && balance >= AutoFeedCost));
        SetTabColor(ticketsTabButton, showingTickets);
        SetTabColor(gadgetsTabButton, !showingTickets);
        RefreshGadgetIcons();
    }

    // ==================== 通关 ====================
    // 唯一入口。由 RefreshUI 的判定调用 —— 不挂在某个「给钱」的方法上，
    // 因为给钱的路径太多（票/机器/自动投喂/调试），挂一处必然漏一处。
    private void TriggerVictory()
    {
        hasWon = true;
        // 必须打 TotalMultiplier 而不是 OutputMultiplier —— v4 起里程碑也是一个倍率源，
        // 只报升级链会少算一半（里程碑最高 4.73x）。
        Debug.Log("[挂个爽] WIN | 持有 $" + balance + " 达成通关目标 $" + WinTarget
            + " | 升级级数 " + TotalUpgradeLevels + " | 升级倍率 x" + OutputMultiplier.ToString("0.0")
            + " | 里程碑倍率 x" + MilestoneMultiplier().ToString("0.0")
            + " | 总倍率 x" + TotalMultiplier.ToString("0.0")
            + " | 用时 " + VictoryPanel.FormatTime(sessionSeconds)
            + " | 票数 " + TotalTicketsScratched);

        // 快照：面板显示达成瞬间的值，不是它被打开那一刻的实时值。
        victoryBalance = balance;
        victoryTickets = TotalTicketsScratched;
        victoryMultiplier = TotalMultiplier;
        victorySeconds = sessionSeconds;

        // 进度条立刻锁 100% 变金 —— 必须在转场之前。转场有 1 秒，
        // 这期间条还是「99%」的话，玩家会先看到条没满、再看到面板弹出来。
        if (goalBanner != null) goalBanner.MarkComplete();

        // **先存盘再弹面板**：面板上的 MAIN MENU 按钮会调 SaveProgress，
        // 但达成这一帧如果玩家直接杀掉进程，就什么都留不下。达成是里程碑事件，
        // 值得单独落一次盘（hasWon + Won 键必须同批写，理由见 SaveState 的注释）。
        SaveState();

        MainMenuScreen menu = MainMenuScreen.Instance;
        if (menu != null)
            menu.ShowVictory(victoryBalance, victorySeconds, victoryTickets, victoryMultiplier);
        // 没有菜单实例（理论上不会发生，MainMenuScreen 在场景里常驻）时，
        // 达成态照样落盘、进度条照样变金，只是没有结算面板。不崩就算对。
    }

    // ==================== GADGETS 左侧图标 ====================
    // 点亮数 = 等级 + 1，槽位数固定（= 满级 + 1，由装配菜单写死进场景）。
    // 归一化而不是画真实值：WASHER CAPACITY 真实容量 5→15，15 个盘子挤在 64px 里
    // 每个不到 4px（等于没画）；归一化后所有行同构，且满级必然全亮（GadgetRowIcon.IsFullyLit）。
    //
    // **一次性购买的四行（海绵 / 洗盘机 / 刮票机 / 自动投喂）只写 completed、不写进度** ——
    // 它们没有等级，图标恒定，靠按钮转金色 Completed 表示"已拥有"（决策：不再叠解锁角标）。
    private void RefreshGadgetIcons()
    {
        if (!Application.isPlaying) return;   // 别把存档态烘进场景，同 RefreshGadgetRows 的理由

        // 引用留空时自取一次（GadgetRowIcon 挂在按钮根上）。
        if (plateValueIcon == null) plateValueIcon = IconOn(plateValueButton);
        if (multiplePlatesIcon == null) multiplePlatesIcon = IconOn(multiplePlatesButton);
        if (purpleSpongeIcon == null) purpleSpongeIcon = IconOn(purpleSpongeButton);
        if (washerUnlockIcon == null) washerUnlockIcon = IconOn(washerUnlockButton);
        if (washerSpeedIcon == null) washerSpeedIcon = IconOn(speedUpgradeButton);
        if (washerCapacityIcon == null) washerCapacityIcon = IconOn(capacityUpgradeButton);
        if (scratcherUnlockIcon == null) scratcherUnlockIcon = IconOn(scratcherUnlockButton);
        if (scratcherSpeedIcon == null) scratcherSpeedIcon = IconOn(scratcherSpeedButton);
        if (scratcherCapacityIcon == null) scratcherCapacityIcon = IconOn(scratcherCapacityButton);
        if (autoFeedIcon == null) autoFeedIcon = IconOn(autoFeedButton);

        bool valueMaxed = plateValueLevel >= PlateValueCosts.Length;
        if (plateValueIcon != null) plateValueIcon.SetProgress(plateValueLevel + 1, valueMaxed);

        bool platesMaxed = multiPlateUnlocked && multiPlateLevel >= MultiPlateUpgradeCosts.Length;
        if (multiplePlatesIcon != null)
            // 没解锁时也画 1 个盘子：图标表达"最多能同时有几张"，不是"买了没买"。
            multiplePlatesIcon.SetProgress(multiPlateLevel + 1, platesMaxed);

        bool spongeMaxed = purpleUnlocked;
        if (purpleSpongeIcon != null) purpleSpongeIcon.SetProgress(1, spongeMaxed);
        bool washerMaxed = washerUnlocked;
        if (washerUnlockIcon != null) washerUnlockIcon.SetProgress(1, washerMaxed);

        if (washerSpeedIcon != null)
            washerSpeedIcon.SetProgress(speedLevel + 1, !washerUnlocked || speedLevel >= WasherSpeedCosts.Length);
        if (washerCapacityIcon != null)
            washerCapacityIcon.SetProgress(capacityLevel + 1, !washerUnlocked || capacityLevel >= WasherCapacityCosts.Length);

        bool scratcherMaxed = scratcherUnlocked;
        if (scratcherUnlockIcon != null)
        {
            scratcherUnlockIcon.SetProgress(1, scratcherMaxed);
            // 机身随 tier 换（V3/V1/V2 = 刮票机的三档机身），与 ScratcherTier 同源。
            if (scratcher != null) scratcherUnlockIcon.SetBaseSprite(scratcher.TierSprite(ScratcherTier));
        }
        if (scratcherSpeedIcon != null)
            scratcherSpeedIcon.SetProgress(scratcherSpeedLevel + 1, !scratcherUnlocked || scratcherSpeedLevel >= ScratcherSpeedCosts.Length);
        if (scratcherCapacityIcon != null)
            scratcherCapacityIcon.SetProgress(scratcherCapacityLevel + 1, !scratcherUnlocked || scratcherCapacityLevel >= ScratcherCapacityCosts.Length);

        bool autoFeedMaxed = autoFeedUnlocked;
        if (autoFeedIcon != null) autoFeedIcon.SetProgress(1, autoFeedMaxed);
    }

    private static GadgetRowIcon IconOn(Button button)
    {
        return button != null ? button.GetComponent<GadgetRowIcon>() : null;
    }

    private string PlateButtonLabel(bool plateFull)
    {
        if (!multiPlateUnlocked) return plates.Count > 0 ? "CLEAN THE PLATE FIRST" : "ONE MORE PLATE";
        if (plateFull) return "TABLE FULL  " + plates.Count + "/" + PlateCapacity;
        if (plateSpaceBlocked) return "MOVE ITEMS TO MAKE ROOM";
        return PlateCapacity + " MORE PLATES";
    }

    private void RefreshNewTicket(Button button, Image progress, string name, int kind, bool unlocked)
    {
        SetLabel(button, TicketLabel(name, kind, TicketPrices[kind], unlocked));
        SetTicketDetail(button, TicketDetail(kind, unlocked));
        SetButtonState(button, BuyableState(currentTicket == null && balance >= (unlocked ? TicketPrices[kind] : UnlockPrices[kind])));
        SetProgress(progress, kind);
    }

    private string TicketLabel(string name, int kind, int price, bool unlocked)
    {
        return unlocked
            ? "NEW " + name + "  " + MoneyFormat.Money(price)
            : "UNLOCK " + name + "  " + MoneyFormat.Money(UnlockPrices[kind]);
    }
    private string TicketDetail(int kind, bool unlocked)
    {
        if (!unlocked) return "THEN " + MoneyFormat.Money(TicketPrices[kind]) + " / TICKET";
        int next = NextMilestone(scratched[kind]);
        //里程碑提升的是**全局倍率**，不是产钱速度 —— 所以文案必须说"+X% MULT"。
        // 缩写理由：这行是票面 Details，框宽 562px / fs18 = 最多 31 字符。
        // 完整拼法 `12/25 SCRATCHED   NEXT +8% MULTIPLIER` = 36字符 = 648px 会溢出 86px。
        // 「短行用 MULT、全词 MULTIPLIER」是排版层面的取舍，不是语义取舍——
        // 同一语义在空间充裕处（飘字/ 结算面板）仍然写全词。
        if (next == 0) return "ALL MILESTONES DONE   +" + TotalMilestonePercent(kind) + "% MULT";
        // stage 不再需要（金额随档位变化，用同一个 Δ 即可 —— MilestoneMultipliers 是每票一个值）。
        int delta = Mathf.RoundToInt(MilestoneMultipliers[kind] * 100f);
        return scratched[kind] + "/" + next + " SCRATCHED   NEXT +" + delta + "% MULT";
    }

    // 达成里程碑后的累计倍率百分比，供 TicketDetail 的「全部达成」分支用。
    // 与下面 delta 的单档倍率不同：这条要把 3 档（Milestones.Length）全算进去。
    private int TotalMilestonePercent(int kind)
    {
        return Mathf.RoundToInt(MilestoneMultipliers[kind] * Milestones.Length * 100f);
    }
    private static int NextMilestone(int count)
    {
        foreach (int milestone in Milestones) if (count < milestone) return milestone;
        return 0;
    }
    private void SetProgress(Image image, int kind)
    {
        if (image == null) return;
        int next = NextMilestone(scratched[kind]);
        int previous = next == 10 ? 0 : next == 25 ? 10 : 25;
        float progress = next == 0 ? 1f : (float)(scratched[kind] - previous) / (next - previous);
        image.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        image.rectTransform.sizeDelta = Vector2.zero;
    }
    private static void SetLabel(Button button, string value)
    {
        if (button == null) return;
        Transform labelObject = button.transform.Find("Label");
        TMP_Text label = labelObject != null ? labelObject.GetComponent<TMP_Text>() : null;
        if (label != null) label.text = value;
    }
    private static void SetTicketDetail(Button button, string value)
    {
        if (button == null) return;
        Transform detailObject = button.transform.Find("Details");
        TMP_Text detail = detailObject != null ? detailObject.GetComponent<TMP_Text>() : null;
        if (detail != null) detail.text = value;
    }
    private static void SetTabColor(Button button, bool selected)
    {
        if (button == null) return;
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = selected ? new Color32(49, 111, 133, 255) : new Color32(36, 48, 67, 255);
    }

    // 商店按钮的三档视觉。**恒可点**，不再用 interactable=false 拦点击 ——
    // 那样玩家点下去什么反馈都没有，而「金币不足」恰恰是必须给反馈的情况。
    //
    //   Available   —— 白染色（现在买得起）
    //   Unavailable —— 灰染色（未解锁 / 买不起）
    //   Completed   —— 金底金边 + 关果冻 + 免悬停按下高亮（已拥有 / 已满级，不会再有下一次）
    //
    // Unavailable 沿用 Selectable 自己的颜色通道：把 normalColor 换成 disabledColor。
    // 按钮恒为可用态 → 状态机取的是 normalColor，于是底图变成和原来禁用态**逐像素一致**的灰
    // （同一个 ColorBlock.disabledColor，同一套 sRGB/线性换算，不用手算 alpha 混合）。
    // 悬停/按下仍然会亮起来 —— 它确实是可以点的，只是点了会抖钱。
    //
    // Completed 走不了这条路：ColorBlock 的染色是**乘性**的，底色 RGB(28,44,69) 乘任何 0~1
    // 的 tint 只会更暗，永远乘不出金色。所以金色只能直接写在 targetGraphic.color 上，
    // 同时把 ColorBlock 四态一起压成白色 —— 否则悬停(0.96)/按下(0.78) 仍会让金底明暗跳变，
    // 玩家会以为这块牌子还能点。
    private enum ButtonState
    {
        Available,
        Unavailable,
        Completed
    }

    private static readonly Color CompletedFill = new Color32(74, 58, 26, 255);
    private static readonly Color CompletedBorder = new Color32(198, 158, 64, 255);

    // 按钮的**初始**样式，第一次被 SetButtonState 碰到之前抓一次。
    // 金色是唯一会覆盖 Image.color / Outline.effectColor / ColorBlock 四态的状态，
    // 退出它（Debug 重置进度）时必须还原，不能靠猜原值。
    private struct ButtonStyle
    {
        public Color fill;
        public Color border;
        public ColorBlock colors;
    }

    private readonly Dictionary<Button, ButtonStyle> buttonStyles = new Dictionary<Button, ButtonStyle>();
    private readonly Dictionary<Button, ButtonState> buttonStates = new Dictionary<Button, ButtonState>();

    private void SetButtonState(Button button, ButtonState state)
    {
        if (button == null) return;

        ButtonStyle style;
        if (!buttonStyles.TryGetValue(button, out style))
        {
            Image sourceImage = button.GetComponent<Image>();
            Outline sourceOutline = button.GetComponent<Outline>();
            style = new ButtonStyle();
            style.fill = sourceImage != null ? sourceImage.color : Color.white;
            style.border = sourceOutline != null ? sourceOutline.effectColor : CompletedBorder;
            style.colors = button.colors;
            buttonStyles.Add(button, style);
        }

        // 同状态重复写会让 Selectable 每帧重跑一次 tint（把图形标脏），先短路。
        ButtonState previous;
        if (buttonStates.TryGetValue(button, out previous) && previous == state) return;
        buttonStates[button] = state;

        button.interactable = true;

        ColorBlock colors = style.colors;
        if (state == ButtonState.Completed)
        {
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
        }
        else
        {
            colors.normalColor = state == ButtonState.Available ? Color.white : colors.disabledColor;
        }
        button.colors = colors;

        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = state == ButtonState.Completed ? CompletedFill : style.fill;

        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
            outline.effectColor = state == ButtonState.Completed ? CompletedBorder : style.border;

        // 满级按钮不该再有果冻：组件一关，OnDisable 就会把 scale 复位（含文本的反向缩放），
        // 顺带把指针事件也停掉 —— IPointer*Handler 只在 enabled 时才收事件。
        HoverJelly jelly = button.GetComponent<HoverJelly>();
        if (jelly != null)
        {
            bool wanted = state != ButtonState.Completed;
            if (jelly.enabled != wanted) jelly.enabled = wanted;
        }
    }

    // 两态按钮（票 / 盘子）：只有「现在能不能买」，没有"已拥有"的终态。
    private static ButtonState BuyableState(bool buyable)
    {
        return buyable ? ButtonState.Available : ButtonState.Unavailable;
    }

    // 升级行三态：满级 → 金色状态牌；未解锁 / 买不起 → 灰；否则白。
    private ButtonState UpgradeState(bool unlocked, int level, int[] costs)
    {
        if (unlocked && level >= costs.Length) return ButtonState.Completed;
        return unlocked && balance >= costs[level] ? ButtonState.Available : ButtonState.Unavailable;
    }

    // 满级行不显示价格。价格一律走 MoneyFormat，否则末级那几十万会撑爆按钮。
    private static string UpgradePrice(int level, int[] costs)
    {
        return level >= costs.Length ? "" : "  " + MoneyFormat.Money(costs[level]);
    }
    private static string LevelTag(int level, int[] costs)
    {
        return "  LV " + level + "/" + costs.Length;
    }
    private bool Spend(int amount)
    {
        if (balance < amount)
        {
            LotterySfx.Play(LotterySfx.Sound.InsufficientFunds);
            // 唯一的「买不起」出口。所有购买入口的最后一道判断都是它，
            // 所以金币不足的反馈接在这里就一处覆盖全场。
            if (InsufficientFunds != null) InsufficientFunds();
            return false;
        }
        balance -= amount;
        LotterySfx.Play(LotterySfx.Sound.Select);
        return true;
    }
    private void Commit() { SaveState(); RefreshUI(); }

    private void LoadState()
    {
        balance = PlayerPrefs.GetInt(SavePrefix + "Balance", 0);
        goldUnlocked = PlayerPrefs.GetInt(SavePrefix + "Gold", 0) != 0;
        novaUnlocked = PlayerPrefs.GetInt(SavePrefix + "Nova", 0) != 0;
        heartMatchUnlocked = PlayerPrefs.GetInt(SavePrefix + "HeartMatch", 0) != 0;
        crossCodeUnlocked = PlayerPrefs.GetInt(SavePrefix + "CrossCode", 0) != 0;
        zigzagRunUnlocked = PlayerPrefs.GetInt(SavePrefix + "ZigzagRun", 0) != 0;
        purpleUnlocked = PlayerPrefs.GetInt(SavePrefix + "Purple", 0) != 0;
        washerUnlocked = PlayerPrefs.GetInt(SavePrefix + "Washer", 0) != 0;
        multiPlateUnlocked = PlayerPrefs.GetInt(SavePrefix + "MultiPlate", 0) != 0;
        multiPlateLevel = multiPlateUnlocked ? Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "MultiPlateLevel", 0), 0, MultiPlateUpgradeCosts.Length) : 0;
        plateValueLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "PlateValueLevel", 0), 0, PlateValueCosts.Length);
        speedLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "Speed", 0), 0, WasherSpeedCosts.Length);
        capacityLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "Capacity", 0), 0, WasherCapacityCosts.Length);
        autoFeedUnlocked = PlayerPrefs.GetInt(SavePrefix + "AutoFeed", 0) != 0;
        scratcherUnlocked = PlayerPrefs.GetInt(SavePrefix + "Scratcher", 0) != 0;
        scratcherSpeedLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "ScratchSpeed", 0), 0, ScratcherSpeedCosts.Length);
        scratcherCapacityLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "ScratchCapacity", 0), 0, ScratcherCapacityCosts.Length);
        for (int i = 0; i < scratched.Length; i++)
            scratched[i] = Mathf.Max(0, PlayerPrefs.GetInt(SavePrefix + "Scratched" + i, 0));
        // v4新增：里程碑达成档数。不读的话旧档（键不存在）默认为 0，等于白送一次倍率重置。
        for (int i = 0; i < milestoneStages.Length; i++)
            milestoneStages[i] = Mathf.Clamp(
                PlayerPrefs.GetInt(SavePrefix + "Milestone" + i, 0), 0, Milestones.Length);
        // 通关标记。与里程碑同理：不读 = 重开白送一次结算弹窗，且不报错。
        hasWon = PlayerPrefs.GetInt(SavePrefix + "Won", 0) != 0;
    }
    private void SaveState()
    {
        if (!sessionStarted) return;
        PlayerPrefs.SetInt(SavePrefix + "HasStarted", 1);
        PlayerPrefs.SetInt(SavePrefix + "Balance", balance);
        PlayerPrefs.SetInt(SavePrefix + "Gold", goldUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Nova", novaUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "HeartMatch", heartMatchUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "CrossCode", crossCodeUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "ZigzagRun", zigzagRunUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Purple", purpleUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Washer", washerUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "MultiPlate", multiPlateUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "MultiPlateLevel", multiPlateLevel);
        PlayerPrefs.SetInt(SavePrefix + "PlateValueLevel", plateValueLevel);
        PlayerPrefs.SetInt(SavePrefix + "Speed", speedLevel);
        PlayerPrefs.SetInt(SavePrefix + "Capacity", capacityLevel);
        PlayerPrefs.SetInt(SavePrefix + "AutoFeed", autoFeedUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Scratcher", scratcherUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "ScratchSpeed", scratcherSpeedLevel);
        PlayerPrefs.SetInt(SavePrefix + "ScratchCapacity", scratcherCapacityLevel);
        for (int i = 0; i < scratched.Length; i++) PlayerPrefs.SetInt(SavePrefix + "Scratched" + i, scratched[i]);
        for (int i = 0; i < milestoneStages.Length; i++)
            PlayerPrefs.SetInt(SavePrefix + "Milestone" + i, milestoneStages[i]);
        // 通关标记。**必须与hasWon 字段同批写** —— 漏掉的话玩家每次重进
        // 都会再弹一次结算（这就是 milestoneStages 踩过的同一个坑的第二次重演）。
        PlayerPrefs.SetInt(SavePrefix + "Won", hasWon ? 1 : 0);
        PlayerPrefs.Save();
    }
    private void OnApplicationPause(bool paused) { if (paused) SaveState(); }
    private void OnApplicationQuit() => SaveState();

    // 两台机器与海绵都回设计位（场景里手摆的位置）。启动与 DebugResetProgress 调。
    //
    // **刻意不存档**：机器永远算盘子落点的禁区（见 ConfigurePlateLanding），
    // 玩家把两台都推到桌子中间会永久压缩落点区域，这种局面不该被带到下一次启动。
    private void ResetDragPositions()
    {
        ResetDrag(washer != null ? washer.gameObject : null);
        ResetDrag(scratcher != null ? scratcher.gameObject : null);
        if (sponge != null) sponge.ResetToDesignPosition();
    }

    // 机器上有没有 MachineDrag 是运行时才知道的（组件可能还没装配），
    // 所以这里现取一次 —— 回位是低频操作，不值得为它加一条序列化引用。
    private static void ResetDrag(GameObject target)
    {
        if (target == null) return;
        MachineDrag drag = target.GetComponent<MachineDrag>();
        if (drag != null) drag.ResetToDesignPosition();
    }

    public void ToggleDebugMenu() { if (debugPanel != null) debugPanel.SetActive(!debugPanel.activeSelf); }
    public void DebugAddMoney() { balance += 1000; LotterySfx.Play(LotterySfx.Sound.CoinGain); Commit(); }
    public void DebugUnlockAll()
    {
        goldUnlocked = novaUnlocked = heartMatchUnlocked = crossCodeUnlocked = zigzagRunUnlocked = purpleUnlocked = washerUnlocked = true;
        multiPlateUnlocked = true;
        autoFeedUnlocked = true;
        if (sponge != null) sponge.SetAdvanced(true);
        RefreshWasher();
        if (washer != null) washer.BeginWithFullEntry();
        if (!scratcherUnlocked)
        {
            scratcherUnlocked = true;
            RefreshScratcher(playEntry: true);
        }
        else RefreshScratcher();
        Commit();
    }
    public void DebugSpeedUp() { speedLevel = Mathf.Min(WasherSpeedCosts.Length, speedLevel + 1); RefreshWasher(); Commit(); }
    public void DebugSpeedDown() { speedLevel = Mathf.Max(0, speedLevel - 1); RefreshWasher(); Commit(); }
    public void DebugCapacityUp()
    {
        int previousCapacity = WasherCapacities[capacityLevel];
        capacityLevel = Mathf.Min(WasherCapacityCosts.Length, capacityLevel + 1);
        RefreshWasher();
        if (washer != null) washer.AddPlates(WasherCapacities[capacityLevel] - previousCapacity);
        Commit();
    }
    public void DebugCapacityDown()
    {
        int previousCapacity = WasherCapacities[capacityLevel];
        capacityLevel = Mathf.Max(0, capacityLevel - 1);
        RefreshWasher();
        if (washer != null) washer.AddPlates(WasherCapacities[capacityLevel] - previousCapacity);
        Commit();
    }
    public void DebugResetProgress()
    {
        DeleteSavedProgress();
        StopAllCoroutines();
        if (currentTicket != null) Destroy(currentTicket.gameObject);
        ClearPlates();
        currentTicket = null;
        hasWon = false;
        // 达成态的其余部分一起清：牌子不回初始色的话，重置后进度条还是 100% 金色，
        // 与「已重置」的语义直接矛盾（这是外观与状态脱钩，不是小瑕疵）。
        if (goalBanner != null) goalBanner.ResetProgress();
        victoryBalance = victoryTickets = 0;
        victoryMultiplier = victorySeconds = 0f;
        sessionSeconds = 0f;
        LoadState();
        if (coinFeedback != null) coinFeedback.Initialize(balanceText, balance);
        if (sponge != null) sponge.SetAdvanced(false);
        RefreshWasher();
        RefreshScratcher();
        ResetDragPositions();
        Commit();
    }
}
