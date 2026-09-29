using System;
using System.Collections.Generic;
using UnityEngine;

// 自动刮彩票机：把票拖进来 → 机器替你刮开 → 一段时间后产出票面奖金。
//
// 状态机：
//   Locked  → 机身隐藏（买下解锁后 PlayEntry() 从上方落下 + 落地果冻）
//   Running → 只要机内还有票就一直跑：迷你票入槽动画 → 转运计时 → 结算出槽
//
// 与洗盘机的差别：
//   ① 洗盘机每周期产出一笔固定钱；这台机器每张票**独立计时**、独立结算，
//      金额取该票抽中的 prize，串行计时（队头完成才轮到下一张起算），容量只是队列长度。
//   ② 机身有三个等级（三段素材），等级由 speedLevel + capacityLevel 推导，
//      换图时机身**脚底不动、向上长**（每张素材的 pivot 都落在内容底边中心）。
//
// 素材的导入设置由 ScratcherSetup 负责：Single / PPU 100 / pivot 在内容底边 / Point / 不压缩。
[RequireComponent(typeof(SpriteRenderer))]
public class AutoScratcher : MonoBehaviour
{
    [Serializable]
    public class Tier
    {
        public string displayName = "TIER";
        public Sprite sprite;
        [Tooltip("工作区在贴图 texel 空间里的矩形 x0,y0,w,h（y 自下往上，原点在贴图左下角）。")]
        public Vector4 workRect = new Vector4(16f, 31f, 32f, 16f);
        [Tooltip("可见内容包围盒 x0,y0,w,h（texel，y 自下往上）。素材是 64×64 且四边有透明留白，" +
                 "命中判定与进度环都必须按它来算，用 sprite.bounds 会把环撑得比机身还大。")]
        public Vector4 contentRect = new Vector4(0f, 0f, 64f, 64f);
        [Tooltip("该等级工作区建议的最大列数。")]
        public int maxColumns = 3;
    }

    [Header("Tiers (0 = 最低级)")]
    [SerializeField] private Tier[] tiers = new Tier[0];

    [Header("References")]
    [SerializeField] private HoverJelly jelly;
    [Tooltip("进度环。尺寸与位置都归它自己算（环在机器**正上方**，直径 = 可见内容宽 × sizeRatio），" +
             "这里只在等级/几何变化时把「我多大、内容中心在哪」推过去。")]
    [SerializeField] private WasherProgressRing ring;
    [SerializeField] private ScratcherEffect effect;
    [Tooltip("迷你票的容器：必须是 scale = 1 的独立物体，否则会被机身缩放带跑。")]
    [SerializeField] private Transform ticketContainer;

    [Header("Feed / Payout")]
    [Tooltip("机内迷你票在槽位里的最大占比。")]
    [SerializeField, Range(0.3f, 1f)] private float slotFill = 0.84f;
    [Tooltip("被吞进来的票整体提升的渲染层数：必须盖住机身，否则迷你票会被机身挡掉。")]
    [SerializeField, Min(0)] private int miniSortingBoost = 6;
    [SerializeField, Min(0.05f)] private float feedDuration = 0.28f;
    [SerializeField, Min(0.05f)] private float payoutDuration = 0.3f;
    [Tooltip("结算时迷你票上浮的距离（世界单位）。")]
    [SerializeField, Min(0f)] private float payoutRise = 2.5f;

    [Header("Entry")]
    [SerializeField, Min(0f)] private float entryDropHeight = 34f;
    [SerializeField, Min(0.1f)] private float entryDuration = 0.55f;

    [Header("Jelly Pulse")]
    [SerializeField] private float feedPulseStrength = 1f;
    [SerializeField] private float completePulseStrength = 1.5f;
    [SerializeField, Range(0.05f, 0.4f)] private float feedPulseDuration = 0.14f;
    [SerializeField, Range(0.05f, 0.4f)] private float completePulseDuration = 0.2f;

    private class Slot
    {
        public LotteryTicket ticket;
        public Transform node;
        public Vector3 enterFrom;
        public Vector3 fromScale;
        public Vector3 ticketBaseScale;
        public Vector3 targetPos;
        public Vector3 targetScale;
        public float stateTime;
        public float elapsed;
        public int phase;      // 0 = 入槽, 1 = 转运, 2 = 出槽
    }

