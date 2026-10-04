using System.Globalization;

// 大额金额的缩写显示。通关目标抬到 7 位数之后，像素字体的余额框和按钮都塞不下
// 「$1,000,000」这种 9 字符长串，所以超过 6 位就切成 $100.5K / $1.2M。
//
// **全场只有这一个入口**：余额、票价、解锁价、升级价、按钮提示都走它。
// 任何地方再手写一次 "$" + 数字，就等于给自己留一个"某块 UI 忘了缩写"的坑。
public static class MoneyFormat
{
    // 在这个值以内保持完整数字（含千分位分隔符）。像素字体下 6 位还能塞进余额框，
    // 7 位开始就有溢出风险 —— 而 7 位正是 $1,000,000 量级的起点。
    public const long FullUpTo = 99_999L;

    // 带 $ 前缀的完整形式。小于 FullUpTo 用千分位，否则用 K/M/B 后缀。
    public static string Money(long amount)
    {
        if (amount < 0) return "-" + Money(-amount);
        if (amount <= FullUpTo) return "$" + amount.ToString("N0", CultureInfo.InvariantCulture);
        return "$" + Short(amount);
    }

    // 不带 $ 的缩写主体，给「$10 -> $25」这种拼接场合用。
    public static string Short(long amount)
    {
        if (amount < 0) return "-" + Short(-amount);
        if (amount <= FullUpTo) return amount.ToString(CultureInfo.InvariantCulture);

        // 换挡阈值取 999,950 而不是 1,000,000：否则 999,999 会走 K 档、
        // 四舍五入成 1,000.0K —— 显示成 "$1000K" 而不是 "$1M"。
        // 判据是「四舍五入之后还塞不塞得下」，不是「原值有没有过整数门」。
        if (amount >= 999_950_000L) return Trim(amount / 1_000_000_000.0) + "B";
        if (amount >= 999_950L) return Trim(amount / 1_000_000.0) + "M";
        return Trim(amount / 1_000.0) + "K";
    }

    // 保留一位小数，但整数时省掉 ".0"（$1M 比 $1.0M 短一个字符，像素字体下这很值）。
    private static string Trim(double value)
    {
        double rounded = System.Math.Round(value, 1);
        if (System.Math.Abs(rounded - System.Math.Round(rounded)) < 0.05)
            return ((long)System.Math.Round(rounded)).ToString(CultureInfo.InvariantCulture);
        return rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
