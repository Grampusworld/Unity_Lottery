using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The menu owns modality and pause state; this panel only owns its five pages.
public class NewGameTutorial : MonoBehaviour
{
    [SerializeField] private MainMenuScreen menu;
    [SerializeField] private TMP_Text heading;
    [SerializeField] private TMP_Text body;
    [SerializeField] private TMP_Text counter;
    [SerializeField] private TMP_Text nextLabel;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private GameObject kitty1;
    [SerializeField] private GameObject kitty2;
    [SerializeField] private LotteryGame game;
    [SerializeField] private RectTransform plateTarget;
    [SerializeField] private RectTransform shopTarget;
    [SerializeField] private RectTransform spotlightRoot;
    [SerializeField] private RectTransform[] shades;
    [SerializeField] private RectTransform highlightFrame;
    private readonly Vector3[] corners = new Vector3[4];

    private static readonly string[] Titles =
    {
        "A VERY HUNGRY INVESTOR", "SCRUB YOUR WAY TO RICHES",
        "SCRATCH RESPONSIBLY, HUMAN", "HIRE SOME ROBOT PAWS", "THE BOSS BELIEVES IN YOU"
    };
    private static readonly string[] Pages =
    {
        "Welcome to Lottery Mania—your goal is to earn $2,000,000 to buy me ultra-expensive cat food. What? Why is cat food so expensive? Because it's the year 9999 inflation.",
        "Click 'One More Plate' in the bottom-left corner, and a dirty plate appears on screen. Drag the sponge to clean dirty plates and earn your basic income.",
        "When you have enough money, remember to click the button on the left to buy lottery tickets. Warning: lottery tickets can win or lose—you won't always make a profit. If you're short on cash, go back to washing plates.",
        "Once you've saved up enough, you can buy the Auto Plate Washer and Auto Lottery Scratcher, and unlock higher-tier lottery tickets to earn even more coins.",
        "Good luck! Meow!"
    };
    private int page;
    private bool running;
    public Button NextButton => nextButton;

    private void OnEnable()
    {
        spotlightRoot.gameObject.SetActive(true);
        UpdateSpotlight();
    }

    private void OnDisable()
    {
        if (spotlightRoot != null) spotlightRoot.gameObject.SetActive(false);
    }

    private void LateUpdate() => UpdateSpotlight();

    public void Begin()
    {
        page = 0;
        running = true;
        RefreshPage();
    }

    public void Next()
    {
        if (!running || menu.CurrentView != MainMenuScreen.ScreenView.Tutorial || ScreenFader.Busy) return;
        if (page == Pages.Length - 1)
        {
            running = false;
            menu.FinishTutorial();
            return;
        }
        page++;
        RefreshPage();
    }

    public void Previous()
    {
        if (!running || page == 0 || menu.CurrentView != MainMenuScreen.ScreenView.Tutorial || ScreenFader.Busy) return;
        page--;
        RefreshPage();
        if (page == 0 && UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(nextButton.gameObject);
    }

    private void RefreshPage()
    {
        heading.text = Titles[page];
        body.text = Pages[page];
        counter.text = (page + 1) + " / " + Pages.Length;
        nextLabel.text = page == Pages.Length - 1 ? "LET'S PLAY!" : "NEXT >";
        previousButton.gameObject.SetActive(page > 0);
        kitty1.SetActive(page < Pages.Length - 1);
        kitty2.SetActive(page == Pages.Length - 1);
        if (page == 3) game.ShowGadgets();
        else game.ShowTickets();
        UpdateSpotlight();
    }

    private void UpdateSpotlight()
    {
        var target = page == 1 ? plateTarget : page == 2 || page == 3 ? shopTarget : null;
        Rect bounds = spotlightRoot.rect;
        if (target == null)
        {
            Place(shades[0], bounds.min, bounds.max);
            for (int i = 1; i < shades.Length; i++) shades[i].gameObject.SetActive(false);
            highlightFrame.gameObject.SetActive(false);
            ((RectTransform)transform).anchoredPosition = Vector2.zero;
            return;
        }
        target.GetWorldCorners(corners);
        var targetCanvas = target.GetComponentInParent<Canvas>().rootCanvas;
        var overlayCanvas = spotlightRoot.GetComponentInParent<Canvas>().rootCanvas;
        Camera from = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
        Camera to = overlayCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : overlayCanvas.worldCamera;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (var corner in corners)
        {
            Vector2 point;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(spotlightRoot,
                RectTransformUtility.WorldToScreenPoint(from, corner), to, out point);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        min = Vector2.Max(bounds.min, min - Vector2.one * 6);
        max = Vector2.Min(bounds.max, max + Vector2.one * 6);
        Place(shades[0], bounds.min, new Vector2(min.x, bounds.yMax));
        Place(shades[1], new Vector2(max.x, bounds.yMin), bounds.max);
        Place(shades[2], new Vector2(min.x, bounds.yMin), new Vector2(max.x, min.y));
        Place(shades[3], new Vector2(min.x, max.y), new Vector2(max.x, bounds.yMax));
        Place(highlightFrame, min, max);
        // Keep the dialog beside the shop, clamped inside the reference canvas.
        var panel = (RectTransform)transform;
        panel.anchoredPosition = new Vector2(page == 1 ? 0 :
            Mathf.Clamp(max.x + 20 + panel.rect.width * .5f, 0, Mathf.Max(0, bounds.xMax - panel.rect.width * .5f - 20)), 0);
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.gameObject.SetActive(true);
        rect.anchoredPosition = (min + max) * .5f;
        rect.sizeDelta = Vector2.Max(Vector2.zero, max - min);
    }
}
