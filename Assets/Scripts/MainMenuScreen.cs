using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// An overlay keeps unfinished tickets and machine cycles alive when returning to the menu.
[DefaultExecutionOrder(-100)]
public class MainMenuScreen : MonoBehaviour
{
    public enum ScreenView { MainMenu, Playing, Pause, Settings, NewGameConfirmation }

    [SerializeField] private LotteryGame game;
    [SerializeField] private CanvasGroup gameplayUI;
    [SerializeField] private GameObject overlay;
    [SerializeField] private Image backdrop;
    [SerializeField] private GameObject mainDecoration;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject confirmationPanel;
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button reducedMotionButton;
    [SerializeField] private Button cancelNewGameButton;
    [SerializeField] private TMP_Text continueHint;
    [SerializeField] private TMP_Text reducedMotionLabel;
    [SerializeField] private TMP_Text displayLabel;

    private const string DisplayKey = "LotteryMania.Settings.FullScreen";
    private static bool startAfterReload;
    public static bool GameplayBlocked { get; private set; }
    public ScreenView CurrentView { get; private set; }
    private ScreenView settingsReturn = ScreenView.MainMenu;
    private bool fullscreen;
    private bool reloading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        startAfterReload = false;
        GameplayBlocked = false;
        Time.timeScale = 1f;
    }

    private void Awake()
    {
        foreach (Button button in mainPanel.GetComponentsInChildren<Button>(true))
            ButtonSfx.Attach(button).PlayHoverSound = true;
        fullscreen = PlayerPrefs.GetInt(DisplayKey, Screen.fullScreen ? 1 : 0) != 0;
        ApplyDisplayMode();
        Show(ScreenView.MainMenu);
        HoverJellySettings.Changed += RefreshSettings;
    }

    private IEnumerator Start()
    {
        // All gameplay Start methods must load the save before we can save a new session.
        yield return null;
        if (!startAfterReload)
        {
            // EventSystem may not exist during our early Awake; select after all scene Starts.
            Show(CurrentView);
            yield break;
        }
        startAfterReload = false;
        EnterGameplay();
    }

    private void Update()
    {
        if (reloading || !EscapePressed()) return;
        switch (CurrentView)
        {
            case ScreenView.Playing: OpenPause(); break;
            case ScreenView.Pause: Resume(); break;
            case ScreenView.Settings: BackFromSettings(); break;
            case ScreenView.NewGameConfirmation: CancelNewGame(); break;
        }
    }

    public void RequestNewGame()
    {
        if (reloading || CurrentView != ScreenView.MainMenu) return;
        if (LotteryGame.HasSavedGame) Show(ScreenView.NewGameConfirmation);
        else ConfirmNewGame();
    }

    public void ConfirmNewGame()
    {
        if (reloading || (CurrentView != ScreenView.MainMenu && CurrentView != ScreenView.NewGameConfirmation)) return;
        reloading = true;
        // Disable saving on the old instance before clearing; it must not recreate the old save.
        game.DiscardSessionForNewGame();
        startAfterReload = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void CancelNewGame()
    {
        if (CurrentView != ScreenView.NewGameConfirmation) return;
        Show(ScreenView.MainMenu);
        LotterySfx.Play(LotterySfx.Sound.Back);
    }

    public void ContinueGame()
    {
        if (reloading || CurrentView != ScreenView.MainMenu || !LotteryGame.HasSavedGame) return;
        EnterGameplay();
    }

    private void EnterGameplay()
    {
        game.BeginSession();
        Show(ScreenView.Playing);
    }

    public void OpenPause()
    {
        if (CurrentView != ScreenView.Playing) return;
        game.HideDebugMenu();
        game.SaveProgress();
        Show(ScreenView.Pause);
    }

    public void Resume()
    {
        if (CurrentView != ScreenView.Pause) return;
        Show(ScreenView.Playing);
        LotterySfx.Play(LotterySfx.Sound.Back);
    }

    public void ReturnToMainMenu()
    {
        if (CurrentView != ScreenView.Pause) return;
        game.SaveProgress();
        Show(ScreenView.MainMenu);
        LotterySfx.Play(LotterySfx.Sound.Back);
    }

    public void OpenSettings()
    {
        if (CurrentView != ScreenView.MainMenu && CurrentView != ScreenView.Pause) return;
        settingsReturn = CurrentView;
        RefreshSettings();
        Show(ScreenView.Settings);
    }

    public void BackFromSettings()
    {
        if (CurrentView != ScreenView.Settings) return;
        Show(settingsReturn);
        LotterySfx.Play(LotterySfx.Sound.Back);
    }

    public void ToggleReducedMotion()
    {
        HoverJellySettings.Toggle();
        LotterySfx.Play(LotterySfx.Sound.SettingToggle);
    }

    public void ToggleDisplayMode()
    {
        fullscreen = !fullscreen;
        PlayerPrefs.SetInt(DisplayKey, fullscreen ? 1 : 0);
        PlayerPrefs.Save();
        ApplyDisplayMode();
        RefreshSettings();
        LotterySfx.Play(LotterySfx.Sound.SettingToggle);
    }

    private void ApplyDisplayMode()
    {
        // The Editor Game view cannot switch the OS window; a standalone player can.
        if (!Application.isEditor)
            Screen.fullScreenMode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }

    private void RefreshSettings()
    {
        reducedMotionLabel.text = "REDUCED MOTION  " + (HoverJellySettings.ReducedMotion ? "ON" : "OFF");
        displayLabel.text = "DISPLAY  " + (fullscreen ? "FULLSCREEN" : "WINDOWED");
    }

    private void Show(ScreenView view)
    {
        CurrentView = view;
        GameplayBlocked = view != ScreenView.Playing;
        Time.timeScale = GameplayBlocked ? 0f : 1f;
        gameplayUI.interactable = !GameplayBlocked;
        gameplayUI.blocksRaycasts = !GameplayBlocked;
        overlay.SetActive(GameplayBlocked);
        bool mainBackground = view == ScreenView.MainMenu || view == ScreenView.NewGameConfirmation ||
            (view == ScreenView.Settings && settingsReturn == ScreenView.MainMenu);
        mainDecoration.SetActive(mainBackground);
        backdrop.color = mainBackground ? new Color32(20, 27, 43, 255) : new Color32(10, 15, 26, 210);
        mainPanel.SetActive(view == ScreenView.MainMenu);
        pausePanel.SetActive(view == ScreenView.Pause);
        settingsPanel.SetActive(view == ScreenView.Settings);
        confirmationPanel.SetActive(view == ScreenView.NewGameConfirmation);
        continueButton.interactable = LotteryGame.HasSavedGame;
        continueHint.text = continueButton.interactable ? "RESUME YOUR SAVED PROGRESS" : "NO SAVED GAME YET";
        RefreshSettings();
        if (EventSystem.current == null) return;
        Button focus = view == ScreenView.MainMenu ? newGameButton :
            view == ScreenView.Pause ? resumeButton :
            view == ScreenView.Settings ? reducedMotionButton :
            view == ScreenView.NewGameConfirmation ? cancelNewGameButton : null;
        EventSystem.current.SetSelectedGameObject(focus != null ? focus.gameObject : null);
    }

    private static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    private void OnDestroy()
    {
        HoverJellySettings.Changed -= RefreshSettings;
        if (reloading) return;
        GameplayBlocked = false;
        Time.timeScale = 1f;
    }
}