    private readonly List<Slot> slots = new List<Slot>();
    private SpriteRenderer body;
    private LotteryGame game;
    private bool unlocked;
    private float secondsPerTicket = 8f;
    private int capacity = 1;
    private int tierIndex = -1;
    private float entryTimer = -1f;
    private Vector3 entryFrom;
    private Vector3 entryTo;
    private Vector3 baseLossyScale = Vector3.one;
    private Vector3 lastLayoutPosition;

    public bool Unlocked => unlocked;
    public int Count => slots.Count;
    public int TierIndex => Mathf.Max(0, tierIndex);
    public int Capacity => capacity;

    // 现在还能不能再收一张（解锁 + 没满仓）。投放提示（TicketDragger 的缩小反馈）
    // 与 TryAccept 用同一个属性判定，保证「提示缩了」就等于「松手必收」。
    public bool CanAcceptTicket => unlocked && ticketContainer != null && slots.Count < capacity;

    public string TierName =>
        (tiers != null && tierIndex >= 0 && tierIndex < tiers.Length) ? tiers[tierIndex].displayName : "";

    // 正在播入场下落（还没落地）。计数牌要在这段时间藏起来：锚点跟着 transform 走，
    // 而 transform 还在半空，字会从屏幕上方一路飘下来。
    public bool Entering => entryTimer >= 0f;

    // 机身**可见内容**的底边中点（世界坐标），用来把计数牌摆到机器下方。
    // 不能直接用 transform.position：三张素材的 pivot 现在恰好都落在内容底边上，
    // 但那是装配时实测出来的巧合，不是这个类对外承诺的接口。
    public Vector3 ContentBottomWorld
    {
        get
        {
            if (body == null || body.sprite == null) return transform.position;
            ContentRectWorld(out Vector3 center, out Vector2 size);
            return new Vector3(center.x, center.y - size.y * 0.5f, transform.position.z);
        }
    }

    private void Awake()
    {
        body = GetComponent<SpriteRenderer>();
        // 静止尺寸只取一次，且必须避开果冻缩放（否则工作区会跟着抖）。
        baseLossyScale = transform.lossyScale;
        body.enabled = false;
        lastLayoutPosition = transform.position;
    }

    private void OnDestroy() => ClearAll(false);

    // ---- 对外接口 ----------------------------------------------------------

    public void Configure(LotteryGame owner, bool active, float seconds, int cap, int tier, bool playEntry)
    {
        game = owner;
        secondsPerTicket = Mathf.Max(0.5f, seconds);
        capacity = Mathf.Max(1, cap);
        bool wasUnlocked = unlocked;
        unlocked = active;
        if (body == null) body = GetComponent<SpriteRenderer>();

        ApplyTier(tier);
        body.enabled = active;
        // ApplyTier 在「等级没变」时会 early-return（ApplyTierVisuals 也就不会跑），
        // 所以锚点必须在这里再无条件推一次，否则第二次 Configure 之后环会停在旧位置。
        UpdateRingAnchor();

        if (!active)
        {
            // 入场下落被中途取消时（Debug 重置进度正好落在 0.55s 的窗口里就会），
            // 光清计时器会把机身**永久留在半空**：下一次解锁时 PlayEntry 拿"当前位置"当停机位，
            // 机器就停在天上了。所以取消入场必须同时把机身放回停机位。
            if (entryTimer >= 0f)
            {
                entryTimer = -1f;
                transform.position = entryTo;
            }
            ClearAll(true);
            if (ring != null) ring.Hide(true);
            if (effect != null) effect.SetRunning(false);
            return;
        }

        // 容量被调小（Debug 面板）：多出来的票立刻结算并移除，不凭空消失。
        while (slots.Count > capacity) PayAndRemove(slots.Count - 1);
        RelayoutSlots();
        if (!wasUnlocked && playEntry) PlayEntry();
    }

