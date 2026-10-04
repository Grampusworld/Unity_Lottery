using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pure presentation: money is awarded and saved by LotteryGame immediately.
// The HUD waits for the coin to arrive before counting up to the revealed balance.
[DisallowMultipleComponent]
public class CoinGainFeedback : MonoBehaviour
{
    [SerializeField] private Sprite[] coinFrames = new Sprite[3];
    [SerializeField, Min(0.1f)] private float flightDuration = 0.45f;
    [SerializeField, Min(0f)] private float holdDuration = 0.1f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.15f;
    [SerializeField, Min(0.1f)] private float countDuration = 0.7f;
    [SerializeField, Min(0f)] private float spinDegreesPerSecond = 540f;

    private readonly List<GameObject> activePopups = new List<GameObject>();
    private TMP_Text balanceText;
    private RectTransform canvasRect;
    private Camera worldCamera;
    private Camera uiCamera;
    private int authoritativeBalance;
    private int pendingGains;
    private float displayedBalance;
    private float countFrom;
    private int countTarget;
    private float countElapsed;
    private bool counting;

    public void Initialize(TMP_Text label, int startingBalance)
    {
        ClearPopups();
        balanceText = label;
        worldCamera = Camera.main;
        Canvas canvas = label != null ? label.GetComponentInParent<Canvas>() : null;
        if (canvas != null)
        {
            canvas = canvas.rootCanvas;
            canvasRect = canvas.transform as RectTransform;
            uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        authoritativeBalance = Mathf.Max(0, startingBalance);
        pendingGains = 0;
        SetImmediate(authoritativeBalance);
    }

    public bool ShowGain(int amount, Transform source)
    {
        if (amount <= 0 || source == null || worldCamera == null || canvasRect == null ||
            balanceText == null || coinFrames == null || coinFrames.Length == 0 || coinFrames[0] == null)
            return false;

        RectTransform popup = CreatePopup(amount, out Image coin, out CanvasGroup group);
        if (popup == null) return false;

        Vector3 sourceTop = source.position;
        SpriteRenderer sourceSprite = source.GetComponent<SpriteRenderer>();
        if (sourceSprite != null) sourceTop.y = sourceSprite.bounds.max.y;
        Vector3 sourceScreen = worldCamera.WorldToScreenPoint(sourceTop);
        sourceScreen.y += 22f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sourceScreen, uiCamera,
            out Vector2 start);

        RectTransform moneyRect = balanceText.transform as RectTransform;
        Vector2 moneyScreen = RectTransformUtility.WorldToScreenPoint(uiCamera,
            moneyRect.TransformPoint(moneyRect.rect.center));
        int lane = activePopups.Count % 5;
        // 停在余额栏右缘：金币在栏内，金额伸向桌面，计数中的余额文字不会被盖住。
        // 同一时间多笔收益再错开路径和终点。
        Vector2 endScreen = moneyScreen + new Vector2(360f + lane * 4f, 12f - lane * 27f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, endScreen, uiCamera,
            out Vector2 end);

        popup.anchoredPosition = start;
        activePopups.Add(popup.gameObject);
        pendingGains += amount;
        StartCoroutine(FlyCoin(popup, coin, group, start, end, amount, lane));
        return true;
    }

    // Called whenever the authoritative balance changes. Spending is shown immediately;
    // gains stay hidden until the matching coin reaches the HUD.
    public void SyncBalance(int value)
    {
        value = Mathf.Max(0, value);
        bool spent = value < authoritativeBalance;
        bool unanimatedGain = value > authoritativeBalance && pendingGains == 0;
        authoritativeBalance = value;
        int revealed = Mathf.Max(0, value - pendingGains);
        if (spent || unanimatedGain) SetImmediate(revealed);
        else if (revealed != countTarget) BeginCount(revealed);
    }

