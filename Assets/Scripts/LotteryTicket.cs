using TMPro;
using UnityEngine;

// 挂在彩票 Prefab 的根物体上。原有 ScratchCard 不需要修改。
public class LotteryTicket : MonoBehaviour
{
    [SerializeField] private TextMeshPro prizeText;
    private LotteryGame game;
    private bool revealed;
    public int Prize { get; private set; }

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
