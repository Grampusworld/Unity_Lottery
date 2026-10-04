using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Adds authored, editable uGUI panels to the existing game scene without replacing its HUD.
public static class MainMenuSetup
{
    private static readonly Color32 Navy = new Color32(20, 27, 43, 255);
    private static readonly Color32 Card = new Color32(28, 44, 69, 255);
    private static readonly Color32 Cream = new Color32(246, 235, 205, 255);
    private static readonly Color32 Gold = new Color32(234, 179, 76, 255);
    private static readonly Color32 Teal = new Color32(73, 153, 158, 255);
    private static TMP_FontAsset font;

    [MenuItem("Tools/Lottery/Add Main Menu Screen")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene outside Play mode first.");
        if (UnityEngine.Object.FindAnyObjectByType<MainMenuScreen>() != null)
            throw new InvalidOperationException("Menu already exists; edit its panels in the Inspector.");
        var game = UnityEngine.Object.FindAnyObjectByType<LotteryGame>();
        var gameCanvas = GameObject.Find("Canvas");
        if (game == null || gameCanvas == null) throw new InvalidOperationException("Game/HUD missing.");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PressStart2P-Regular.asset");
        var table = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/Table.png");
        var lucky = SpriteAt("Assets/Lotteries/LuckyTicket/LuckyTicket_Base.png");
        var luckyCover = SpriteAt("Assets/Lotteries/LuckyTicket/LuckyTicket_Cover.png");
        var heart = SpriteAt("Assets/Lotteries/HeartMatchTicket/HeartMatch_Base.png");
        var sponge = SpriteAt("Assets/Materials/Sponges/Dish Sponge - Yellow.png");
        var washer = SpriteAt("Assets/Materials/Automatic Dish Washer/Automatic Dish Washer - Machine.png");
        var scratcher = SpriteAt("Assets/Materials/Auto Lottery Machine/Auto Lottery Machine V3 - Retro CRT.png");
        if (font == null || table == null) throw new InvalidOperationException("Menu font/table missing.");

        var canvasObject = new GameObject("MainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Add main menu");
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var menu = canvasObject.AddComponent<MainMenuScreen>();
        var hudGroup = gameCanvas.GetComponent<CanvasGroup>();
        if (hudGroup == null) hudGroup = gameCanvas.AddComponent<CanvasGroup>();
        var overlay = Stretch(canvas.transform, "MenuOverlay");
        var backdrop = Image(Stretch(overlay, "Backdrop"), Navy, true);
        var decoration = Stretch(overlay, "MainDecoration");
        var sceneFrame = Rect(decoration, "TableBorder", Vector2.zero, new Vector2(1190, 646));
        Image(sceneFrame, Gold);
        var tabletop = Rect(sceneFrame, "TableTexture", Vector2.zero, new Vector2(1180, 636)).gameObject.AddComponent<RawImage>();
        tabletop.texture = table;
        // Crop the painted table from its transparent 256px canvas; leave a visible border.
        tabletop.uvRect = new Rect(60f / 256f, 130f / 256f, 96f / 256f, 54f / 256f);
        tabletop.color = new Color(.45f, .47f, .53f, 1f);
        tabletop.raycastTarget = false;

        var luckyArt = Art(decoration, "LuckyTicketDecoration", lucky, new Vector2(-414, 168), new Vector2(245, 153));
        Art(luckyArt, "Cover", luckyCover, Vector2.zero, new Vector2(245, 153));
        Art(decoration, "HeartTicketDecoration", heart, new Vector2(408, 168), new Vector2(245, 153));
        Art(decoration, "WasherDecoration", washer, new Vector2(-418, -140), new Vector2(225, 225));
        Art(decoration, "ScratcherDecoration", scratcher, new Vector2(420, -115), new Vector2(205, 256));
        Art(decoration, "SpongeDecoration", sponge, new Vector2(430, -262), new Vector2(110, 55));

        var main = Panel(overlay, "MainPanel", new Vector2(574, 558));
        Text(main, "TitleLine1", "LOTTERY", new Vector2(0, 219), new Vector2(520, 58), 42, Gold);
        Text(main, "TitleLine2", "MANIA", new Vector2(0, 158), new Vector2(520, 58), 42, Cream);
        Text(main, "Subtitle", "SCRATCH. SCRUB. GROW.", new Vector2(0, 104), new Vector2(510, 24), 14, Teal);
        Image(Rect(main, "TitleDivider", new Vector2(0, 78), new Vector2(440, 3)), Teal);
        var newGame = Button(main, "NewGameButton", "NEW GAME", new Vector2(0, 20), new Vector2(410, 66), menu.RequestNewGame);
        var continueGame = Button(main, "ContinueButton", "CONTINUE", new Vector2(0, -65), new Vector2(410, 66), menu.ContinueGame);
        var hint = Text(main, "ContinueHint", "RESUME YOUR SAVED PROGRESS", new Vector2(0, -115), new Vector2(500, 20), 10, Cream);
        Button(main, "SettingsButton", "SETTING", new Vector2(0, -168), new Vector2(410, 66), menu.OpenSettings);
        Text(main, "Footer", "PIXEL INCREMENTAL GAME", new Vector2(0, -245), new Vector2(510, 24), 11, Teal);

        var pause = Panel(overlay, "PausePanel", new Vector2(574, 434));
        Text(pause, "Title", "PAUSED", new Vector2(0, 155), new Vector2(520, 52), 32, Gold);
        var resume = Button(pause, "ResumeButton", "RESUME", new Vector2(0, 72), new Vector2(410, 66), menu.Resume);
        Button(pause, "SettingsButton", "SETTING", new Vector2(0, -13), new Vector2(410, 66), menu.OpenSettings);
        Button(pause, "MainMenuButton", "MAIN MENU", new Vector2(0, -98), new Vector2(410, 66), menu.ReturnToMainMenu);
        Text(pause, "Hint", "ESC TO RESUME", new Vector2(0, -176), new Vector2(510, 22), 11, Teal);

        var settings = Panel(overlay, "SettingsPanel", new Vector2(680, 454));
        Text(settings, "Title", "SETTING", new Vector2(0, 166), new Vector2(630, 48), 30, Gold);
        var reduce = Button(settings, "ReducedMotionButton", "REDUCED MOTION  OFF", new Vector2(0, 78), new Vector2(570, 70), menu.ToggleReducedMotion, 17);
        var display = Button(settings, "DisplayModeButton", "DISPLAY  FULLSCREEN", new Vector2(0, -12), new Vector2(570, 70), menu.ToggleDisplayMode, 17);
        Text(settings, "AutoSaveHint", "CHANGES ARE SAVED AUTOMATICALLY", new Vector2(0, -71), new Vector2(630, 24), 10, Cream);
        Button(settings, "BackButton", "BACK", new Vector2(0, -138), new Vector2(310, 62), menu.BackFromSettings);
        Text(settings, "Hint", "ESC TO GO BACK", new Vector2(0, -202), new Vector2(630, 22), 11, Teal);

        var confirmation = Panel(overlay, "NewGameConfirmation", new Vector2(680, 374));
        Text(confirmation, "Title", "START NEW GAME?", new Vector2(0, 126), new Vector2(630, 45), 24, Gold);
        var message = Text(confirmation, "Warning", "YOUR CURRENT PROGRESS\nWILL BE REPLACED.\nTHIS CANNOT BE UNDONE.", new Vector2(0, 25), new Vector2(630, 114), 17, Cream);
        message.textWrappingMode = TextWrappingModes.Normal;
        var cancel = Button(confirmation, "CancelButton", "CANCEL", new Vector2(-158, -104), new Vector2(280, 65), menu.CancelNewGame);
        Button(confirmation, "ConfirmButton", "NEW GAME", new Vector2(158, -104), new Vector2(280, 65), menu.ConfirmNewGame);

        var data = new SerializedObject(menu);
        Assign(data, "game", game); Assign(data, "gameplayUI", hudGroup);
        Assign(data, "overlay", overlay.gameObject); Assign(data, "backdrop", backdrop);
        Assign(data, "mainDecoration", decoration.gameObject);
        Assign(data, "mainPanel", main.gameObject); Assign(data, "pausePanel", pause.gameObject);
        Assign(data, "settingsPanel", settings.gameObject); Assign(data, "confirmationPanel", confirmation.gameObject);
        Assign(data, "newGameButton", newGame); Assign(data, "continueButton", continueGame);
        Assign(data, "resumeButton", resume); Assign(data, "reducedMotionButton", reduce);
        Assign(data, "cancelNewGameButton", cancel); Assign(data, "continueHint", hint);
        Assign(data, "reducedMotionLabel", reduce.GetComponentInChildren<TMP_Text>());
        Assign(data, "displayLabel", display.GetComponentInChildren<TMP_Text>());
        data.ApplyModifiedPropertiesWithoutUndo();
        pause.gameObject.SetActive(false);
        settings.gameObject.SetActive(false);
        confirmation.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("LOTTERY MANIA main menu, pause and settings panels added.");
    }