    public void PlayEntry()
    {
        entryTo = transform.position;
        entryFrom = entryTo + Vector3.up * entryDropHeight;
        entryTimer = 0f;
        transform.position = entryFrom;
    }

    public bool ContainsPoint(Vector3 world, float margin)
    {
        if (body == null || body.sprite == null) return false;
        Vector3 center;
        Vector2 size;
        if (HasContentRect())
        {
            ContentRectWorld(out center, out size);
        }
        else
        {
            center = transform.position + Vector3.Scale(body.sprite.bounds.center, baseLossyScale);
            Vector3 fallback = Vector3.Scale(body.sprite.bounds.size, baseLossyScale);
            size = new Vector2(fallback.x, fallback.y);
        }
        float halfX = size.x * 0.5f * (1f + margin);
        float halfY = size.y * 0.5f * (1f + margin);
        Vector3 d = world - center;
        return Mathf.Abs(d.x) <= halfX && Mathf.Abs(d.y) <= halfY;
    }

    // 收下一张票。返回 false 表示机器没解锁 / 已满，调用方要把票退回桌面。
    public bool TryAccept(LotteryTicket ticket)
    {
        if (!unlocked || ticket == null || ticket.Settled || ticketContainer == null) return false;
        if (slots.Count >= capacity) return false;

        ticket.MarkSettled();
        Transform node = ticket.transform;

        // 半途被吞进来的票：必须停掉它自己的入场动画与拖拽组件。
        // 正常玩法走不到这里（入场期间交互是关着的），但一旦走到，
        // TicketFlyIn 会继续每帧改 position/scale，把槽位摆位顶掉。
        TicketFlyIn fly = node.GetComponent<TicketFlyIn>();
        if (fly != null) fly.enabled = false;
        TicketDragger dragger = node.GetComponent<TicketDragger>();
        if (dragger != null) dragger.enabled = false;      // 注意：OnDisable 会把涂层输入重新打开
        ScratchCard cover = node.GetComponentInChildren<ScratchCard>(true);
        if (cover != null) cover.InputEnabled = false;     // 机内的票不该再被玩家刮

        // 票自己的悬停果冻也要关掉：槽位动画每帧写 node.localScale（入槽 lerp + 处理中的抖动），
        // 果冻同帧再写一次就是两个源抢同一个属性 —— 症状是迷你票在槽里抖、尺寸不对。
        HoverJelly ticketJelly = node.GetComponent<HoverJelly>();
        if (ticketJelly != null) ticketJelly.enabled = false;

        Vector3 enterFrom = node.position;
        Vector3 fromScale = fly != null ? fly.BaseScale : node.localScale;
        node.SetParent(ticketContainer, true);   // 容器 scale = 1，localScale 即世界缩放
        RaiseSorting(node);

        var slot = new Slot
        {
            ticket = ticket,
            node = node,
            enterFrom = enterFrom,
            fromScale = fromScale,
            ticketBaseScale = fromScale,
            phase = 0,
            stateTime = 0f,
            elapsed = 0f
        };
        slots.Add(slot);
        RelayoutSlots();

        if (jelly != null) jelly.Pulse(feedPulseStrength, feedPulseDuration);
        if (effect != null) effect.SetRunning(true);
        return true;
    }

    public void ClearAll(bool pay)
    {
        while (slots.Count > 0)
        {
            if (pay) PayAndRemove(slots.Count - 1);
            else
            {
                Slot last = slots[slots.Count - 1];
                if (last.node != null) Destroy(last.node.gameObject);
                slots.RemoveAt(slots.Count - 1);
            }
        }
        RelayoutSlots();
    }

    // ---- 换等级 ------------------------------------------------------------

