using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 洗盘机的装饰盘子：入场动画 + 机内叠放 + 到位时的果冻脉冲。
//
// 三种入场（由 AutomaticDishWasher 决定用哪种）：
//   Feed(count, fast: false) —— 完整版，买下洗盘机时播一次，每盘间隔和飞行时间都放慢。
//   Feed(count, fast: true)  —— 快放版，每轮结算后播，总时长锁死 fastFeedDuration。
//   AddPlates(added)         —— 容量升级后只补飞新增的那几个，机内已有的不动。
//   PlaceInstantly(count)    —— 读档时直接摆好，不播动画。
//
// 槽位坐标按洗盘机的实际尺寸现算（并自动收敛列距/层距），
// 所以以后改洗盘机大小或改容量都不用再调参数。
public class WasherPlateFeeder : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpriteRenderer washerRenderer;
    [SerializeField] private HoverJelly washerJelly;
    [SerializeField] private Transform plateContainer;
    [SerializeField] private WasherWaterEffect window;

    [Header("Plate")]
    [SerializeField] private Sprite plateSprite;
    [SerializeField, Min(0.001f)] private float plateScale = 20f;
    [SerializeField] private int plateSortingOffset = 1;

    [Header("Entry - Full (on unlock)")]
    [Tooltip("单个盘子从屏幕外飞到槽位的时长。")]
    [SerializeField] private float flyDuration = 0.35f;
    [Tooltip("完整版里相邻两个盘子出发的间隔。")]
    [SerializeField] private float fullFeedInterval = 0.18f;

    [Header("Entry - Fast (every cycle)")]
    [Tooltip("快放版的总时长锁死值：间隔 = (总时长 - 飞行时长) / (盘子数 - 1)。")]
    [SerializeField] private float fastFeedDuration = 0.8f;
    [Tooltip("Reduce Motion 打开时的快放总时长。")]
    [SerializeField] private float reducedFeedDuration = 0.4f;
    [SerializeField, Min(0.01f)] private float minFlyDuration = 0.08f;
    [Tooltip("出生点在屏幕右边界之外多远（世界单位）。")]
    [SerializeField] private float spawnMargin = 4f;
    [Tooltip("飞行途中的上抛弧度（世界单位）。")]
    [SerializeField] private float arcHeight = 1.5f;

    [Header("Stacking")]
    [SerializeField, Min(1)] private int columns = 3;
    [Tooltip("列间距上限，实际值会自动收敛到不超出机内宽度。")]
    [SerializeField] private float columnSpacing = 7.6f;
    [Tooltip("每叠一层的垂直错位上限，实际值会自动收敛到不超出机内高度。")]
    [SerializeField] private float stackOffset = 0.8f;

    [Header("Jelly Pulse")]
    [SerializeField] private float pulseStrength = 1f;
    [Tooltip("快放版里盘子太密，脉冲强度要压低，否则会叠成持续膨胀。")]
    [SerializeField] private float pulseStrengthFast = 0.6f;
    [Tooltip("盘子越密，单次脉冲强度越低（防止共振累积）。该值是密到极限时的强度系数。")]
    [SerializeField, Range(0.05f, 1f)] private float fastStrengthFloor = 0.35f;
    [SerializeField, Min(0.02f)] private float minPulseDuration = 0.06f;
    [SerializeField, Min(0.05f)] private float maxPulseDuration = 0.22f;

    // 必须 [SerializeField]（且不能 readonly）：Play 中域重载（改脚本触发自动刷新）会
    // 重序列化场景状态，非序列化字段全部清空 —— 旧版 readonly 追踪表丢失后，盘子本体
    // 还活着但无人刷新，变成「被 mask 藏住、机身拖回来才显形」的静止孤儿层
    // （2026-10-04 实锤：9 在册 + 9 孤儿，孤儿批钉在重载那一刻的机身位置）。
    [SerializeField] private List<Transform> plates = new List<Transform>();
    // 正在飞入的盘子。机身被拖动时 RefreshSlotPositions 必须跳过它们 ——
    // FlyIn 协程每帧在写 position，两边都写就是两个源抢同一个属性。
    // 不序列化（HashSet 不可序列化）：域重载后清空是可接受的 —— 协程也死了，
    // 活着的盘子会由 RefreshSlotPositions 直接吸附到槽位。
    private readonly HashSet<Transform> inFlight = new HashSet<Transform>();
    private Coroutine feedRoutine;
    private int slotCursor;
    private SpriteMask windowMask;
    private Sprite maskSprite;
    private Vector3 lastWindowScale;

    public bool IsFeeding => feedRoutine != null;
    public int PlateCount => plates.Count;

    private void Awake()
    {
        if (washerRenderer == null) washerRenderer = GetComponent<SpriteRenderer>();
        if (window == null) window = GetComponentInChildren<WasherWaterEffect>(true);
        BuildWindowMask();
        lastWindowScale = washerRenderer != null ? washerRenderer.transform.lossyScale : Vector3.one;
        if (plateContainer == null)
        {
            // 盘子不能挂在洗盘机下面：洗盘机 localScale=24，会把盘子一起放大；
            // 而且果冻缩放会让它抖动。独立容器保持 scale=1、位置在世界原点。
            var container = new GameObject("WasherPlates");
            container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            container.transform.localScale = Vector3.one;
            plateContainer = container.transform;
        }
    }

    private void OnDestroy()
    {
        ClearPlates();
        if (maskSprite != null) Destroy(maskSprite);
    }

    // 自愈兜底：容器里「活着但不在追踪表」的盘子一律收编。
    // 域重载（Awake 不会重跑，OnEnable 会重跑）或任何未来泄漏路径留下的孤儿，
    // 在这一帧重新进入槽位布局并按当前机身位置重排 —— 而不是变成隐形静止层。
    // 编辑态必须跳过：收编属于运行时状态逻辑，烘进场景就是第二个 2026-10-01。
    private void OnEnable()
    {
        if (!Application.isPlaying || plateContainer == null) return;
        bool adopted = false;
        for (int i = plateContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = plateContainer.GetChild(i);
            if (child == null || plates.Contains(child)) continue;
            plates.Add(child);
            adopted = true;
        }
        if (adopted) RefreshSlotPositions();
    }

    private void LateUpdate()
    {
        if (washerRenderer == null || washerRenderer.transform.lossyScale == lastWindowScale) return;
        lastWindowScale = washerRenderer.transform.lossyScale;
        RefreshSlotPositions();
    }

    private Bounds WindowLocalBounds => window != null ? window.WindowLocalBounds
        : new Bounds(new Vector3(-0.005f, 0.005f, 0f), new Vector3(0.9f, 0.34f, 0f));

    private void BuildWindowMask()
    {
        if (washerRenderer == null) return;
        var go = new GameObject("WasherWindowMask");
        go.transform.SetParent(washerRenderer.transform, false);
        Bounds bounds = WindowLocalBounds;
        go.transform.localPosition = bounds.center;
        go.transform.localScale = new Vector3(bounds.size.x, bounds.size.y, 1f);
        maskSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 2f, 2f),
            new Vector2(0.5f, 0.5f), 2f, 0, SpriteMeshType.FullRect);
        maskSprite.hideFlags = HideFlags.DontSave;
        windowMask = go.AddComponent<SpriteMask>();
        windowMask.sprite = maskSprite;
        windowMask.isCustomRangeActive = true;
        windowMask.frontSortingLayerID = windowMask.backSortingLayerID = washerRenderer.sortingLayerID;
        // 前后顺序必须严格包住盘子层；与 front 同层会在 URP 2D 中完全隐藏盘子。
        int plateOrder = washerRenderer.sortingOrder + plateSortingOffset;
        windowMask.frontSortingOrder = plateOrder + 1;
        windowMask.backSortingOrder = plateOrder - 1;
    }

    // 编辑器装配后可显式刷新；窗几何跟随机身真实缩放，不缓存旧尺寸。
    public void RefreshMetrics() => RefreshSlotPositions();

    // ---- 对外接口 ----------------------------------------------------------

    public void Feed(int count, bool fast, Action onComplete)
    {
        StopFeed();
        slotCursor = plates.Count;
        if (count <= 0 || plateSprite == null || washerRenderer == null)
        {
            if (onComplete != null) onComplete();
            return;
        }
        feedRoutine = StartCoroutine(FeedRoutine(count, fast, onComplete));
    }

    // 容量升级：只补飞新增的那几个，机内已有的不动，不打断当前洗涤。
    // 传负数表示减容量（Debug 面板用），只从末尾移除，也不重排已有的。
    public void AddPlates(int added)
    {
        if (added == 0 || plateSprite == null || washerRenderer == null) return;
        if (added < 0)
        {
            RemovePlates(-added);
            return;
        }
        slotCursor = plates.Count;
        float duration = HoverJellySettings.ReducedMotion ? flyDuration * 0.5f : flyDuration;
        float pulse = Mathf.Clamp(fullFeedInterval * 2.2f, minPulseDuration, maxPulseDuration);
        for (int i = 0; i < added; i++)
        {
            SpawnFlyer(slotCursor + i, slotCursor + added, duration, pulse, pulseStrength, i * fullFeedInterval);
        }
        slotCursor += added;
    }

    // 读档：机内直接摆好，不播动画。
    public void PlaceInstantly(int count)
    {
        ClearPlates();
        if (plateSprite == null || washerRenderer == null) return;
        for (int i = 0; i < count; i++)
        {
            Transform plate = CreatePlate();
            plate.position = SlotPosition(i, Mathf.Max(1, count));
            plate.GetComponent<SpriteRenderer>().maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            plates.Add(plate);
        }
        slotCursor = plates.Count;
    }

    private void RemovePlates(int count)
    {
        for (int i = 0; i < count && plates.Count > 0; i++)
        {
            int last = plates.Count - 1;
            if (plates[last] != null)
            {
                inFlight.Remove(plates[last]);
                Destroy(plates[last].gameObject);
            }
            plates.RemoveAt(last);
        }
        slotCursor = plates.Count;
    }

    public void ClearPlates()
    {
        StopFeed();
        inFlight.Clear();
        for (int i = plates.Count - 1; i >= 0; i--)
        {
            if (plates[i] != null) Destroy(plates[i].gameObject);
        }
        plates.Clear();
        slotCursor = 0;
    }

    // 洗完那一刻：把机内**当前这一批**交给缩小退场，随后飞入的新一批与它共存 0.3s。
    //
    // 刻意**不复用 ClearPlates()**：那条路还被另外三个调用点共用 ——
    // 场景卸载（本组件的 OnDestroy）、读档（PlaceInstantly，注释明写「不播动画」）、
    // 锁定重置（AutomaticDishWasher 的 Locked 分支）。其中卸载时对象正在销毁，
    // 动画根本跑不完；读档则是明确要求不播。所以退场动画只能接在「洗完」这一个点上。
    public void ShrinkOutPlates()
    {
        StopFeed();
        inFlight.Clear();
        for (int i = 0; i < plates.Count; i++)
        {
            Transform plate = plates[i];
            if (plate == null) continue;
            ShrinkOut shrink = plate.GetComponent<ShrinkOut>();
            if (shrink == null) shrink = plate.gameObject.AddComponent<ShrinkOut>();
            // 脱离容器再看：它已经不属于槽位布局，也不该被随后的 ClearPlates /
            // RefreshSlotPositions 牵连。保留世界坐标，所以观感是原地缩小而不是瞬移。
            plate.SetParent(null, true);
            shrink.Play();
        }
        plates.Clear();
        slotCursor = 0;
    }

    // 机身位置被拖动/滑行改动时每帧调用：机内已就位的盘子必须重摆到新槽位。
    // 飞行中的盘子不在这里改 —— FlyIn 每帧自己重算目标（见 FlyIn 里的注释）。
    public void RefreshSlotPositions()
    {
        if (plates.Count == 0) return;
        int total = Mathf.Max(1, plates.Count);
        for (int i = 0; i < plates.Count; i++)
        {
            Transform plate = plates[i];
            if (plate == null || inFlight.Contains(plate)) continue;
            plate.localScale = new Vector3(VisualPlateScale, VisualPlateScale, 1f);
            plate.position = SlotPosition(i, total);
        }
    }

    private void StopFeed()
    {
        if (feedRoutine != null)
        {
            StopCoroutine(feedRoutine);
            feedRoutine = null;
        }
    }

    // ---- 入场流程 ----------------------------------------------------------

    private IEnumerator FeedRoutine(int count, bool fast, Action onComplete)
    {
        bool reduced = HoverJellySettings.ReducedMotion;
        float fly;
        float interval;

        if (fast)
        {
            float total = reduced ? reducedFeedDuration : fastFeedDuration;
            fly = Mathf.Min(flyDuration, Mathf.Max(minFlyDuration, total / count * 3f));
            interval = count > 1 ? Mathf.Max(0f, (total - fly) / (count - 1)) : 0f;
        }
        else
        {
            float scale = reduced ? 0.5f : 1f;
            fly = flyDuration * scale;
            interval = fullFeedInterval * scale;
        }

        // 脉冲时长跟着间隔走：间隔密就压短，避免 30 个盘子把弹簧叠成持续膨胀。
        float pulseDuration = Mathf.Clamp(interval * 2.2f, minPulseDuration, maxPulseDuration);
        // 强度还要按密度衰减：间隔 25ms 时接近弹簧共振周期，满强度会共振累积，
        // 把洗盘机越弹越大（实测炸到过 1e34）。隔得越密，单次脉冲越轻，表现成一串轻颤。
        float density = Mathf.Clamp01(interval / 0.12f);
        float strength = fast
            ? pulseStrengthFast * Mathf.Lerp(fastStrengthFloor, 1f, density)
            : pulseStrength;

        for (int i = 0; i < count; i++)
        {
            SpawnFlyer(slotCursor + i, slotCursor + count, fly, pulseDuration, strength, 0f);
            if (i < count - 1) yield return new WaitForSeconds(interval);
        }
        yield return new WaitForSeconds(fly);

        slotCursor += count;
        feedRoutine = null;
        if (onComplete != null) onComplete();
    }

    private void SpawnFlyer(int index, int total, float fly, float pulseDuration, float strength, float delay)
    {
        Transform plate = CreatePlate();
        Vector3 target = SlotPosition(index, Mathf.Max(1, total));
        plate.position = new Vector3(SpawnX(), target.y, target.z);
        plates.Add(plate);
        StartCoroutine(FlyIn(plate, index, Mathf.Max(1, total), fly, pulseDuration, strength, delay));
    }

    private IEnumerator FlyIn(Transform plate, int index, int total, float duration,
        float pulseDuration, float strength, float delay)
    {
        inFlight.Add(plate);
        if (delay > 0f) yield return new WaitForSeconds(delay);

        Vector3 start = plate.position;
        float time = 0f;
        while (time < duration)
        {
            if (plate == null)   // 被 ClearPlates / RemovePlates 中途销毁
            {
                inFlight.Remove(plate);
                yield break;
            }
            // 已被 ShrinkOutPlates 接管退场（SetParent(null)）：立刻让位。
            // 缩小那 0.3s 里 scale 归 ShrinkOut 独占，这里再写 position/scale/mask
            // 就是同帧两个源抢同一属性（铁律）。
            if (plate.parent == null)
            {
                inFlight.Remove(plate);
                yield break;
            }
            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / duration);
            float ease = 1f - Mathf.Pow(1f - k, 3f); // easeOutCubic：出发快、到位稳
            // 目标**每帧重算**，而不是在出发时钉死：机身被拖动时槽位会整片移动，
            // 抱住旧 target 的盘子会飞到一个已经不存在的位置上。
            Vector3 target = SlotPosition(index, total);
            float x = Mathf.Lerp(start.x, target.x, ease);
            float y = Mathf.Lerp(start.y, target.y, ease) + Mathf.Sin(k * Mathf.PI) * arcHeight;
            plate.position = new Vector3(x, y, target.z);
            plate.localScale = new Vector3(VisualPlateScale, VisualPlateScale, 1f);
            SpriteRenderer plateRenderer = plate.GetComponent<SpriteRenderer>();
            Bounds outer = washerRenderer.bounds;
            Bounds flying = plateRenderer.bounds;
            bool touchesMachine = flying.max.x >= outer.min.x && flying.min.x <= outer.max.x
                && flying.max.y >= outer.min.y && flying.min.y <= outer.max.y;
            plateRenderer.maskInteraction = touchesMachine ? SpriteMaskInteraction.VisibleInsideMask : SpriteMaskInteraction.None;
            yield return null;
        }

        inFlight.Remove(plate);
        plate.position = SlotPosition(index, total);
        plate.GetComponent<SpriteRenderer>().maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        // 果冻与「盘子到位」精确同步：在这一帧打脉冲。
        if (washerJelly != null) washerJelly.Pulse(strength, pulseDuration);
    }

    private Transform CreatePlate()
    {
        var go = new GameObject("Plate");
        go.transform.SetParent(plateContainer, false);
        go.transform.localScale = new Vector3(VisualPlateScale, VisualPlateScale, 1f);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = plateSprite;
        renderer.sortingLayerID = washerRenderer.sortingLayerID;
        renderer.sortingOrder = washerRenderer.sortingOrder + plateSortingOffset;
        return go.transform;
    }

    // ---- 槽位计算 ----------------------------------------------------------

    private Vector3 SlotPosition(int index, int total)
    {
        Vector3 size = InteriorSize();
        Vector3 center = InteriorCenter();

        int cols = Mathf.Max(1, columns);
        int rows = Mathf.CeilToInt((float)Mathf.Max(1, total) / cols);
        int col = index % cols;
        int row = index / cols;

        // 列距 / 层距都自动收敛，保证盘子再多也不溢出机内。
        float spacing = cols > 1
            ? Mathf.Min(columnSpacing, Mathf.Max(0f, (size.x - plateSize.x) / (cols - 1)))
            : 0f;
        float step = rows > 1
            ? Mathf.Min(stackOffset, Mathf.Max(0f, (size.y - plateSize.y) / (rows - 1)))
            : 0f;

        float stackHeight = plateSize.y + (rows - 1) * step;
        float x = (col - (cols - 1) * 0.5f) * spacing;
        float y = -stackHeight * 0.5f + plateSize.y * 0.5f + row * step;
        return new Vector3(center.x + x, center.y + y, center.z - 0.1f);
    }

    private Vector2 plateSize =>
        new Vector2(plateSprite.bounds.size.x * VisualPlateScale, plateSprite.bounds.size.y * VisualPlateScale);

    private float VisualPlateScale
    {
        get
        {
            if (plateSprite == null) return plateScale;
            Vector3 size = InteriorSize();
            return Mathf.Min(plateScale, size.y / Mathf.Max(0.001f, plateSprite.bounds.size.y),
                size.x / (Mathf.Max(1, columns) * Mathf.Max(0.001f, plateSprite.bounds.size.x)));
        }
    }

    private Vector3 InteriorSize()
    {
        Vector3 local = WindowLocalBounds.size;
        // 在实际窗内保留一圈机身 texel，完整圆盘和叠层都不能盖到外壳。
        local.x = Mathf.Max(0.001f, local.x - 0.02f);
        local.y = Mathf.Max(0.001f, local.y - 0.02f);
        return Vector3.Scale(local, washerRenderer.transform.lossyScale);
    }

    private Vector3 InteriorCenter()
    {
        return washerRenderer.transform.TransformPoint(WindowLocalBounds.center);
    }

    private float SpawnX()
    {
        Camera cam = Camera.main;
        if (cam == null) return InteriorCenter().x + 40f;
        float halfWidth = cam.orthographicSize * cam.aspect;
        return cam.transform.position.x + halfWidth + spawnMargin;
    }
}
