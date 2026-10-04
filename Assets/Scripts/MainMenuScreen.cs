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
    public enum ScreenView { MainMenu, Playing, Pause, Settings, NewGameConfirmation, Victory, Tutorial }

    [SerializeField] private LotteryGame game;
    [SerializeField] private NewGameTutorial tutorial;
    [SerializeField] private CanvasGroup gameplayUI;
    [SerializeField] private GameObject overlay;
    [SerializeField] private Image backdrop;
    [SerializeField] private GameObject mainDecoration;
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject confirmationPanel;
    // 通关结算。挂 VictoryPanel（脚本负责统计文案），这里只管显隐与两个按钮。
    [SerializeField] private GameObject victoryPanel;
    [SerializeField] private Button victoryContinueButton;
    [SerializeField] private Button victoryMenuButton;
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
    // 通关结算要主动找到菜单实例（LotteryGame 触发 → 菜单切视图）。
    // 静态实例而��� FindAnyObjectByType：达成判定在 RefreshUI 里，每帧可能跑，
    // 不想为此挂一条序列化引用；Awake 里自取一次，之后读字段。
    private static MainMenuScreen instance;
    public static MainMenuScreen Instance => instance;
    public static bool GameplayBlocked { get; private set; }
    public ScreenView CurrentView { get; private set; }
    private ScreenView settingsReturn = ScreenView.MainMenu;
    private bool fullscreen;
    private bool reloading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        startAfterReload = false;
        instance = null;
        GameplayBlocked = false;
        Time.timeScale = 1f;
    }

    private void Awake()
    {
        // New Game 会 LoadScene 重建整个场景，旧实例的 OnDestroy 必须能识别"我不是当前那个"。
        instance = this;
        foreach (Button button in mainPanel.GetComponentsInChildren<Button>(true))
            ButtonSfx.Attach(button).PlayHoverSound = true;
        fullscreen = PlayerPrefs.GetInt(DisplayKey, Screen.fullScreen ? 1 : 0) != 0;
        ApplyDisplayMode();
        // New Game 重载后startAfterReload 为 true：直接进Playing 视图，
        // 否则 MainMenuPanel 会在淡入的0.5 秒里闪一下再消失。
        Show(startAfterReload ? ScreenView.Playing : ScreenView.MainMenu);
        HoverJellySettings.Changed += RefreshSettings;
        // New Game 的 LoadScene 会重建整个场景。转场结束时必须重新评估暂停态，
        // 否则新实例停在 GameplayBlocked = true / timeScale = 0，玩家进不去游戏。
        ScreenFader.Completed += OnTransitionCompleted;
        // 启动时先铺满全黑，再淡入到主菜单。转场进行中时不要重复淡入。
        if (!ScreenFader.Busy) ScreenFader.FadeInOnStart();
    }

    private void OnTransitionCompleted()
    {
        // 重新评估暂停态：转场期间 Show 因为ScreenFader.Busy 而强制阻塞，
        // 现在 Busy 已为false，这次调用才真正把 GameplayBlocked 放开。
        Show(CurrentView);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        HoverJellySettings.Changed -= RefreshSettings;
        ScreenFader.Completed -= OnTransitionCompleted;
        if (reloading) return;
        GameplayBlocked = false;
        Time.timeScale = 1f;
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
        if (tutorial != null)
        {
            tutorial.Begin();
            Show(ScreenView.Tutorial);
        }
    }

    public void FinishTutorial()
    {
        if (CurrentView != ScreenView.Tutorial) return;
        Show(ScreenView.Playing);
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
            // 结算面板上 ESC = 继续玩，与 Pause 的 ESC 语义一致。
            case ScreenView.Victory: ResumeFromVictory(); break;
        }
    }

    // 通关结算：走一次淡入转场，在黑屏时刻切到 Victory 视图。
    // 转场期间 GameplayBlocked 保持 true（Show 里 blocked 含 ScreenFader.Busy），
    // 玩家看不到「面板凭空出现」，也点不到背后的游戏 UI。
    //统计数字由 LotteryGame 在达成那一帧算好传进来 —— 面板显示的是**达成瞬间的快照**。
    public void ShowVictory(int balance, float seconds, int tickets, float multiplier)
    {
        if (CurrentView == ScreenView.Victory) return;
        VictoryPanel panel = victoryPanel != null ? victoryPanel.GetComponent<VictoryPanel>() : null;
        if (panel != null) panel.Present(balance, seconds, tickets, multiplier);
        ScreenFader.Transition(() => Show(ScreenView.Victory));
    }

    // CONTINUE：结算只是「报喜」，不结束游戏。hasWon 已锁、进度条已锁 100%，
    // 玩家继续玩就是无尽模式，余额照常涨。
    public void ResumeFromVictory()
    {
        if (CurrentView != ScreenView.Victory) return;
        Show(ScreenView.Playing);
        LotterySfx.Play(LotterySfx.Sound.Back);
    }

    // MAIN MENU：先存一次再回主菜单，收益不能因为看结算而丢。
    public void VictoryToMainMenu()
    {
        if (CurrentView != ScreenView.Victory) return;
        game.SaveProgress();
        Show(ScreenView.MainMenu);
        LotterySfx.Play(LotterySfx.Sound.Back);
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
        // 场景重载会销毁本实例，所以 state必须放进委托闭包里带过去，
        // 不能依赖字段 —— 重载后新实例是另一个对象。
        ScreenFader.Transition(() =>
        {
            // Disable saving on the old instance before clearing; it must not recreate the old save.
            game.DiscardSessionForNewGame();
            startAfterReload = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        });
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
        // Continue 不重载场景，EnterGameplay 立刻生效；黑屏停留阶段就已经进游戏了。
        ScreenFader.Transition(EnterGameplay);
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
        // 转场进行中一律保持阻塞：Show(Playing) 会在黑屏停留阶段就被调用，
        // 若这里直接放开，玩家会在全黑画面里就能点东西。转场结束时由
        // OnTransitionCompleted 重新调一次 Show 来真正放开。
        bool blocked = view != ScreenView.Playing || ScreenFader.Busy;
        GameplayBlocked = blocked;
        Time.timeScale = blocked ? 0f : 1f;
        gameplayUI.interactable = !GameplayBlocked;
        gameplayUI.blocksRaycasts = !GameplayBlocked;
        overlay.SetActive(GameplayBlocked);
        bool mainBackground = view == ScreenView.MainMenu || view == ScreenView.NewGameConfirmation ||
            (view == ScreenView.Settings && settingsReturn == ScreenView.MainMenu);
        mainDecoration.SetActive(mainBackground);
        // 结算画面要盖住游戏画面（玩家刚达成目标，背后那堆盘子/机器不该再抢视线），
        // 所以 backdrop 用更暗的一档 —— 与主菜单同色会让人以为退回了主菜单。
        backdrop.color = view == ScreenView.Tutorial ? Color.clear : view == ScreenView.Victory
            ? new Color32(8, 12, 21, 235)
            : mainBackground ? new Color32(20, 27, 43, 255) : new Color32(10, 15, 26, 210);
        mainPanel.SetActive(view == ScreenView.MainMenu);
        pausePanel.SetActive(view == ScreenView.Pause);
        settingsPanel.SetActive(view == ScreenView.Settings);
        confirmationPanel.SetActive(view == ScreenView.NewGameConfirmation);
        if (victoryPanel != null) victoryPanel.SetActive(view == ScreenView.Victory);
        if (tutorial != null) tutorial.gameObject.SetActive(view == ScreenView.Tutorial);
        continueButton.interactable = LotteryGame.HasSavedGame;
        continueHint.text = continueButton.interactable ? "RESUME YOUR SAVED PROGRESS" : "NO SAVED GAME YET";
        RefreshSettings();
        if (EventSystem.current == null) return;
        Button focus = view == ScreenView.MainMenu ? newGameButton :
            view == ScreenView.Pause ? resumeButton :
            view == ScreenView.Settings ? reducedMotionButton :
            view == ScreenView.NewGameConfirmation ? cancelNewGameButton :
            // 结算默认聚焦 CONTINUE：ESC 和「回车/空格」都落到它身上，
            // 玩家第一反应按的键就是「继续」，不会误触 MAIN MENU。
            view == ScreenView.Victory ? victoryContinueButton :
            view == ScreenView.Tutorial && tutorial != null ? tutorial.NextButton : null;
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
}
