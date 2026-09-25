using TMPro;
using UnityEngine;

// 挂在彩票 Prefab 的根物体上。原有 ScratchCard 不需要修改。
public class LotteryTicket : MonoBehaviour
{
    [SerializeField] private int[] prizes = { 0 };
    [SerializeField] private TextMeshPro prizeText;
    private LotteryGame game;
    private bool revealed;
    public int Prize { get; private set; }

    // 每张票的奖池配在自己的 Prefab 上，等概率抽取；奖池留空时按 0 处理。
    public int RollPrize()
    {
        if (prizes == null || prizes.Length == 0) return 0;
        return prizes[Random.Range(0, prizes.Length)];
    }

    public void Initialize(LotteryGame owner, int prize)
    {
        game = owner;
        Prize = prize;
        revealed = false;
        // 出票时就写好数字，由灰色涂层遮住，擦除时逐渐露出。
        prizeText.text = prize.ToString();
        MeshRenderer textRenderer = prizeText.GetComponent<MeshRenderer>();
        textRenderer.sortingLayerName = "Default";
        textRenderer.sortingOrder = 1;
    }

    // 在 ScratchCard 的 On Revealed 中选择此方法。
    public void Reveal()
    {
        if (revealed || game == null) return;
        revealed = true;
        game.CompleteTicket(this);
    }
}
