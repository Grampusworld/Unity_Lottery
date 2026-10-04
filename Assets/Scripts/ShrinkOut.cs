using UnityEngine;

// 「先快速缩小、缩到足够小再销毁」的退场收尾。挂到需要这种退场的物体上，
// 由持有者在时机到了的时候调一次 Play()，播完自毁。
//
// 为什么必须是独立组件，而不是在 LotteryGame 的协程里内联写 localScale：
//   ① 项目铁律「同帧不要有两个源写 localScale」—— 写 scale 的那一方得是一个明确的组件，
//      而它同时还要负责「先把别的写者关掉」这件互斥工作。要关的比想象中多：
//      HoverJelly / TicketDragger / PlateDragger 三者的 OnDisable **都会各写一次 scale 复位**，
//      PlateFlyIn 的落地回弹还可能在 IsFlying 变 false 之后继续写。
//   ② AutoScratcher 出槽早就是同一套写法（自己写 scale 写到底再 Destroy），
//      参数也应该跟着它走，免得出现第二套视觉语言。
//
// 参数刻意与 AutoScratcher 出槽一致：0.3s、线性、缩到 5%（payoutDuration / Lerp(1, 0.05, u)）。
[DisallowMultipleComponent]
public class ShrinkOut : MonoBehaviour
{
    [Tooltip("缩小过程的时长（秒）。与 AutoScratcher 出槽的 payoutDuration 一致。")]
    [SerializeField, Min(0.01f)] private float duration = 0.3f;
    [Tooltip("缩到基准尺寸的百分之几。0.05 与 AutoScratcher 出槽一致。\n" +
             "刻意不缩到 0：0 会得到退化变换，而 5% 在屏幕上早就看不见了。")]
    [SerializeField, Range(0.001f, 1f)] private float endScale = 0.05f;

    // 播完之前一直是 true。持有者用它判断「什么时候才算真的退场」：
    // 盘子的容量名额、票的 currentTicket 都是到这一刻才放开（见 LotteryGame.RemoveTicket/RemovePlate）。
    public bool Playing { get; private set; }

    private Vector3 baseScale = Vector3.one;
    private float elapsed;

    // 由持有者调用。**幂等**：重复调不会重新开始 ——
    // 两次退场叠在同一个物体上会把 scale 弹回去，看起来像缩到一半又长回来。
    public void Play()
    {
        if (Playing) return;
        // 顺序要紧：先关掉**别的**写 scale 的组件，再抓基准值。
        // 它们的 OnDisable 都会同步写一次复位，反过来写就会把缩小的第一帧当场抹掉 ——
        // 症状是「卡一下再缩」。
        DisableScaleWriters();
        baseScale = transform.localScale;
        elapsed = 0f;
        Playing = true;
        enabled = true;      // 组件被关掉过也要能自己跑起来，否则 Playing 永远停住
    }

    private void DisableScaleWriters()
    {
        HoverJelly jelly = GetComponent<HoverJelly>();
        if (jelly != null) jelly.enabled = false;

        TicketDragger ticketDragger = GetComponent<TicketDragger>();
        if (ticketDragger != null) ticketDragger.enabled = false;

        PlateDragger plateDragger = GetComponent<PlateDragger>();
        if (plateDragger != null) plateDragger.enabled = false;

        // 两个飞入动画：正常路径下退场时早就落定了（IsFlying 已 false），
        // 关掉是防御性的 —— PlateFlyIn 的 OnDisable 只把 DirtyPlate.Ready 置回 true，不碰 scale，
        // 所以关它是安全的；而落地回弹按理说可能拖到 IsFlying 变 false 之后，留个保险。
        TicketFlyIn ticketFly = GetComponent<TicketFlyIn>();
        if (ticketFly != null) ticketFly.enabled = false;

        PlateFlyIn plateFly = GetComponent<PlateFlyIn>();
        if (plateFly != null) plateFly.enabled = false;

        // DirtyPlate 刻意**不关**：盘子在缩的这 0.3s 里仍然算「在场」——
        // 仍占容量名额、仍是机器与新盘落点的禁区（2026-09-29 定）。
        // 它不会因此被重复结算：ScrubAt 自带 completed 早退。
    }

    private void Update()
    {
        if (!Playing) return;
        elapsed += Time.deltaTime;
        float u = Mathf.Clamp01(elapsed / duration);
        // 线性，与 AutoScratcher 出槽同一套。写 baseScale * k 而不是绝对尺寸：
        // 票的基准缩放是非均匀的 (48.611, 46.194, 50)，写绝对值会把票拉变形。
        float k = Mathf.Lerp(1f, endScale, u);
        transform.localScale = new Vector3(baseScale.x * k, baseScale.y * k, baseScale.z);

        if (u < 1f) return;
        Playing = false;
        Destroy(gameObject);
    }
}
