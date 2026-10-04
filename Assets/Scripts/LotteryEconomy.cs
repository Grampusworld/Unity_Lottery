// 经济数值的公共来源。预制体奖池由 EconomyShopSetup 写入，运行时沿用原有 prizes 字段。
//
// ==================== 2026-10-04 数值重标定（v4）：40 分钟节奏 ====================
// 目标：通关 = 持有 WinTarget（$2,000,000），实测 40.4 分钟（pivot 见 Tools/solve_economy.py）。
//
// 【三条规则 —— 改任何一个数之前先读这三条】
//
//  ① 价格不再是「速率 × 85 秒」的单一常数。
//     40 分钟节奏的时间点**不均匀**（4/10/15/20/30/40 min，间隔 6/5/5/10/10），
//     而均匀 T 必然让第一个购买点落在 t = T —— 要它落在 4 分钟就得 T = 4 分钟，
//     37点 × 4 分钟 = 148 分钟。**均匀 T 与本节奏数学互斥**，不是调参能解决的。
//     现在每个购买点有独立的回本 T（见下表，回本列）。
//     **要调节奏改 SCHED 里的目标时刻，重跑 Tools/solve_economy.py 重解价格，不要手改单个价格。**
//
//  ② 全局倍率有两个来源，相乘而非相加。
//     BoostPerLevel（每买下任意一级升级 ×1.10）负责「升级链节奏」；
//     MilestoneMultipliers（彩票里程碑）负责「彩票线变强」。终局约 114.7x。
//     **两者都是指数源，不要再引入第三个。** 链内部跨度（洗盘机、盘价值）已冻结，
//     改它们等于重画机器手感，代价远大于收益。
//
//  ③ 内容总成本必须与 WinTarget 同量级。
//     现状 C = $3,624,433 vs W = $2,000,000（C/W = 1.81）。内容 ≳ 目标才不会出现
//     「买光后干等」。C/W 掉到 1 以下意味着后期无事可做。
//
// 【WinTarget 的推导】W = Rf × tail。终局速率 Rf = $11,577/s（累计倍率 114.7x），
//     最后一个购买点在 37.5 分钟，留 2.9 分钟积累 → W = 11,577 × 173 ≈ $2,000,000。
//     **调 W 是纯乘数，不影响任何购买节奏** —— 实测总时长偏差时优先调 W，不要动价格。
//     注意：20/30/50 万已被排除 —— 它们只够 17–43 秒积累，玩家会在 30 分钟就撞线通关。
//
// 【改完必须重跑 Tools/solve_economy.py 校准】，不要靠肉眼估。
// ====================================================================
public static class LotteryEconomy
{
    public static readonly string[] TicketNames = { "LUCKY", "GOLD", "NOVA", "HEARTMATCH", "CROSSCODE", "ZIGZAG" };

    // 票价维持 v3：ROI 统一 1.47–1.50x，是节奏模型的输入，动了要重跑求解器。
    public static readonly int[] TicketPrices = { 13, 31, 68, 134, 300, 625 };

    // 解锁价来自求解器：GOLD 10:00 / NOVA 14:50 / HEARTMATCH 18:00 / CROSSCODE 23:00 / ZIGZAG 30:00
    public static readonly int[] UnlockPrices = { 0, 423, 711, 3005, 17964, 94059 };

    public static readonly int[][] PrizePools = {
        new[] { 0, 5, 10, 20, 50 },                    // LUCKY
        new[] { 0, 0, 25, 25, 50, 50, 75, 100 },       // GOLD
        new[] { 0, 0, 30, 60, 90, 120, 180, 240 },     // NOVA
        new[] { 0, 0, 0, 200, 200, 200, 600 },         // HEARTMATCH
        new[] { 0, 0, 250, 250, 500, 750, 1000 },      // CROSSCODE
        new[] { 0, 0, 0, 700, 700, 1400, 2800 }        // ZIGZAG
    };
    public const int HeartPairPrize = 200;
    public const int HeartTriplePrize = 600;
    public const int CrossMatchPrize = 250;

