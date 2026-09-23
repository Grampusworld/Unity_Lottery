using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 挂在场景里的 GameManager 上。
public class LotteryGame : MonoBehaviour
{
    [SerializeField] private LotteryTicket ticketPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private TMP_Text balanceText;
    [SerializeField] private Button newTicketButton;
    [SerializeField, Min(0)] private float disappearDelay = 2f;

    // ponytail: 本阶段只用等概率测试金额和内存余额；做经济系统时再加入权重及存档。
    private readonly int[] prizes = { 0, 5, 10, 20, 50 };
    private int balance;
    private LotteryTicket currentTicket;
    private bool settled;

    private void Start()
    {
        balanceText.text = "MONEY: " + balance;
        newTicketButton.interactable = true;
    }

    // 在按钮的 On Click 中选择此方法。
    public void NewTicket()
    {
        if (currentTicket != null) return;
        int prize = prizes[Random.Range(0, prizes.Length)];
        currentTicket = Instantiate(ticketPrefab, spawnPoint.position, spawnPoint.rotation);
        currentTicket.Initialize(this, prize);
        settled = false;
        newTicketButton.interactable = false;
    }

    public void CompleteTicket(LotteryTicket ticket)
    {
        // 只结算当前这张彩票，而且只结算一次。
        if (ticket == null || ticket != currentTicket || settled) return;
        settled = true;
        balance += ticket.Prize;
        balanceText.text = "MONEY: " + balance;
        StartCoroutine(RemoveTicket(ticket));
    }

    private IEnumerator RemoveTicket(LotteryTicket ticket)
    {
        yield return new WaitForSeconds(disappearDelay);
        Destroy(ticket.gameObject);
        currentTicket = null;
        newTicketButton.interactable = true;
    }

    // 可运行的小检查：Play 模式且场上无彩票时，从本组件菜单执行。
    [ContextMenu("Check single payout (Play Mode)")]
    private void CheckSinglePayout()
    {
        if (!Application.isPlaying || currentTicket != null)
        {
            Debug.LogWarning("请进入 Play 模式，并在场上没有彩票时运行此检查。", this);
            return;
        }
        NewTicket();
        // 固定非零金额，避免抽到 0 导致重复结算测试失去意义。
        currentTicket.Initialize(this, 10);
        int before = balance;
        CompleteTicket(currentTicket);
        CompleteTicket(currentTicket);
        bool passed = balance == before + 10 && !newTicketButton.interactable;
        Debug.Assert(passed, "检查失败：重复结算或按钮状态错误。", this);
        if (passed) Debug.Log("检查通过：调用两次结算，余额只增加 10。测试票将在延迟后消失。", this);
    }
}