    private RectTransform CreatePopup(int amount, out Image coin, out CanvasGroup group)
    {
        GameObject root = new GameObject("Coin Gain " + MoneyFormat.Money(amount), typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvasRect, false);
        root.transform.SetAsLastSibling();
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(220f, 56f);
        group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        GameObject icon = new GameObject("Coin", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(rect, false);
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(24f, 0f);
        iconRect.sizeDelta = new Vector2(40f, 40f);
        coin = icon.GetComponent<Image>();
        coin.sprite = coinFrames[0];
        coin.preserveAspect = true;
        coin.raycastTarget = false;

        GameObject number = new GameObject("Amount", typeof(RectTransform), typeof(TextMeshProUGUI));
        number.transform.SetParent(rect, false);
        RectTransform numberRect = (RectTransform)number.transform;
        numberRect.anchorMin = numberRect.anchorMax = new Vector2(0f, 0.5f);
        numberRect.pivot = new Vector2(0f, 0.5f);
        numberRect.anchoredPosition = new Vector2(48f, 0f);
        numberRect.sizeDelta = new Vector2(165f, 54f);
        TextMeshProUGUI label = number.GetComponent<TextMeshProUGUI>();
        label.font = balanceText.font;
        label.fontSize = 24f;
        label.color = new Color32(255, 224, 92, 255);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.text = "+" + MoneyFormat.Money(amount);
        return rect;
    }

    private IEnumerator FlyCoin(RectTransform popup, Image coin, CanvasGroup group,
        Vector2 start, Vector2 end, int amount, int lane)
    {
        float elapsed = 0f;
        while (elapsed < flightDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / flightDuration);
            float easeOut = 1f - Mathf.Pow(1f - t, 3f);
            Vector2 spread = new Vector2(0f, lane * 24f * Mathf.Sin(t * Mathf.PI));
            popup.anchoredPosition = Vector2.LerpUnclamped(start, end, easeOut) + spread;
            AnimateCoin(coin, elapsed);
            yield return null;
        }

        popup.anchoredPosition = end;
        pendingGains = Mathf.Max(0, pendingGains - amount);
        BeginCount(Mathf.Max(0, authoritativeBalance - pendingGains));

        float hold = 0f;
        while (hold < holdDuration)
        {
            hold += Time.unscaledDeltaTime;
            AnimateCoin(coin, elapsed + hold);
            yield return null;
        }

        float fade = 0f;
        while (fade < fadeDuration)
        {
            fade += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(fade / fadeDuration);
            AnimateCoin(coin, elapsed + hold + fade);
            yield return null;
        }

        activePopups.Remove(popup.gameObject);
        Destroy(popup.gameObject);
    }

    private void AnimateCoin(Image coin, float elapsed)
    {
        int frame = Mathf.FloorToInt(elapsed * 10f) % coinFrames.Length;
        if (coinFrames[frame] != null) coin.sprite = coinFrames[frame];
        coin.rectTransform.localEulerAngles = new Vector3(0f, 0f, -elapsed * spinDegreesPerSecond);
    }

    private void BeginCount(int value)
    {
        countFrom = displayedBalance;
        countTarget = value;
        countElapsed = 0f;
        counting = Mathf.Abs(countFrom - value) > 0.01f;
        if (!counting) RenderBalance();
    }

    private void SetImmediate(int value)
    {
        displayedBalance = countFrom = countTarget = value;
        countElapsed = 0f;
        counting = false;
        RenderBalance();
    }

    private void Update()
    {
        if (!counting) return;
        countElapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(countElapsed / countDuration);
        displayedBalance = Mathf.Lerp(countFrom, countTarget, 1f - Mathf.Pow(1f - t, 3f));
        if (t >= 1f)
        {
            displayedBalance = countTarget;
            counting = false;
        }
        RenderBalance();
    }

    private void RenderBalance()
    {
        // 走 MoneyFormat：通关目标抬到 7 位数之后，"$1000000" 会直接撑出余额框。
        if (balanceText != null) balanceText.text = "MONEY  " + MoneyFormat.Money((long)Mathf.RoundToInt(displayedBalance));
    }

    private void OnDisable()
    {
        ClearPopups();
        SetImmediate(authoritativeBalance);
    }

    private void ClearPopups()
    {
        StopAllCoroutines();
        for (int i = 0; i < activePopups.Count; i++)
            if (activePopups[i] != null) Destroy(activePopups[i]);
        activePopups.Clear();
        pendingGains = 0;
    }
}
