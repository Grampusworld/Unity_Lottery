using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
[DisallowMultipleComponent]
public class ButtonSfx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private bool playClickSound = true;
    [SerializeField] private bool playHoverSound;
    public bool PlayClickSound { get => playClickSound; set => playClickSound = value; }
    public bool PlayHoverSound { get => playHoverSound; set => playHoverSound = value; }

    private Button button;
    private bool hovering;

    public static ButtonSfx Attach(Button target)
    {
        ButtonSfx feedback = target.GetComponent<ButtonSfx>();
        if (feedback == null) feedback = target.gameObject.AddComponent<ButtonSfx>();
        // These actions play their own result cue, including keyboard/direct invocations.
        for (int i = 0; i < target.onClick.GetPersistentEventCount(); i++)
        {
            Object owner = target.onClick.GetPersistentTarget(i);
            string method = target.onClick.GetPersistentMethodName(i);
            if ((owner is MainMenuScreen && (method == nameof(MainMenuScreen.ToggleReducedMotion) ||
                method == nameof(MainMenuScreen.ToggleDisplayMode) || method == nameof(MainMenuScreen.Resume) ||
                method == nameof(MainMenuScreen.CancelNewGame) || method == nameof(MainMenuScreen.BackFromSettings) ||
                method == nameof(MainMenuScreen.ReturnToMainMenu))) ||
                (owner is LotteryGame && (method == nameof(LotteryGame.ShowTickets) ||
                method == nameof(LotteryGame.ShowGadgets))) ||
                (owner is HoverJellyDebugToggle && method == nameof(HoverJellyDebugToggle.Toggle)))
                feedback.PlayClickSound = false;
        }
        return feedback;
    }

    private void Awake() => button = GetComponent<Button>();

    private void OnEnable()
    {
        if (button == null) button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
    }

    private void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(OnClick);
        hovering = false;
    }

    private void OnClick()
    {
        // A valid click may already have hidden its panel through a persistent listener.
        // Button itself filters inactive/disabled input; onClick also covers keyboard Submit.
        if (PlayClickSound) LotterySfx.Play(LotterySfx.Sound.Select);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hovering || !isActiveAndEnabled || !button.IsInteractable()) return;
        hovering = true;
        if (PlayHoverSound) LotterySfx.Play(LotterySfx.Sound.MenuHover);
    }

    public void OnPointerExit(PointerEventData eventData) => hovering = false;
}
