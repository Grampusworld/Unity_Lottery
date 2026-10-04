using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// One persistent owner; each cue has its own voice so different cues can overlap.
[DisallowMultipleComponent]
public class LotterySfx : MonoBehaviour
{
    public enum Sound
    {
        MenuHover, Select, CoinGain, DragStart, InsufficientFunds,
        Scratch, Drop, SettingToggle, ShopTab, Back, ManualFeed
    }

    private const string ResourcePath = "LotterySfx";
    // BGM 从 Resources 运行时加载，不占 prefab 序列化字段（改字段默认值对已有 prefab 无效）。
    private const string MusicResourcePath = "Music/happy_adveture";
    private static readonly string[] ClipNames =
        { "drop_003", "select_005", "confirmation_003", "drop_002", "error_005",
          "scratch_003", "drop_001", "toggle_004", "tick_002", "back_001", "pluck_002" };
    private static LotterySfx instance;

    [SerializeField] private AudioClip menuHover;
    [SerializeField] private AudioClip select;
    [SerializeField] private AudioClip coinGain;
    [SerializeField] private AudioClip dragStart;
    [SerializeField] private AudioClip insufficientFunds;
    [SerializeField] private AudioClip scratch;
    [SerializeField] private AudioClip drop;
    [SerializeField] private AudioClip settingToggle;
    [SerializeField] private AudioClip shopTab;
    [SerializeField] private AudioClip back;
    [SerializeField] private AudioClip manualFeed;
    [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.35f;

    private readonly AudioSource[] voices = new AudioSource[ClipNames.Length];
    private readonly int[] lastFrames = new int[ClipNames.Length];
    private readonly bool[] warnedMissing = new bool[ClipNames.Length];
    private AudioSource musicSource;
    private bool warnedMusicMissing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        SceneManager.sceneLoaded -= BindSceneButtons;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance == null)
        {
            LotterySfx prefab = Resources.Load<LotterySfx>(ResourcePath);
            if (prefab != null) Instantiate(prefab);
            else
            {
                Debug.LogWarning("[LotterySfx] Missing Assets/Resources/LotterySfx.prefab. " +
                    "Sound effects are unavailable; restore the prefab and its audio references.");
                new GameObject("LotterySfx (missing configuration)").AddComponent<LotterySfx>();
            }
        }
        SceneManager.sceneLoaded -= BindSceneButtons;
        SceneManager.sceneLoaded += BindSceneButtons;
    }

    private void Awake() => Initialize();

    // Restore the owner/voices after an Editor script reload without creating extra sources.
    private void OnEnable() => Initialize();

    private void Initialize()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded -= BindSceneButtons;
        SceneManager.sceneLoaded += BindSceneButtons;
        if (voices[0] != null)
        {
            EnsureMusic();
            return;
        }
        AudioSource[] existingVoices = GetComponents<AudioSource>();
        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource voice = i < existingVoices.Length
                ? existingVoices[i] : gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            voice.ignoreListenerPause = true;
            voices[i] = voice;
            lastFrames[i] = -1;
            if (GetClip((Sound)i) == null) WarnMissing(i);
        }
        EnsureMusic();
    }

    // BGM 走独立子物体 + 独立 AudioSource，loop=true，与音效声部互不干扰。
    // 幂等：域重载 / 重复 Initialize 不会建出第二个声部或重新起播。
    private void EnsureMusic()
    {
        if (musicSource == null)
        {
            Transform existing = transform.Find("BGM");
            GameObject go = existing != null ? existing.gameObject
                : new GameObject("BGM");
            if (existing == null)
            {
                go.transform.SetParent(transform, false);
                go.AddComponent<AudioSource>();
            }
            musicSource = go.GetComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;              // 循环播放
            musicSource.spatialBlend = 0f;        // 2D，不随距离衰减
            musicSource.ignoreListenerPause = true; // Time.timeScale=0 时继续响
        }
        if (musicSource.clip == null)
            musicSource.clip = Resources.Load<AudioClip>(MusicResourcePath);
        if (musicSource.clip == null)
        {
            if (!warnedMusicMissing)
            {
                warnedMusicMissing = true;
                Debug.LogWarning($"[LotterySfx] BGM clip not found at Assets/Resources/{MusicResourcePath}.mp3. " +
                    "Background music is unavailable.", this);
            }
            return;
        }
        musicSource.volume = musicVolume;
        if (!musicSource.isPlaying) musicSource.Play();
    }

    private static void BindSceneButtons(Scene scene, LoadSceneMode mode)
    {
        foreach (Button button in Object.FindObjectsByType<Button>(
            FindObjectsInactive.Include))
            if (button.gameObject.scene == scene) ButtonSfx.Attach(button);
    }

    // Returns false when a repeated request is suppressed or configuration is missing.
    public static bool Play(Sound sound)
    {
        if (instance == null)
        {
            Debug.LogWarning("[LotterySfx] No active sound manager. " +
                "Check Assets/Resources/LotterySfx.prefab and enter Play Mode again.");
            return false;
        }
        return instance.PlayCue(sound);
    }

    private bool PlayCue(Sound sound)
    {
        int index = (int)sound;
        AudioClip clip = GetClip(sound);
        if (clip == null)
        {
            WarnMissing(index);
            return false;
        }
        AudioSource voice = voices[index];
        // Covers duplicate requests before the audio engine starts playback, too.
        if (voice.isPlaying || lastFrames[index] == Time.frameCount) return false;
        lastFrames[index] = Time.frameCount;
        voice.clip = clip;
        voice.volume = volume;
        voice.Play();
        return true;
    }

    private AudioClip GetClip(Sound sound)
    {
        switch (sound)
        {
            case Sound.MenuHover: return menuHover;
            case Sound.Select: return select;
            case Sound.CoinGain: return coinGain;
            case Sound.DragStart: return dragStart;
            case Sound.InsufficientFunds: return insufficientFunds;
            case Sound.Scratch: return scratch;
            case Sound.Drop: return drop;
            case Sound.SettingToggle: return settingToggle;
            case Sound.ShopTab: return shopTab;
            case Sound.Back: return back;
            case Sound.ManualFeed: return manualFeed;
            default: return null;
        }
    }

    private void WarnMissing(int index)
    {
        if (warnedMissing[index]) return;
        warnedMissing[index] = true;
        Debug.LogWarning($"[LotterySfx] {((Sound)index)} has no AudioClip assigned. " +
            $"Expected Assets/Materials/Audios/{ClipNames[index]}.ogg; " +
            "check the audio file and the reference on Assets/Resources/LotterySfx.prefab.", this);
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        SceneManager.sceneLoaded -= BindSceneButtons;
    }
}
