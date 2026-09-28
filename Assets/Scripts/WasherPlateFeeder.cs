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
    [SerializeField] private Vector2 interiorOffset = new Vector2(0f, -1f);
    [SerializeField, Range(0.2f, 1f)] private float interiorWidthRatio = 0.85f;
    [SerializeField, Range(0.2f, 1f)] private float interiorHeightRatio = 0.7f;

    [Header("Jelly Pulse")]
    [SerializeField] private float pulseStrength = 1f;
    [Tooltip("快放版里盘子太密，脉冲强度要压低，否则会叠成持续膨胀。")]
    [SerializeField] private float pulseStrengthFast = 0.6f;
    [Tooltip("盘子越密，单次脉冲强度越低（防止共振累积）。该值是密到极限时的强度系数。")]
    [SerializeField, Range(0.05f, 1f)] private float fastStrengthFloor = 0.35f;
    [SerializeField, Min(0.02f)] private float minPulseDuration = 0.06f;
    [SerializeField, Min(0.05f)] private float maxPulseDuration = 0.22f;

    private readonly List<Transform> plates = new List<Transform>();
    private Coroutine feedRoutine;
    private int slotCursor;
    private Vector3 baseLossyScale = Vector3.one;
    private bool metricsReady;

    public bool IsFeeding => feedRoutine != null;
    public int PlateCount => plates.Count;

    private void Awake()
    {
        if (washerRenderer == null) washerRenderer = GetComponent<SpriteRenderer>();
        EnsureMetrics();
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

    private void OnDestroy() => ClearPlates();

    // 洗盘机的静止尺寸只取一次，且必须避开果冻缩放（否则槽位会跟着抖）。
    // 惰性取样而不是只在 Awake 里取：Awake 的时机在编辑器里不可靠，
    // 实测过拿到 (1,1,1) 导致机内尺寸算小 20 倍、列距被收敛到下限。
    private void EnsureMetrics()
    {
        if (metricsReady || washerRenderer == null) return;
        baseLossyScale = washerRenderer.transform.lossyScale;
        metricsReady = true;
    }

    // 改了洗盘机大小后调一次，重新取样（运行时可在 Inspector 之外手动触发）。
    public void RefreshMetrics()
    {
        metricsReady = false;
        EnsureMetrics();
    }

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
            plates.Add(plate);
        }
        slotCursor = plates.Count;
    }

    private void RemovePlates(int count)
    {
        for (int i = 0; i < count && plates.Count > 0; i++)
        {
            int last = plates.Count - 1;
            if (plates[last] != null) Destroy(plates[last].gameObject);
            plates.RemoveAt(last);
        }
        slotCursor = plates.Count;
    }

    public void ClearPlates()
    {
        StopFeed();
        for (int i = plates.Count - 1; i >= 0; i--)
        {
            if (plates[i] != null) Destroy(plates[i].gameObject);
        }
        plates.Clear();
        slotCursor = 0;
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
        StartCoroutine(FlyIn(plate, target, fly, pulseDuration, strength, delay));
    }

    private IEnumerator FlyIn(Transform plate, Vector3 target, float duration,
        float pulseDuration, float strength, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        Vector3 start = plate.position;
        float time = 0f;
        while (time < duration)
        {
            if (plate == null) yield break;   // 被 ClearPlates / RemovePlates 中途销毁
            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / duration);
            float ease = 1f - Mathf.Pow(1f - k, 3f); // easeOutCubic：出发快、到位稳
            float x = Mathf.Lerp(start.x, target.x, ease);
            float y = Mathf.Lerp(start.y, target.y, ease) + Mathf.Sin(k * Mathf.PI) * arcHeight;
            plate.position = new Vector3(x, y, target.z);
            yield return null;
        }

        plate.position = target;
        // 果冻与「盘子到位」精确同步：在这一帧打脉冲。
        if (washerJelly != null) washerJelly.Pulse(strength, pulseDuration);
    }

    private Transform CreatePlate()
    {
        var go = new GameObject("Plate");
        go.transform.SetParent(plateContainer, false);
        go.transform.localScale = new Vector3(plateScale, plateScale, 1f);
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
            ? Mathf.Min(columnSpacing, Mathf.Max(0.1f, (size.x - plateSize.x) / (cols - 1)))
            : 0f;
        float step = rows > 1
            ? Mathf.Min(stackOffset, Mathf.Max(0.05f, (size.y - plateSize.y) / (rows - 1)))
            : 0f;

        float stackHeight = plateSize.y + (rows - 1) * step;
        float x = (col - (cols - 1) * 0.5f) * spacing;
        float y = -stackHeight * 0.5f + plateSize.y * 0.5f + row * step;
        return new Vector3(center.x + x, center.y + y, center.z - 0.1f);
    }

    private Vector2 plateSize =>
        new Vector2(plateSprite.bounds.size.x * plateScale, plateSprite.bounds.size.y * plateScale);

    private Vector3 InteriorSize()
    {
        EnsureMetrics();
        Vector3 local = washerRenderer.sprite.bounds.size;
        Vector3 world = Vector3.Scale(local, baseLossyScale);
        return new Vector3(world.x * interiorWidthRatio, world.y * interiorHeightRatio, world.z);
    }

    private Vector3 InteriorCenter()
    {
        EnsureMetrics();
        Vector3 local = washerRenderer.sprite.bounds.center;
        Vector3 offset = Vector3.Scale(local, baseLossyScale);
        Vector3 center = washerRenderer.transform.position + offset;
        center.x += interiorOffset.x;
        center.y += interiorOffset.y;
        return center;
    }

    private float SpawnX()
    {
        EnsureMetrics();
        Camera cam = Camera.main;
        if (cam == null) return InteriorCenter().x + 40f;
        float halfWidth = cam.orthographicSize * cam.aspect;
        return cam.transform.position.x + halfWidth + spawnMargin;
    }
}
