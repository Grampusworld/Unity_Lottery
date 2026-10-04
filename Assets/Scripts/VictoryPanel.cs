using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 通关结算面板。挂在 MainMenuCanvas 的 VictoryPanel 上，由 MainMenuScreen 的
// ScreenView.Victory 视图控制显隐 —— **不自己管 timeScale / 遮罩 / ESC**，
// 那些已经由 Show() 统一处理（blocked = view != Playing）。
//
// 为什么不给它独立 Canvas：MainMenuScreen.Show() 里「非 Playing 视图 → timeScale = 0 +
// 遮罩 + 解除交互」这套已经踩过坑（blocked 必须含 ScreenFader.Busy），另起一套等于重踩。
//
// **文案宽度必须预先算**：Press Start 2P 等宽，字宽 = 字号。
// 行框宽 580 / fs24 = 24 字符，最长一行是 `MULTIPLIER   X114.70` = 20 字符 = 480px。
// （标签从 SPEED 换成 MULTIPLIER 后这行成了最长的一行，反超 MONEY 行的 18 字符。
// 结算余额可能远超目标 —— 达成后 CONTINUE 钱会继续涨 —— 按 `MONEY   $100,000,000`
// = 20 字符 = 480px 预留，仍在框内。）
public class VictoryPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text moneyLabel;
    [SerializeField] private TMP_Text timeLabel;
    [SerializeField] private TMP_Text ticketLabel;
    [SerializeField] private TMP_Text multiplierLabel;
    [SerializeField] private TMP_Text hintLabel;

    // 统计文案由 LotteryGame 在达成那一刻算好传进来 —— 面板自己不读游戏状态，
    // 否则「达成后继续花钱」会让面板上的数字跟着跳（面板显示的是达成瞬间的快照）。
    //
    // 标签与值用**同一个字符串**（不是两个文本框）：等宽字体下空格本身就是可靠的列对齐，
    // 分两个框反而要手工对 x 值，一个字宽的偏差就让整列参差。
    // 标签统一补到 8 字符宽（"MONEY"+3空格 / "TICKETS"+1空格），值从第 9 个字符起。
    public void Present(int balance, float seconds, int tickets, float multiplier)
    {
        if (titleLabel != null) titleLabel.text = "YOU REACHED THE GOAL!";
        if (moneyLabel != null) moneyLabel.text = "MONEY   " + MoneyFormat.Full(balance);
        if (timeLabel != null) timeLabel.text = "TIME    " + FormatTime(seconds);
        if (ticketLabel != null) ticketLabel.text = "TICKETS " + tickets;
        if (multiplierLabel != null)
            // 走 MoneyFormat.Multiplier 而不自己拼 —— GOAL 区的实时倍率用同一个入口，
            // 否则两处会出现 "X114.7" / "X114.70" 这种同义不同形的写法。
            multiplierLabel.text = "MULTIPLIER   " + MoneyFormat.Multiplier(multiplier);
        if (hintLabel != null) hintLabel.text = "ESC TO CONTINUE";
    }

    // 秒 → "MM MIN SS SEC"。分钟不封顶（超 99 分钟会变成 3 位，宽度按 12 字符预留过）。
    public static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int minutes = total / 60;
        int rest = total % 60;
        // 秒两位补零：像素字体下 "5 SEC" 和 "05 SEC" 宽度一样（等宽），
        // 但补零读起来更整齐，且和 "40 MIN 12 SEC" 的对齐一致。
        return minutes + " MIN " + (rest < 10 ? "0" : "") + rest + " SEC";
    }
}
