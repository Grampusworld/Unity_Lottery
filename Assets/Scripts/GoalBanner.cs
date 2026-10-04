using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 桌子正上方蓝带里的「通关目标」牌子：GOAL $2M + 进度条 + 百分比。
//
// 为什么单独一个组件而不是把三段逻辑塞进 LotteryGame.RefreshUI：
// RefreshUI 每帧跑（余额计数动画期间），而这里要处理三个**各自独立**的更新节奏 ——
// 文字只在初始化写一次、条每帧写 fillAmount、百分比文本只在整数位变化时写。
// 混进 RefreshUI 会让人分不清哪段是每帧的、哪段是幂等的。
//
// **进度条在达成后锁死 100% 并变金**，不跟随余额回退：
// 玩家通关后继续花钱，余额会掉到目标以下，此时进度条若跟着掉就像「目标被撤销了」。
// 语义上这条进度是「你已达成 / 还没达成」，不是「当前余额占比」。
// （判据在 LotteryGame 里是 hasWon 存档键，不在这里 —— 这里只管显示。）
public class GoalBanner : MonoBehaviour
{
    [Header("Goal text")]
    // 目标是常量，所以文字在 Configure 里写一次就够。留空则跳过（场景没装配时不报错）。
    [SerializeField] private TMP_Text goalLabel;

    [Header("Progress bar")]
    // 用 Image.type = Filled + fillMethod Horizontal，fillAmount 是唯一要每帧写的值。
    [SerializeField] private Image barFill;
    [Tooltip("进度条满宽。用于把 fillAmount 换算成像素级台阶，避免 900px 上出现半像素边界。")]
    [SerializeField] private float barWidth = 900f;

    [Header("Percent label")]
    [SerializeField] private TMP_Text percentLabel;

    [Header("Multiplier label")]
    // 当前全局倍率（TotalMultiplier），由 LotteryGame 每帧推进来。
    // **不参与完成态锁死** —— 通关后升级和里程碑仍会改变倍率，冻结在这里就等于
    // 玩家通关后再也不看得到自己变强了。
    [SerializeField] private TMP_Text multiplierLabel;

    [Tooltip("达成后进度条与百分比的颜色（金）。")]
    [SerializeField] private Color completeColor = new Color32(234, 179, 76, 255);
    [SerializeField] private Color normalColor = new Color32(246, 235, 205, 255);

    private int target = 1;
    private bool completed;
    private int lastPercent = -1;
    // 倍率同样做「值没变就不写」的自检：RefreshUI 每帧调，
    // 每帧给 TMP 赋同一个字符串会持续触发网格重建（字体图集不是无限缓存）。
    private float lastMultiplier = -1f;

    // 用 unscaled 驱动：达成那一刻 timeScale 可能已被 MainMenuScreen 置 0，
    // 用 scaled delta 会在同一帧少走一格，进度条看上去「卡一帧才满」。
    private void Update()
    {
        if (completed) return;
        float raw = barFill != null ? barFill.fillAmount : 0f;
        // 吸附到 1px 台阶：fillAmount 是 0..1 浮点，900px 宽的条上
        // 0.0007 的差就是 0.6px —— 边界不在整数像素上，像素风下会看到接缝抖动。
        float snapped = barWidth > 0f ? Mathf.Round(raw * barWidth) / barWidth : raw;
        if (barFill != null && !Mathf.Approximately(barFill.fillAmount, snapped))
            barFill.fillAmount = snapped;
    }

    // **Configure 只写目标文本，不碰百分比与完成态**：
    // 读档进来的已通关玩家，调用顺序是 Configure → MarkComplete，两次都对；
    // 但若有人把顺序写反，Configure 就会把已金色的完成态抹成 0%。
    // 让Configure 对完成态免疫（提前 return）比在调用点约定顺序可靠得多 ——
    // 顺序约定属于「靠人记住」，而这里只需一个 if。
    public void Configure(int goalTarget)
    {
        target = Mathf.Max(1, goalTarget);
        if (goalLabel != null) goalLabel.text = "GOAL  " + MoneyFormat.Money(target);
        if (completed) return;
        lastPercent = -1;
        RefreshPercent(0);
    }

    // balance / completed 由 LotteryGame 每帧传进来。completed 一旦为 true 就锁死。
    public void SetProgress(int balance, bool isComplete)
    {
        if (completed) return;
        float ratio = Mathf.Clamp01((float)balance / target);
        if (barFill != null) barFill.fillAmount = ratio;
        int percent = Mathf.Clamp(Mathf.FloorToInt(ratio * 100f), 0, 100);
        if (percent == lastPercent) return;
        lastPercent = percent;
        RefreshPercent(percent);
        if (isComplete) return;
    }

    //达成：置完成态。**幂等** —— 重复调用不会把已金色的条打回原色。
    public void MarkComplete()
    {
        if (completed) return;
        completed = true;
        if (barFill != null)
        {
            barFill.fillAmount = 1f;
            barFill.color = completeColor;
        }
        if (percentLabel != null)
        {
            percentLabel.text = "DONE";
            percentLabel.color = completeColor;
        }
    }
    public bool IsComplete => completed;

    // 倍率显示。**刻意不受 completed 约束**（与 SetProgress / MarkComplete 相反）——
    // 达成只锁进度条（那是「你已达成」的快照），倍率是**当下状态**，
    // 通关后继续升级 / 攒里程碑仍会变，冻结就等于对玩家说「你变强了但看不出来」。
    public void SetMultiplier(float multiplier)
    {
        // 值没变就整段早退：RefreshUI 每帧调，倍率在两次升级之间是恒定的。
        if (Mathf.Approximately(multiplier, lastMultiplier)) return;
        lastMultiplier = multiplier;
        if (multiplierLabel != null) multiplierLabel.text = MoneyFormat.Multiplier(multiplier);
    }

    // DebugResetProgress 要能把「已达成」态清掉，否则重置后进度条还是金的。
    public void ResetProgress()
    {
        completed = false;
        lastPercent = -1;
        lastMultiplier = -1f;
        if (barFill != null) barFill.color = normalColor;
        if (percentLabel != null) percentLabel.color = normalColor;
        if (barFill != null) barFill.fillAmount = 0f;
        RefreshPercent(0);
    }

    private void RefreshPercent(int percent)
    {
        if (percentLabel == null) return;
        percentLabel.text = percent + "%";
    }
}
