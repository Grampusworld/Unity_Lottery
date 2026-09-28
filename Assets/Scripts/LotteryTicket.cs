using TMPro;
using UnityEngine;

// 挂在彩票 Prefab 的根物体上。原有 ScratchCard 不需要修改。
public class LotteryTicket : MonoBehaviour
{
    [Tooltip("票种：0 = Lucky / 1 = Gold / 2 = Nova。只用于里程碑计数与统计。")]
    [SerializeField, Range(0, 2)] private int kind;
    [Tooltip("每张票的奖池配在自己的 Prefab 上，等概率抽取；奖池留空时按 0 处理。")]
    [SerializeField] private int[] prizes = { 0 };
    [SerializeField] private TextMeshPro prizeText;

    private LotteryGame game;
    private TicketFlyIn flyIn;
    private TicketDragger dragger;
    private bool settled;

    public int Prize { get; private set; }
    public int Kind => kind;
    // 已结算（手动刮开 or 被自动刮彩票机消化掉）。防止两边重复给钱。
    public bool Settled => settled;

    // 每张票的奖池配在自己的 Prefab 上，等概率抽取；奖池留空时按 0 处理。
    public int RollPrize()
    {
        if (prizes == null || prizes.Length == 0) return 0;
        return prizes[Random.Range(0, prizes.Length)];
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
        // 出票时就写好数字，由灰色涂层遮住，擦除时逐渐露出。
        prizeText.text = prize.ToString();
        MeshRenderer textRenderer = prizeText.GetComponent<MeshRenderer>();
        textRenderer.sortingLayerName = "Default";
        textRenderer.sortingOrder = 1;

        // 入场动画 / 拖拽都要知道自己被谁驱动、桌面落点在哪。
        if (dragger == null) dragger = GetComponent<TicketDragger>();
        if (dragger != null) dragger.Initialize(owner, this);
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