    private static void Assign(SerializedObject data, string field, UnityEngine.Object value)
    {
        var prop = data.FindProperty(field);
        if (prop == null) throw new InvalidOperationException("Missing menu field: " + field);
        prop.objectReferenceValue = value;
    }

    private static Sprite SpriteAt(string path)
    {
        var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (sprite == null) throw new InvalidOperationException("Missing sprite: " + path);
        return sprite;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    private static RectTransform Stretch(Transform parent, string name)
    {
        var rt = Rect(parent, name, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static Image Image(RectTransform rt, Color color, bool raycast = false)
    {
        var image = rt.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = raycast;
        return image;
    }

    private static RectTransform Panel(Transform parent, string name, Vector2 size)
    {
        var border = Rect(parent, name, Vector2.zero, size);
        Image(border, Gold);
        var inside = Stretch(border, "NavyInset");
        inside.offsetMin = new Vector2(4, 4); inside.offsetMax = new Vector2(-4, -4);
        Image(inside, Navy);
        return border;
    }

    private static RectTransform Art(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size)
    {
        var rt = Rect(parent, name, pos, size);
        var image = Image(rt, Color.white);
        image.sprite = sprite; image.preserveAspect = true;
        return rt;
    }

    private static TextMeshProUGUI Text(Transform parent, string name, string value, Vector2 pos, Vector2 size, float fontSize, Color color)
    {
        var text = Rect(parent, name, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = fontSize; text.color = color;
        text.text = value; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.raycastTarget = false;
        return text;
    }

    private static Button Button(Transform parent, string name, string value, Vector2 pos, Vector2 size, UnityAction callback, float fontSize = 20)
    {
        var rt = Rect(parent, name, pos, size);
        var image = Image(rt, Gold, true);
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, 1f, .75f);
        colors.selectedColor = new Color(1f, 1f, .75f); colors.pressedColor = new Color(.7f, .8f, .9f);
        colors.disabledColor = new Color(.35f, .35f, .35f); colors.fadeDuration = .06f;
        button.colors = colors;
        Image(Rect(rt, "Inset", Vector2.zero, size - new Vector2(6, 6)), Card);
        Text(rt, "Label", value, Vector2.zero, size - new Vector2(22, 12), fontSize, Cream);
        rt.gameObject.AddComponent<HoverJelly>();
        UnityEventTools.AddPersistentListener(button.onClick, callback);
        return button;
    }
}