    // 里程碑门槛不变（10/25/50 张）。
    public static readonly int[] Milestones = { 10, 25, 50 };

    // ==================== 里程碑：现金 → 全局倍率（2026-10-04） ====================
    // 原设计发一次性现金，合计 $6,920 —— 在 40 分钟节奏里只占目标金额的 0.35%，
    // 玩家完全感知不到。改为提升全局倍率 M，语义变为「这条票线让你整体变快」。
    //
    // Δ 按几何级数 d_i = 0.08 × 5^(i/5)：低档票增幅要「明显」，但仍低于高档票。
    //   LUCKY +8.0% → ZIGZAG +40.0%（跨度仅 5x，不是早期方案的 50x）。
    // **跨度是这条曲线的全部意义**：Δ/操作若差 50 倍，理性玩家解锁 ZIGZAG 后
    // 永不回头买 LUCKY，低档票直接变成死内容。
    //
    // 加性累积（M += Δ）：M_max = 4.73x。代价是 milestone 感随M 变大而贬值 ——
    // ZIGZAG 第 50 张名义 +40%，实际只从 4.13x 到 4.53x（+9.7%）。已接受，
    // 因为乘性累积会让 M 复利到 147x，指数源失控。
    public static readonly float[] MilestoneMultipliers = {
        0.08f,   // LUCKY
        0.1104f, // GOLD
        0.1523f, // NOVA
        0.2101f, // HEARTMATCH
        0.2899f, // CROSSCODE
        0.40f    // ZIGZAG
    };

    // 盘子价值：结构冻结（1→8），PV 链是洗盘机收益的乘数之一。
    public static readonly int[] PlateValues = { 1, 2, 3, 5, 8 };
    public static readonly int[] PlateValueCosts = { 41, 105, 183, 398 };

    public const float EmptyPrizeReduction = 0.10f;

    //紫海绵放在第一个（4:00）：擦盘 3.0s → 1.5s 是真倍率提升，
    // 而多盘位只减少「擦完一张要点一次下一张」的空档，收益约 +10–15%，当第一个升级手感太平。
    public const int PurpleSpongeCost = 68;
    public const int MultiPlateCost = 37;
    public static readonly int[] MultiPlateUpgradeCosts = { 705, 776, 562 };

    // 洗盘机：解锁 15:00。结构冻结（容量 ×3、周期 ×2）。
    public const int WasherUnlockCost = 174;
    public static readonly int[] WasherSeconds = { 10, 9, 8, 7, 6, 5 };
    public static readonly int[] WasherCapacities = { 5, 7, 9, 12, 15 };
    public static readonly int[] WasherSpeedCosts = { 1245, 4429, 13368, 477783, 536165 };
    public static readonly int[] WasherCapacityCosts = { 2182, 3541, 51462, 302659 };

    // 刮票机：解锁 20:00。周期 20→12s（v3 起从 8→3s 改上来的，别再改回去：
    // 机器比人手快 6.7 倍会让速度链最后一级的 1 秒永远用不上）。
    public const int ScratcherUnlockCost = 2351;
    public static readonly int[] ScratcherSeconds = { 20, 18, 16, 15, 13, 12 };
    public static readonly int[] ScratcherSpeedCosts = { 15514, 42983, 61214, 245549, 202873 };
    public const int ScratcherBaseCapacity = 1;
    public static readonly int[] ScratcherCapacityCosts = { 70369, 51684, 477585, 394320, 434064 };

    // 自动投喂：买下的票跳过桌面直接进刮票机，消掉「拖拽」这段纯体力操作。
    // 不解除 currentTicket 单槽限制 —— 玩家仍需每 N 秒点一次买票，保留参与感。
    public const int AutoFeedCost = 113865;

    // 通关：持有余额达到这个数即通关。
    public const int WinTarget = 2_000_000;

    // 全局产出倍率（升级链部分）：每买下任意一级升级（含各种"解锁"），所有收入乘一次它。
    // 与 MilestoneMultipliers 相乘得终局 114.7x。校准值，别凭感觉改。
    public const float BoostPerLevel = 1.10f;
}
