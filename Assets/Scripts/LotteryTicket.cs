using TMPro;
using UnityEngine;

// 挂在彩票 Prefab 的根物体上。原有 ScratchCard 不需要修改。
public class LotteryTicket : MonoBehaviour
{
    [Tooltip("票种：0 Lucky / 1 Gold / 2 Nova / 3 HeartMatch / 4 CrossCode / 5 ZigzagRun。")]
    [SerializeField, Range(0, 5)] private int kind;
    [Tooltip("各条目定义原始权重；空奖率统一减去经济表中的百分点，正奖金相对权重不变。")]
    [SerializeField] private int[] prizes = { 0 };
    [SerializeField] private TextMeshPro prizeText;

    [Tooltip("多数字票：按奖格顺序关联数字文本。旧三种票留空。交叉票的第一个是中心目标数。")]
    [SerializeField] private TextMeshPro[] numberTexts;

    private LotteryGame game;
    private TicketFlyIn flyIn;
    private TicketDragger dragger;
    private bool settled;

    public int Prize { get; private set; }
    public int Kind => kind;
    // 已结算（手动刮开 or 被自动刮彩票机消化掉）。防止两边重复给钱。
    public bool Settled => settled;

    public float EmptyPrizeProbability
    {
        get
        {
            if (prizes == null || prizes.Length == 0) return 1f;
            int empty = 0;
            foreach (int prize in prizes) if (prize <= 0) empty++;
            if (empty == prizes.Length) return 1f;
            return Mathf.Max(0f, (float)empty / prizes.Length - LotteryEconomy.EmptyPrizeReduction);
        }
    }

    public int RollPrize()
    {
        if (prizes == null || prizes.Length == 0) return 0;
        if (Random.value < EmptyPrizeProbability) return 0;
        int positives = 0;
        foreach (int prize in prizes) if (prize > 0) positives++;
        if (positives == 0) return 0;
        int pick = Random.Range(0, positives);
        foreach (int prize in prizes) if (prize > 0 && pick-- == 0) return prize;
        return 0;
    }

    private void Awake()
    {
        flyIn = GetComponent<TicketFlyIn>();
        dragger = GetComponent<TicketDragger>();
    }

    public void Initialize(LotteryGame owner, int ticketKind, int prize)
    {
        game = owner;
        kind = ticketKind;
        Prize = prize;
        settled = false;
        if (kind < 3)
        {
            SetText(prizeText, prize.ToString());
        }
        else
        {
            int[] numbers = TicketNumberRules.Generate(kind, prize);
            for (int i = 0; numberTexts != null && i < numberTexts.Length && i < numbers.Length; i++)
                SetText(numberTexts[i], numbers[i].ToString("00"));
            if (kind == 3) SetText(prizeText, "PAIR $" + LotteryEconomy.HeartPairPrize + " / TRIPLE $" + LotteryEconomy.HeartTriplePrize);
            else if (kind == 4) SetText(prizeText, "MATCH CENTER = $" + LotteryEconomy.CrossMatchPrize);
            else
            {
                // 输票也显示可能的正奖金，不能从刮层外的规则文案提前得知中奖结果。
                int offer = prize > 0 ? prize : RollPositivePrize();
                SetText(prizeText, "ASCEND = $" + offer);
            }
        }

        // 入场动画 / 拖拽都要知道自己被谁驱动、桌面落点在哪。
        if (dragger == null) dragger = GetComponent<TicketDragger>();
        if (dragger != null) dragger.Initialize(owner, this);
    }

    private int RollPositivePrize()
    {
        int count = 0;
        foreach (int value in prizes) if (value > 0) count++;
        if (count == 0) return LotteryEconomy.PrizePools[5][3];
        int pick = Random.Range(0, count);
        foreach (int value in prizes) if (value > 0 && pick-- == 0) return value;
        return LotteryEconomy.PrizePools[5][3];
    }

    private static void SetText(TextMeshPro text, string value)
    {
        if (text == null) return;
        text.text = value;
        MeshRenderer renderer = text.GetComponent<MeshRenderer>();
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = 1;
    }

    // 由自动刮彩票机在收下这张票时调用。
    public void MarkSettled() => settled = true;

    // 在 ScratchCard 的 On Revealed 中选择此方法。
    public void Reveal()
    {
        if (settled || game == null) return;
        // 不在这里置 settled：结算与「已结算」标记统一由 LotteryGame.CompleteTicket 负责，
        // 两边都写会让后者以为自己该早退，奖金直接不发。
        game.CompleteTicket(this);
    }
}
