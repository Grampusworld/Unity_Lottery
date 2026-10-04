using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 全屏淡入淡出遮罩。挂在 DontDestroyOnLoad 的常驻对象上，所以 New Game 的
// SceneManager.LoadScene 不会把它一起销毁 —— 淡出途中重载场景，遮罩仍然活着。
//
// 存在的原因：主菜单期间 MainMenuScreen 会把 Time.timeScale 设成 0
// （GameplayBlocked = true）。用 WaitForSeconds 的协程在timeScale = 0 时会永久冻结，
// 所以这里全部走 unscaledTime —— 那是唯一不受暂停影响的时钟。
//
// 打断语义：转场期间 MainMenuScreen.GameplayBlocked 保持 true，所有输入被吞掉。
// 不可打断是刻意的 ——「可打断」意味着要维护反向状态机和中间态，
// 而1 秒的转场里玩家按不到任何东西。
[DefaultExecutionOrder(-200)]
public class ScreenFader : MonoBehaviour
{
    private static ScreenFader instance;

    // 比 MainMenuScreen（-100）更早：Awake 里要先把遮罩铺满全黑，
    // 否则菜单第一帧会在半透明遮罩后面闪一下。
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SetAlpha(1f);
    }

    [SerializeField] private Image overlay;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;
    [SerializeField, Min(0f)] private float holdDuration = 0.15f;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.5f;
    // REDUCED MOTION 下用的短时长。保留「有个过渡」的不闪烁感，但不是完整动画。
    [SerializeField, Min(0f)] private float reducedFadeOut = 0.12f;
    [SerializeField, Min(0f)] private float reducedHold = 0.05f;
    [SerializeField, Min(0f)] private float reducedFadeIn = 0.12f;

    private Coroutine routine;

    /// <summary>当前是否正在转场。转场期间 GameplayBlocked 应保持 true。</summary>
    public static bool Busy { get; private set; }

    /// <summary>
    /// 转场完全结束（含淡入）时广播一次。New Game 重载场景后，
    /// 新建的 MainMenuScreen 靠它把GameplayBlocked 放开 —— 否则会永久卡在暂停态。
    /// </summary>
    public static event Action Completed;

    // 域重载会清掉 static，但对象还在（和 LotterySfx 同一个坑）。
    private void OnEnable()
    {
        if (instance == null) instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            Busy = false;
        }
    }

    /// <summary>转场结束后把遮罩停用。必须在协程外也安全（幂等）。</summary>
    public static void Clear()
    {
        if (instance == null) return;
        instance.SetAlpha(0f);
        instance.Stop();
    }

    private void Stop()
    {
        if (routine == null) return;
        StopCoroutine(routine);
        routine = null;
        Busy = false;
    }

    private void SetAlpha(float alpha)
    {
        if (overlay == null) return;
        var color = overlay.color;
        color.a = alpha;
        overlay.color = color;
    }

    /// <summary>
    /// 在黑屏停留阶段执行 <paramref name="switchScene"/>，然后淡入。
    /// 场景重载放在 hold 里，新场景的实例会接着把 alpha 从 1 淡到 0。
    /// </summary>
    public static void Transition(Action switchScene)
    {
        if (instance == null) return;
        if (Busy) return;
        if (switchScene != null) instance.StartCoroutine(instance.Run(switchScene));
    }

    /// <summary>淡入到当前画面（程序启动时用）。</summary>
    public static void FadeInOnStart()
    {
        if (instance == null) return;
        if (Busy) return;
        instance.StartCoroutine(instance.RunFadeInOnly());
    }

    private IEnumerator Run(Action switchScene)
    {
        Busy = true;
        bool reduced = HoverJellySettings.ReducedMotion;
        float outDur = reduced ? reducedFadeOut : fadeOutDuration;
        float hold = reduced ? reducedHold : holdDuration;
        float inDur = reduced ? reducedFadeIn : fadeInDuration;

        routine = StartCoroutine(Fade(1f, outDur));
        yield return routine;
        routine = StartCoroutine(Hold(hold));
        yield return routine;

        // 切换点在全黑时刻。New Game 在这里 LoadScene，遮罩是常驻对象所以不受影响。
        if (switchScene != null) switchScene();

        routine = StartCoroutine(Fade(0f, inDur));
        yield return routine;
        routine = null;
        Busy = false;
        // 重载场景时订阅者（旧实例）已销毁，新实例在Awake 里订阅 —— 两者都会收到。
        Completed?.Invoke();
    }

    private IEnumerator RunFadeInOnly()
    {
        Busy = true;
        SetAlpha(1f);
        bool reduced = HoverJellySettings.ReducedMotion;
        routine = StartCoroutine(Fade(0f, reduced ? reducedFadeIn : 0.3f));
        yield return routine;
        routine = null;
        Busy = false;
    }

    private IEnumerator Fade(float target, float duration)
    {
        if (duration <= 0f) { SetAlpha(target); yield break; }
        float start = overlay != null ? overlay.color.a : 0f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetAlpha(target);
    }

    private IEnumerator Hold(float duration)
    {
        if (duration <= 0f) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}