    // 票 Prefab 的渲染层是给「桌面」排的（机身 0 / 数字 1 / 涂层 2），机身是 4，
    // 不整体抬起来的话迷你票会被机身整个盖住。整体加同一个偏移，票内部的相对层次不变。
    private void RaiseSorting(Transform node)
    {
        if (miniSortingBoost <= 0) return;
        int layer = body != null ? body.sortingLayerID : 0;
        Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].sortingLayerID = layer;
            renderers[i].sortingOrder += miniSortingBoost;
        }
    }

    private void ApplyTier(int tier)
    {
        if (tiers == null || tiers.Length == 0) return;
        int clamped = Mathf.Clamp(tier, 0, tiers.Length - 1);
        if (clamped == tierIndex) return;
        tierIndex = clamped;
        if (body != null && tiers[clamped].sprite != null) body.sprite = tiers[clamped].sprite;
        ApplyTierVisuals();
        RelayoutSlots();
    }

    // 三张素材的内容区大小不一样（44×52 / 46×59 / 52×61 texel），
    // 所以工作区与进度环都必须按当前 sprite 现算，不能只在装配时定死一次。
    private void ApplyTierVisuals()
    {
        if (body == null || body.sprite == null) return;

        WorkRectLocal(out Vector2 center, out Vector2 size);
        if (effect != null) effect.SetWorkRect(center, size, body);
        UpdateRingAnchor();
    }

    // 把「机身可见内容包围盒」推给进度环。位置与尺寸由环自己算：
    // 直径 = 内容宽 × sizeRatio，环心 = 内容顶边 + 间隙 + 半径 —— 环落在机身正上方，与机身零重叠。
    //
    // 用 contentRect（实测不透明区）而不是 sprite.bounds：三张素材都是 64×64 且四边留白不一，
    // sprite.bounds 会让环比机身可见宽还大（旧版本就是 30.4 vs 17.5，环飘在机外一圈）。
    private void UpdateRingAnchor()
    {
        if (ring == null || body == null || body.sprite == null) return;
        ContentRectWorld(out Vector3 center, out Vector2 size);
        ring.SetMachineAnchor(body, center, size);
    }

    // 机身只在**真的动了**的时候才重排内部的绝对坐标（槽位里的迷你票）。
    // 拖动 / 惯性滑行 / 入场下落三条路径都会经过这里 —— 以前靠 MachineDrag 显式调
    // FollowMachine()，而它在滑行分支直接 return，于是甩一把机器、一槽迷你票就留在原地。
    private void LateUpdate()
    {
        Vector3 now = transform.position;
        if (now == lastLayoutPosition) return;
        lastLayoutPosition = now;
        RelayoutSlots();
    }

    // 可见内容包围盒（世界空间：中心 + 尺寸）。抓取判定与拖动夹取都用它 ——
    // 三段素材都是 64×64 且四边有透明留白，而且 pivot 落在内容底边，
    // 所以 transform.position 既不是矩形中心，sprite.bounds 也不是可见范围。
    public bool TryGetContentRectWorld(out Vector3 center, out Vector2 size)
    {
        if (body == null || body.sprite == null)
        {
            center = transform.position;
            size = Vector2.one;
            return false;
        }
        ContentRectWorld(out center, out size);
        return true;
    }

    private bool HasContentRect()
    {
        return tiers != null && tierIndex >= 0 && tierIndex < tiers.Length
            && tiers[tierIndex].contentRect.z > 0.5f && tiers[tierIndex].contentRect.w > 0.5f;
    }

    // 把贴图 texel 空间的矩形换算到机身局部空间（1 单位 = 1/PPU 像素）。
    private void RectLocal(Vector4 rect, out Vector2 center, out Vector2 size)
    {
        center = Vector2.zero;
        size = new Vector2(4f, 4f);
        if (body == null || body.sprite == null) return;
        Vector2 pivot = body.sprite.pivot;                 // 像素
        float ppu = Mathf.Max(1f, body.sprite.pixelsPerUnit);
        float x0 = (rect.x - pivot.x) / ppu;
        float y0 = (rect.y - pivot.y) / ppu;
        float x1 = (rect.x + rect.z - pivot.x) / ppu;
        float y1 = (rect.y + rect.w - pivot.y) / ppu;
        center = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
        size = new Vector2(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
    }

    private void RectWorld(Vector4 rect, out Vector3 center, out Vector2 size)
    {
        RectLocal(rect, out Vector2 localCenter, out Vector2 localSize);
        center = transform.position + new Vector3(localCenter.x * baseLossyScale.x,
            localCenter.y * baseLossyScale.y, 0f);
        size = new Vector2(Mathf.Abs(localSize.x * baseLossyScale.x),
            Mathf.Abs(localSize.y * baseLossyScale.y));
    }

    // 工作区矩形，在机身 sprite 的局部空间里（1 单位 = 1/PPU 像素）。
    private void WorkRectLocal(out Vector2 center, out Vector2 size)
    {
        center = Vector2.zero;
        size = new Vector2(4f, 4f);
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Length) return;
        RectLocal(tiers[tierIndex].workRect, out center, out size);
    }

    // 工作区矩形在世界空间里的中心与尺寸（用于摆迷你票）。
    private void WorkRectWorld(out Vector3 center, out Vector2 size)
    {
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Length)
        {
            center = transform.position;
            size = new Vector2(4f, 4f);
            return;
        }
        RectWorld(tiers[tierIndex].workRect, out center, out size);
    }

    // 可见内容包围盒在世界空间里的中心与尺寸（用于摆进度环 / 命中判定）。
    private void ContentRectWorld(out Vector3 center, out Vector2 size)
    {
        if (!HasContentRect())
        {
            center = transform.position + Vector3.Scale(body.sprite.bounds.center, baseLossyScale);
            Vector3 fallback = Vector3.Scale(body.sprite.bounds.size, baseLossyScale);
            size = new Vector2(fallback.x, fallback.y);
            return;
        }
        RectWorld(tiers[tierIndex].contentRect, out center, out size);
    }

    // ---- 更新 --------------------------------------------------------------

    private void Update()
    {
        if (entryTimer >= 0f)
        {
            entryTimer += Time.deltaTime;
            float k = Mathf.Clamp01(entryTimer / entryDuration);
            transform.position = Vector3.Lerp(entryFrom, entryTo, k * k);   // 加速下落
            if (k < 1f) return;
            entryTimer = -1f;
            transform.position = entryTo;
            // 机身刚停稳：把槽位重算一遍，并让已经进槽的票立刻跟过去。
            // 入场途中收下的票（正常玩法够不到，但读档/调试会）是按「还在半空」的 transform 算的槽位。
            RelayoutSlots();
            if (jelly != null) jelly.Pulse(1.6f, 0.3f);
            return;
        }

        if (!unlocked || game == null) return;

        if (slots.Count == 0)
        {
            if (effect != null) effect.SetRunning(false);
            if (ring != null) ring.Hide();
            return;
        }
        if (effect != null) effect.SetRunning(true);

        // 串行计时：永远只有**队头**（最早进机、且已入槽完成）那张在计时 —— 它完成并
        // 结算（转入出槽动画）后，下一张才从 0 开始起算。等待中的票静止排队：不计时、
        // 不抖动，进度环也只反映队头进度。容量因此是「队列长度」，不是并行道数。
        int activeIndex = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].phase == 1) { activeIndex = i; break; }
        }

        float best = 0f;
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            Slot slot = slots[i];
            if (slot.node == null || slot.ticket == null)
            {
                slots.RemoveAt(i);
                continue;
            }

            if (slot.phase == 0)
            {
                slot.stateTime += Time.deltaTime;
                float k = Mathf.Clamp01(slot.stateTime / feedDuration);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                slot.node.position = Vector3.Lerp(slot.enterFrom, slot.targetPos, ease);
                slot.node.localScale = Vector3.Lerp(slot.fromScale, slot.targetScale, ease);
                if (k >= 1f)
                {
                    slot.phase = 1;
                    slot.stateTime = 0f;
                    slot.node.position = slot.targetPos;
                    slot.node.localScale = slot.targetScale;
                }
                continue;
            }

            if (slot.phase == 1)
            {
                if (i != activeIndex) continue;   // 排队中：静止，等队头完成再起算
                slot.elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(slot.elapsed / secondsPerTicket);
                best = p;
                // 处理中的迷你票轻微抖动，配合刮擦头的节奏。
                float wobble = Mathf.Sin(slot.elapsed * 11f + slot.targetPos.x) * 0.05f;
                slot.node.localScale = slot.targetScale * (1f + wobble);
                if (p >= 1f) FinishSlot(i, true);
                continue;
            }

            // phase 2：出槽
            slot.stateTime += Time.deltaTime;
            float u = Mathf.Clamp01(slot.stateTime / payoutDuration);
            slot.node.position = slot.targetPos + Vector3.up * (u * payoutRise);
            slot.node.localScale = slot.targetScale * Mathf.Lerp(1f, 0.05f, u);
            if (u >= 1f)
            {
                Destroy(slot.node.gameObject);
                slots.RemoveAt(i);
                RelayoutSlots();
            }
        }

        if (ring != null)
        {
            ring.Show();
            ring.SetProgress(best);
        }
    }

    // 结算奖金 + 播出槽动画（phase 2），槽位在动画结束后才真正移除。
    private void FinishSlot(int index, bool pay)
    {
        if (index < 0 || index >= slots.Count) return;
        Slot slot = slots[index];

        if (!pay)
        {
            if (slot.node != null) Destroy(slot.node.gameObject);
            slots.RemoveAt(index);
            RelayoutSlots();
            return;
        }

        if (slot.ticket != null && game != null) game.AwardMachineTicket(slot.ticket.Kind, slot.ticket.Prize);
        if (jelly != null) jelly.Pulse(completePulseStrength, completePulseDuration);
        if (effect != null) effect.Burst();

        if (slot.node != null)
        {
            slot.phase = 2;
            slot.stateTime = 0f;
        }
        else
        {
            slots.RemoveAt(index);
            RelayoutSlots();
        }
    }

    // 立即结算并移除（容量被调小时用；不能走 Phase 2 动画，否则 while 循环退不出去）。
    private void PayAndRemove(int index)
    {
        if (index < 0 || index >= slots.Count) return;
        Slot slot = slots[index];
        if (slot.ticket != null && game != null) game.AwardMachineTicket(slot.ticket.Kind, slot.ticket.Prize);
        if (slot.node != null) Destroy(slot.node.gameObject);
        slots.RemoveAt(index);
        RelayoutSlots();
        if (jelly != null) jelly.Pulse(completePulseStrength, completePulseDuration);
        if (effect != null) effect.Burst();
    }

    // ---- 槽位布局 ----------------------------------------------------------

    private void RelayoutSlots()
    {
        int count = slots.Count;
        if (count == 0) return;
        WorkRectWorld(out Vector3 center, out Vector2 size);

        int cols = ColumnsFor(count);
        int rows = Mathf.CeilToInt((float)count / cols);
        float cellW = size.x / cols;
        float cellH = size.y / rows;

        for (int i = 0; i < count; i++)
        {
            Slot slot = slots[i];
            int row = i / cols;
            int col = i % cols;
            int itemsInRow = Mathf.Min(cols, count - row * cols);

            slot.targetPos = new Vector3(
                center.x + (col - (itemsInRow - 1) * 0.5f) * cellW,
                center.y + size.y * 0.5f - cellH * 0.5f - row * cellH,
                center.z - 0.1f);

            float tw = 1.28f * Mathf.Abs(slot.ticketBaseScale.x);
            float th = 0.8f * Mathf.Abs(slot.ticketBaseScale.y);
            float k = Mathf.Min(cellW * slotFill / Mathf.Max(0.001f, tw),
                                cellH * slotFill / Mathf.Max(0.001f, th));
            slot.targetScale = slot.ticketBaseScale * Mathf.Max(k, 0.0005f);

            // 已经进槽/出槽的票要立刻跟到新位置：机身落点或等级改变后，
            // 它们停的还是按旧 transform 算出来的点（phase 1 不改 position，不跟就会留一个鬼影）。
            if (slot.node != null && slot.phase != 0) slot.node.position = slot.targetPos;
        }
    }

    private int ColumnsFor(int count)
    {
        int cap = 3;
        if (tiers != null && tierIndex >= 0 && tierIndex < tiers.Length)
            cap = Mathf.Max(1, tiers[tierIndex].maxColumns);
        if (count <= 2) return Mathf.Clamp(count, 1, cap);
        if (count <= 4) return Mathf.Min(2, cap);
        return Mathf.Min(3, cap);
    }
}
