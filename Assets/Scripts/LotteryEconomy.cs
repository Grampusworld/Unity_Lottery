// 经济数值的公共来源。预制体奖池由 EconomyShopSetup 写入，运行时沿用原有 prizes 字段。
//
// ==================== 2026-10-01 数值重标定（v3） ====================
// 目标：通关 = 持有 WinTarget（$1,000,000），单局约 60 分钟。
//
// 【三条规则 —— 改任何一个数之前先读这三条】
//
//  ① 价格 = 该级购买瞬间的收入速率 × 85 秒。
//     这就是全部价格表的来源。回本时间恒定 → 玩家的购买间隔均匀 → 成长节奏清晰。
//     实测 37 个购买点，每点间隔 85 秒，购买阶段约 52 分钟，末段积累 7 分钟，合计 59.8 分钟。
//     **不要手改单个价格**：改一处就等于在那个位置插进一个节奏断点。要调节奏改 T（85s）。
//
//  ② 全局倍率 BoostPerLevel 是唯一的指数增长源。
//     各链自带的倍数（洗盘机容量 ×3、盘价值 ×8、票种 ×50）本身已经是一个不小的跨度，
//     如果链自身也指数化，两者相乘就是双重膨胀 —— 实测会把终局速率推到目标值的几十倍。
//     所以链只负责「功能 + 节奏」，增长全部由 BoostPerLevel 承担。校准值 1.10 是解出来的，别凭感觉改。
//
//  ③ 内容总成本（$1,227,852）必须与 WinTarget 同量级。
//     内容远小于目标 = 玩家买光后干等（这是改版前 $46,045 vs $1,000,000 的病）。
//
// 【改完必须重跑一次校准模拟】见 .workbuddy/memory 里的记录，不要靠肉眼估。
// ====================================================================
public static class LotteryEconomy
{
    public static readonly string[] TicketNames = { "LUCKY", "GOLD", "NOVA", "HEARTMATCH", "CROSSCODE", "ZIGZAG" };

    // 票价：ROI 从 ~1.9x 提到 ~1.5x。奖池不动、只提价 ——
    // 原值（10/25/60/100/250/500）下买票是无风险印钞，会把盘子线彻底碾死。
    public static readonly int[] TicketPrices = { 13, 31, 68, 134, 300, 625 };
    public static readonly int[] UnlockPrices = { 0, 83, 668, 2461, 9070, 54796 };

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

    // 里程碑：金额按「该票前 10/25/50 张净利的 25–30%」重算（原值在 $1M 目标下只占 0.27%）。
    public static readonly int[] Milestones = { 10, 25, 50 };
    public static readonly int[,] MilestoneBonuses = {
        { 15, 25, 40 },        // LUCKY
        { 35, 60, 95 },        // GOLD
        { 80, 130, 200 },      // NOVA
        { 150, 250, 400 },     // HEARTMATCH
        { 330, 550, 880 },     // CROSSCODE
        { 680, 1150, 1850 }    // ZIGZAG
    };

    // 盘子价值：1→30 改 1→8。原值 30 倍是全局元凶 —— 它同时放大手动擦盘和洗盘机。
    public static readonly int[] PlateValues = { 1, 2, 3, 5, 8 };
    public static readonly int[] PlateValueCosts = { 38, 420, 1489, 5643 };

    public const float EmptyPrizeReduction = 0.10f;

    public const int PurpleSpongeCost = 34;
    public const int MultiPlateCost = 29;
    public static readonly int[] MultiPlateUpgradeCosts = { 889, 3194, 12071 };

    // 洗盘机：拆掉「容量 × 盘价值」的双指数。容量 5→40 改 5→15（×3），
    // 配上盘价值 ×8 = 满级 15×8/5 = 24 $/s（原 240 $/s）。
    public const int WasherUnlockCost = 103;
    public static readonly int[] WasherSeconds = { 10, 9, 8, 7, 6, 5 };
    public static readonly int[] WasherCapacities = { 5, 7, 9, 12, 15 };
    public static readonly int[] WasherSpeedCosts = { 264, 978, 3513, 13278, 29127 };
    public static readonly int[] WasherCapacityCosts = { 309, 1154, 4210, 16188 };

    // 刮票机：周期 8s→3s 改 20s→12s。
    // 原值下满级 3s/张比玩家投喂速度（买票 + 拖拽 ≈ 4–5s/张）还快，速度链最后一级
    // 花 $12,000 买来的那一秒永远用不上；同时机器比人快 = 挂机产出 ≥ 手动，打穿时长。
    // 改成 20→12s 后机器只比人手（20s/张）快 1.67 倍，队列容量重新有意义。
    public const int ScratcherUnlockCost = 20851;
    public static readonly int[] ScratcherSeconds = { 20, 18, 16, 15, 13, 12 };
    public static readonly int[] ScratcherSpeedCosts = { 39296, 48555, 96973, 119929, 152831 };
    public const int ScratcherBaseCapacity = 1;
    public static readonly int[] ScratcherCapacityCosts = { 44141, 88158, 109026, 138938, 173420 };

    // 自动投喂：买下的票跳过桌面直接进刮票机，消掉「拖拽」这段纯体力操作。
    // 注意它**不解除 currentTicket 单槽限制** —— 玩家仍需每 N 秒点一次买票，
    // 这是刻意的：保留参与感，同时把瓶颈交回给机器。
    public const int AutoFeedCost = 35724;

    // 通关：持有余额达到这个数即通关。
    public const int WinTarget = 1_000_000;

    // 全局产出倍率：每买下任意一级升级（含各种"解锁"），所有收入乘一次它。
    // 37 级 × 1.10 = 约 34 倍；与各链自带的 ~160 倍原始跨度相乘合计约 5,400 倍，
    // 从开局 $0.31/s 推到终局约 $2,244/s。校准值，别凭感觉改。
    public const float BoostPerLevel = 1.10f;
}
