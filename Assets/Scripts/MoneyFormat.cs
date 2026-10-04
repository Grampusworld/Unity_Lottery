using System.Globalization;

// 金额显示。**全场只有这一个入口**：余额、票价、解锁价、升级价、按钮提示、
// 目标文本都走它。任何地方再手写一次 "$" + 数字，就等于给自己留一个
// 「某块 UI 忘了千分位」的坑。
//
// ## 为什么不再缩写（2026-10-04 改）
// 以前超 6 位就切$100.5K / $1.2M，理由是「像素字体的框塞不下 9 字符长串」。
// 现在**一律完整数字 + 千分位**：缩写会让玩家在两个量级之间来回心算
// （"1.2M 到底是120万还是12万"），而游戏后期余额动辄七位数，精度比省字符重要。
//
// **代价是宽度，所以每个文本框的宽度都必须按完整数字重算** ——
// Press Start 2P 是等宽字体，1 字符 = fontSize px，宽度直接等于 字符数 × 字号。
// 见 ShopLayoutFix.MoneyWidth（余额框）、GoalSetup.LabelWidth（GOAL 标签）。
public static class MoneyFormat
{
    // 带 $ 前缀、千分位、**永不缩写**。负数走递归（`-` 贴在 $ 后面：`-$500`）。
    public static string Money(long amount)
    {
        if (amount < 0) return "-" + Money(-amount);
        return "$" + amount.ToString("N0", CultureInfo.InvariantCulture);
    }

    // **不缩写**的完整形式。历史上它只用在结算面板这种「就是要给玩家看确切数字」
    // 的场合，现在 Money() 本身就恒等于不缩写，所以两者输出一致。
    // 保留这个方法是因为调用点的**语义**不同（结算面板 = 战绩快照），
    // 将来若Money() 重新引入某种压缩，这里是天然的例外出口。
    public static string Full(long amount)
    {
        return Money(amount);
    }

    // 倍率显示。**唯一入口** —— GOAL 区的实时倍率和结算面板的战绩倍率共用它，
    // 所以玩家绝不会在两处看到不同写法（曾经一处"X114.7"、一处"1.08X"）。
    //
    // 格式 `X1.08`：前缀大写 X + **两位小数**。选前缀 X 而不是后缀 X，
    // 是因为结算面板的标签列靠等宽空格对齐（"MONEY   "/"SPEED   "），
    // 值统一从第 9 个字符起笔；前缀 X 让所有值左边界一致。
    // 两位小数而非一位：倍率从 1涨到 100+，一位小数下前期 1.0→1.1 的跳变
    // 会被四舍五入吃掉好几个升级。
    public static string Multiplier(float value)
    {
        return "X" + value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}