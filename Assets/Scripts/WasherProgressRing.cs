using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 洗盘机的圆环形进度条：世界空间 Canvas + Image(Filled/Radial360)。
//
// 挂在洗盘机的**同级**物体上，而不是子物体：
//   ① 洗盘机 localScale=24，做子物体会把 Canvas 一起放大；
//   ② 果冻缩放会带着环一起抖，环应该保持稳定的尺寸。
// 填充量量化到 1/fillSteps，让像素风的边缘不至于出现一段半透明的碎边。
public class WasherProgressRing : MonoBehaviour
{
    [Header("Ring")]
    [SerializeField] private Image ringImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Color ringColor = new Color(0.36f, 0.85f, 1f, 1f);

    [Header("Progress")]
    [Tooltip("填充量量化步数：24 表示每 1/24 跳一格，避免出现半透明碎边。")]
    [SerializeField, Min(1)] private int fillSteps = 24;

    [Header("Fade")]
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.1f;

    private Coroutine fadeRoutine;
    private float targetAlpha;

    private void Awake()
    {
        if (ringImage != null)
        {
            ringImage.color = ringColor;
            ringImage.raycastTarget = false;
            ringImage.fillAmount = 0f;
        }
        ApplyAlpha(0f);
    }

    public void Show()
    {
        SetProgress(0f);
        FadeTo(1f, fadeInDuration);
    }

    public void Hide(bool immediate = false)
    {
        if (immediate)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = null;
            ApplyAlpha(0f);
            return;
        }
        FadeTo(0f, fadeOutDuration);
    }

    public void SetProgress(float t)
    {
        if (ringImage == null) return;
        float clamped = Mathf.Clamp01(t);
        float stepped = Mathf.Round(clamped * fillSteps) / fillSteps;
        ringImage.fillAmount = stepped;
    }

    private void FadeTo(float alpha, float duration)
    {
        targetAlpha = alpha;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(duration));
    }

    private IEnumerator FadeRoutine(float duration)
    {
        float from = canvasGroup != null ? canvasGroup.alpha : 1f;
        if (duration <= 0f)
        {
            ApplyAlpha(targetAlpha);
            yield break;
        }
        float time = 0f;
        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            ApplyAlpha(Mathf.Lerp(from, targetAlpha, Mathf.Clamp01(time / duration)));
            yield return null;
        }
        ApplyAlpha(targetAlpha);
    }

    private void ApplyAlpha(float alpha)
    {
        if (canvasGroup != null) canvasGroup.alpha = alpha;
        else if (ringImage != null)
        {
            Color color = ringColor;
            color.a = alpha;
            ringImage.color = color;
        }
    }
}
