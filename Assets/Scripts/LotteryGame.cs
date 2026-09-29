using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Button luckyTicketButton;
    [SerializeField] private Button goldTicketButton;
    [SerializeField] private Button novaTicketButton;
    [SerializeField] private Image luckyProgressFill;
    [SerializeField] private Image goldProgressFill;
    [SerializeField] private Image novaProgressFill;
    [SerializeField, Min(0)] private float disappearDelay = 2f;

    [Header("Dishes")]
    [SerializeField] private DirtyPlate platePrefab;
    [SerializeField] private Transform plateSpawnPoint;
    [SerializeField] private SpongeDrag sponge;
    [SerializeField] private AutomaticDishWasher washer;
    [SerializeField] private Button plateButton;
    [SerializeField] private Button purpleSpongeButton;
    [SerializeField] private Button washerUnlockButton;
    [SerializeField] private Button speedUpgradeButton;
    [SerializeField] private Button capacityUpgradeButton;

    [Header("Auto Scratcher")]
    [SerializeField] private AutoScratcher scratcher;
    [SerializeField] private Button scratcherUnlockButton;
    [SerializeField] private Button scratcherSpeedButton;
    [SerializeField] private Button scratcherCapacityButton;

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

    private const string SavePrefix = "LotteryPrototype.v1.";
    private const int PlatesPerCapacityLevel = 5;   // 每级容量增加的盘子数，与 RefreshWasher 里的公式保持一致
    // 自动刮彩票机：基础 8 秒/张、基础容量 1 张，各 5 级；机器等级由两者之和推导（见 ScratcherTier）。
    private const int ScratcherUnlockCost = 300;
    private const int ScratcherMaxLevel = 5;
    private const int ScratcherBaseSeconds = 8;
    private const int ScratcherBaseCapacity = 1;
    private static readonly int[] Milestones = { 10, 25, 50 };
    private static readonly int[,] MilestoneBonuses =
    {
        { 50, 150, 500 }, { 250, 750, 2500 }, { 1000, 3000, 10000 }
    };

    private int balance;
    private readonly int[] scratched = new int[3];
    private bool goldUnlocked, novaUnlocked, purpleUnlocked, washerUnlocked;
    private int speedLevel, capacityLevel;
    private bool scratcherUnlocked;
    private int scratcherSpeedLevel, scratcherCapacityLevel;
    private LotteryTicket currentTicket;
    private DirtyPlate currentPlate;
    private int currentTicketKind;
    private bool showingTickets = true;

    public DirtyPlate CurrentPlate => currentPlate;
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
    private int ScratcherSeconds => ScratcherBaseSeconds - scratcherSpeedLevel;
    private int ScratcherCapacitySlots => ScratcherBaseCapacity + scratcherCapacityLevel;
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
    }

    private void OnDestroy()
    {
        if (moneyShake != null) InsufficientFunds -= moneyShake.Play;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) ToggleDebugMenu();
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.F1)) ToggleDebugMenu();
#endif
    }

    public void NewLuckyTicket() => SpawnTicket(luckyTicketPrefab, 0, 10);
    public void NewGoldTicket()
    {
        if (!goldUnlocked)
        {
            if (!Spend(100)) return;
            goldUnlocked = true;
            Commit();
            return;
        }
        SpawnTicket(goldTicketPrefab, 1, 50);
    }
    public void NewNovaTicket()
    {
        if (!novaUnlocked)
        {
            if (!Spend(1000)) return;
            novaUnlocked = true;
            Commit();
            return;
        }
        SpawnTicket(novaTicketPrefab, 2, 50);
    }

    private void SpawnTicket(LotteryTicket prefab, int kind, int price)
    {
        if (currentTicket != null || prefab == null || spawnPoint == null || !Spend(price)) return;
        currentTicket = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
        currentTicketKind = kind;
        currentTicket.Initialize(this, kind, currentTicket.RollPrize());
        Commit();
    }

    // 手动刮开的结算。「已结算」标记统一在这里置位（机器那条路走 LotteryTicket.MarkSettled）。
    public void CompleteTicket(LotteryTicket ticket)
    {
        if (ticket == null || ticket.Settled) return;
        ticket.MarkSettled();
        PayTicket(ticket.Kind, ticket.Prize, ticket.transform);
        // 桌子上的那张仍然交给 RemoveTicket 收尾：它要等 2 秒淡出，
        // 而且 currentTicket 必须留到那时才清空，否则玩家在这 2 秒里再买一张会被它抢先置空。
        if (ticket == currentTicket) StartCoroutine(RemoveTicket(ticket));
        else RefreshUI();
    }

    // 把票拖进自动刮彩票机时调用：机器收下就把它从桌面摘掉，玩家可以立刻再买一张。
    public bool TryFeedTicket(LotteryTicket ticket, Vector3 worldPoint)
    {
        if (scratcher == null || ticket == null || !scratcherUnlocked) return false;
        if (!scratcher.ContainsPoint(worldPoint, 0.25f)) return false;
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
        balance += prize;
        int count = ++scratched[index];
        for (int stage = 0; stage < Milestones.Length; stage++)
            if (count == Milestones[stage]) balance += MilestoneBonuses[index, stage];
        if (coinFeedback != null) coinFeedback.ShowGain(balance - oldBalance, source);
        Commit();
    }

    private IEnumerator RemoveTicket(LotteryTicket ticket)
    {
        yield return new WaitForSeconds(disappearDelay);
        if (ticket != null) Destroy(ticket.gameObject);
        currentTicket = null;
        RefreshUI();
    }

    public void OneMorePlate()
    {
        if (currentPlate != null || platePrefab == null || plateSpawnPoint == null) return;
        currentPlate = Instantiate(platePrefab, plateSpawnPoint.position, plateSpawnPoint.rotation);
        currentPlate.Initialize(this);
        ConfigurePlateLanding(currentPlate);
        RefreshUI();
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
        flyIn.SetObstacles(new[]
        {
            washer != null ? washer.transform : null,
            scratcher != null ? scratcher.transform : null,
            currentTicket != null ? currentTicket.transform : null
        });
    }

    public void CompletePlate(DirtyPlate plate)
    {
        if (plate == null || plate != currentPlate) return;
        balance += 1;
        if (coinFeedback != null) coinFeedback.ShowGain(1, plate.transform);
        Commit();
        StartCoroutine(RemovePlate(plate));
    }

    private IEnumerator RemovePlate(DirtyPlate plate)
    {
        yield return new WaitForSeconds(0.75f);
        if (plate != null) Destroy(plate.gameObject);
        currentPlate = null;
        RefreshUI();
    }

    public void BuyPurpleSponge()
    {
        if (purpleUnlocked || !Spend(30)) return;
        purpleUnlocked = true;
        if (sponge != null) sponge.SetAdvanced(true);
        Commit();
    }
    public void BuyWasher()
    {
        if (washerUnlocked || !Spend(200)) return;
        washerUnlocked = true;
        RefreshWasher();
        // 买下洗盘机：播一次完整版盘子入场。
        if (washer != null) washer.BeginWithFullEntry();
        Commit();
    }
    public void UpgradeSpeed()
    {
        if (!washerUnlocked || speedLevel >= 5 || !Spend(1)) return;
        speedLevel++;
        RefreshWasher();
        Commit();
    }
    public void UpgradeCapacity()
    {
        if (!washerUnlocked || capacityLevel >= 5 || !Spend(1)) return;
        capacityLevel++;
        RefreshWasher();
        // 扩容：只补飞新增的那 5 个，机内已有的不动。
        if (washer != null) washer.AddPlates(PlatesPerCapacityLevel);
        Commit();
    }
    public void AwardWashedPlates(int count)
    {
        if (!washerUnlocked || count <= 0) return;
        balance += count;
        if (coinFeedback != null && washer != null) coinFeedback.ShowGain(count, washer.transform);
        Commit();
    }
    private void RefreshWasher()
    {
        if (washer != null) washer.Configure(this, washerUnlocked, 10f - speedLevel, 5 + capacityLevel * 5);
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
        if (!scratcherUnlocked || scratcherSpeedLevel >= ScratcherMaxLevel || !Spend(1)) return;
        scratcherSpeedLevel++;
        RefreshScratcher();
        Commit();
    }
    public void UpgradeScratcherCapacity()
    {
        if (!scratcherUnlocked || scratcherCapacityLevel >= ScratcherMaxLevel || !Spend(1)) return;
        scratcherCapacityLevel++;
        RefreshScratcher();
        Commit();
    }

    // playEntry 只在「刚买下」那一次为 true：速度/容量升级不能重播入场，
    // 否则每点一次升级机器都会从天上重新掉一遍。
    private void RefreshScratcher(bool playEntry = false)
    {
        if (scratcher != null)
            scratcher.Configure(this, scratcherUnlocked, ScratcherSeconds, ScratcherCapacitySlots,
                ScratcherTier, playEntry);
    }

    public void ShowTickets() => SetShopTab(true);
    public void ShowGadgets() => SetShopTab(false);
    private void SetShopTab(bool tickets)
    {
        showingTickets = tickets;
        if (ticketsPanel != null) ticketsPanel.SetActive(tickets);
        if (gadgetsPanel != null) gadgetsPanel.SetActive(!tickets);
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (coinFeedback != null) coinFeedback.SyncBalance(balance);
        else if (balanceText != null) balanceText.text = "MONEY  $" + balance;
        SetLabel(luckyTicketButton, TicketLabel("LUCKY", 0, 10, true));
        SetLabel(goldTicketButton, TicketLabel("GOLD", 1, 50, goldUnlocked));
        SetLabel(novaTicketButton, TicketLabel("NOVA", 2, 50, novaUnlocked));
        SetTicketDetail(luckyTicketButton, TicketDetail(0, true));
        SetTicketDetail(goldTicketButton, TicketDetail(1, goldUnlocked));
        SetTicketDetail(novaTicketButton, TicketDetail(2, novaUnlocked));
        SetButtonState(luckyTicketButton, BuyableState(currentTicket == null && balance >= 10));
        SetButtonState(goldTicketButton, BuyableState(currentTicket == null && balance >= (goldUnlocked ? 50 : 100)));
        SetButtonState(novaTicketButton, BuyableState(currentTicket == null && balance >= (novaUnlocked ? 50 : 1000)));
        SetProgress(luckyProgressFill, 0);
        SetProgress(goldProgressFill, 1);
        SetProgress(novaProgressFill, 2);
        SetLabel(plateButton, currentPlate == null ? "ONE MORE PLATE" : "CLEAN THE PLATE FIRST");
        SetButtonState(plateButton, BuyableState(currentPlate == null));
        SetLabel(purpleSpongeButton, purpleUnlocked ? "PURPLE SPONGE EQUIPPED\n2x BRUSH RADIUS" : "PURPLE SPONGE  $30\n2x BRUSH RADIUS");
        SetButtonState(purpleSpongeButton, purpleUnlocked
            ? ButtonState.Completed
            : BuyableState(balance >= 30));
        SetLabel(washerUnlockButton, washerUnlocked ? "WASHER RUNNING\nAUTO +$" + (5 + capacityLevel * 5) + " / " + (10 - speedLevel) + "s" : "UNLOCK WASHER  $200\nAUTO +$5 / 10s");
        SetButtonState(washerUnlockButton, washerUnlocked
            ? ButtonState.Completed
            : BuyableState(balance >= 200));
        // 每行都是两行文案，格式统一成「现值 / 等级 / 价格」——按钮只有 108px 高，三行会挤。
        // 满级那一行不再挂价格：金色状态牌底下写着 "+$1" 等于说"还能再买一次"。
        SetLabel(speedUpgradeButton, "WASHER SPEED\n" + (10 - speedLevel) + "s  LV " + speedLevel + "/5" + UpgradePrice(speedLevel, 5));
        SetLabel(capacityUpgradeButton, "WASHER CAPACITY\nMAX " + (5 + capacityLevel * 5) + "  LV " + capacityLevel + "/5" + UpgradePrice(capacityLevel, 5));
        SetButtonState(speedUpgradeButton, UpgradeState(washerUnlocked, speedLevel, 5));
        SetButtonState(capacityUpgradeButton, UpgradeState(washerUnlocked, capacityLevel, 5));

        SetLabel(scratcherUnlockButton, scratcherUnlocked
            ? "SCRATCHER  LV " + (ScratcherTier + 1) + "/3\n" + ScratcherTierName + "  " + ScratcherSeconds + "s  X" + ScratcherCapacitySlots
            : "UNLOCK SCRATCHER  $" + ScratcherUnlockCost + "\nAUTO-SCRATCH TICKETS");
        // 解锁后这一行没有任何可点的东西（BuyScratcher 第一句就 return），
        // 所以它不是"暂时用不了"，而是**已完成的终态**：走金色状态牌、摘掉果冻，
        // 别让它继续以"能点"的样子骗玩家去点。
        SetButtonState(scratcherUnlockButton, scratcherUnlocked
            ? ButtonState.Completed
            : BuyableState(balance >= ScratcherUnlockCost));
        SetLabel(scratcherSpeedButton, "SCRATCHER SPEED\n" + ScratcherSeconds + "s  LV " + scratcherSpeedLevel + "/5" + UpgradePrice(scratcherSpeedLevel, ScratcherMaxLevel));
        SetLabel(scratcherCapacityButton, "SCRATCHER CAPACITY\nMAX " + ScratcherCapacitySlots + "  LV " + scratcherCapacityLevel + "/5" + UpgradePrice(scratcherCapacityLevel, ScratcherMaxLevel));
        SetButtonState(scratcherSpeedButton, UpgradeState(scratcherUnlocked, scratcherSpeedLevel, ScratcherMaxLevel));
        SetButtonState(scratcherCapacityButton, UpgradeState(scratcherUnlocked, scratcherCapacityLevel, ScratcherMaxLevel));
        SetTabColor(ticketsTabButton, showingTickets);
        SetTabColor(gadgetsTabButton, !showingTickets);
    }

    private string TicketLabel(string name, int kind, int price, bool unlocked)
    {
        return unlocked ? "NEW " + name + "  $" + price : "UNLOCK " + name + "  $" + (kind == 1 ? 100 : 1000);
    }
    private string TicketDetail(int kind, bool unlocked)
    {
        if (!unlocked) return "THEN $50 / TICKET";
        int next = NextMilestone(scratched[kind]);
        if (next == 0) return "ALL MILESTONES COMPLETE";
        int stage = System.Array.IndexOf(Milestones, next);
        return "SCRATCHED " + scratched[kind] + "/" + next + "   BONUS $" + MilestoneBonuses[kind, stage];
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
    private ButtonState UpgradeState(bool unlocked, int level, int maxLevel)
    {
        if (unlocked && level >= maxLevel) return ButtonState.Completed;
        return unlocked && balance >= 1 ? ButtonState.Available : ButtonState.Unavailable;
    }

    // 满级行不再挂价格：金色状态牌上写着 "+$1"，等于告诉玩家还能再买一次。
    private static string UpgradePrice(int level, int maxLevel)
    {
        return level >= maxLevel ? "" : "  +$1";
    }
    private bool Spend(int amount)
    {
        if (balance < amount)
        {
            // 唯一的「买不起」出口。所有购买入口的最后一道判断都是它，
            // 所以金币不足的反馈接在这里就一处覆盖全场。
            if (InsufficientFunds != null) InsufficientFunds();
            return false;
        }
        balance -= amount;
        return true;
    }
    private void Commit() { SaveState(); RefreshUI(); }

    private void LoadState()
    {
        balance = PlayerPrefs.GetInt(SavePrefix + "Balance", 0);
        goldUnlocked = PlayerPrefs.GetInt(SavePrefix + "Gold", 0) != 0;
        novaUnlocked = PlayerPrefs.GetInt(SavePrefix + "Nova", 0) != 0;
        purpleUnlocked = PlayerPrefs.GetInt(SavePrefix + "Purple", 0) != 0;
        washerUnlocked = PlayerPrefs.GetInt(SavePrefix + "Washer", 0) != 0;
        speedLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "Speed", 0), 0, 5);
        capacityLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "Capacity", 0), 0, 5);
        scratcherUnlocked = PlayerPrefs.GetInt(SavePrefix + "Scratcher", 0) != 0;
        scratcherSpeedLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "ScratchSpeed", 0), 0, ScratcherMaxLevel);
        scratcherCapacityLevel = Mathf.Clamp(PlayerPrefs.GetInt(SavePrefix + "ScratchCapacity", 0), 0, ScratcherMaxLevel);
        for (int i = 0; i < scratched.Length; i++)
            scratched[i] = Mathf.Max(0, PlayerPrefs.GetInt(SavePrefix + "Scratched" + i, 0));
    }
    private void SaveState()
    {
        PlayerPrefs.SetInt(SavePrefix + "Balance", balance);
        PlayerPrefs.SetInt(SavePrefix + "Gold", goldUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Nova", novaUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Purple", purpleUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Washer", washerUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "Speed", speedLevel);
        PlayerPrefs.SetInt(SavePrefix + "Capacity", capacityLevel);
        PlayerPrefs.SetInt(SavePrefix + "Scratcher", scratcherUnlocked ? 1 : 0);
        PlayerPrefs.SetInt(SavePrefix + "ScratchSpeed", scratcherSpeedLevel);
        PlayerPrefs.SetInt(SavePrefix + "ScratchCapacity", scratcherCapacityLevel);
        for (int i = 0; i < scratched.Length; i++) PlayerPrefs.SetInt(SavePrefix + "Scratched" + i, scratched[i]);
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
    public void DebugAddMoney() { balance += 1000; Commit(); }
    public void DebugUnlockAll()
    {
        goldUnlocked = novaUnlocked = purpleUnlocked = washerUnlocked = true;
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
    public void DebugSpeedUp() { speedLevel = Mathf.Min(5, speedLevel + 1); RefreshWasher(); Commit(); }
    public void DebugSpeedDown() { speedLevel = Mathf.Max(0, speedLevel - 1); RefreshWasher(); Commit(); }
    public void DebugCapacityUp()
    {
        capacityLevel = Mathf.Min(5, capacityLevel + 1);
        RefreshWasher();
        if (washer != null) washer.AddPlates(PlatesPerCapacityLevel);
        Commit();
    }
    public void DebugCapacityDown()
    {
        capacityLevel = Mathf.Max(0, capacityLevel - 1);
        RefreshWasher();
        if (washer != null) washer.AddPlates(-PlatesPerCapacityLevel);
        Commit();
    }
    public void DebugResetProgress()
    {
        string[] keys = { "Balance", "Gold", "Nova", "Purple", "Washer", "Speed", "Capacity", "Scratched0", "Scratched1", "Scratched2", "Scratcher", "ScratchSpeed", "ScratchCapacity" };
        foreach (string key in keys) PlayerPrefs.DeleteKey(SavePrefix + key);
        PlayerPrefs.Save();
        StopAllCoroutines();
        if (currentTicket != null) Destroy(currentTicket.gameObject);
        if (currentPlate != null) Destroy(currentPlate.gameObject);
        currentTicket = null;
        currentPlate = null;
        LoadState();
        if (coinFeedback != null) coinFeedback.Initialize(balanceText, balance);
        if (sponge != null) sponge.SetAdvanced(false);
        RefreshWasher();
        RefreshScratcher();
        ResetDragPositions();
        RefreshUI();
    }
}
