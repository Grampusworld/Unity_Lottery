using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class NewGameTutorialSetup
{
    private static readonly Color32 Navy = new Color32(20, 27, 43, 255);
    private static readonly Color32 Gold = new Color32(234, 179, 76, 255);
    private static readonly Color32 Cream = new Color32(246, 235, 205, 255);
    private static TMP_FontAsset font;

    [MenuItem("Tools/Lottery/Add New Game Tutorial")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene outside Play mode first.");
        var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuScreen>();
        if (menu == null) throw new InvalidOperationException("Main menu missing.");
        var serializedMenu = new SerializedObject(menu);
        if (serializedMenu.FindProperty("tutorial").objectReferenceValue != null)
            { Upgrade(); return; }
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PressStart2P-Regular.asset");
        var kitty1 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/Kitty1.png");
        var kitty2 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/Kitty2.png");
        if (font == null || kitty1 == null || kitty2 == null)
            throw new InvalidOperationException("Tutorial font/cat artwork missing.");

        var overlay = (GameObject)serializedMenu.FindProperty("overlay").objectReferenceValue;
        var panel = Rect(overlay.transform, "NewGameTutorial", Vector2.zero, new Vector2(880, 390));
        Undo.RegisterCreatedObjectUndo(panel.gameObject, "Add new game tutorial");
        Fill(Rect(panel, "Shadow", new Vector2(8, -8), panel.sizeDelta), new Color32(5, 9, 17, 255));
        Fill(Rect(panel, "Border", Vector2.zero, panel.sizeDelta), Gold);
        Fill(Rect(panel, "Inset", Vector2.zero, panel.sizeDelta - new Vector2(8, 8)), Navy);
        var tutorial = panel.gameObject.AddComponent<NewGameTutorial>();
        var heading = Text(panel, "Heading", "A VERY HUNGRY INVESTOR", new Vector2(0, 137), new Vector2(800, 40), 20, Gold);
        Fill(Rect(panel, "Divider", new Vector2(0, 102), new Vector2(792, 3)), new Color32(73, 153, 158, 255));
        var body = Text(panel, "Body", "", new Vector2(0, 8), new Vector2(792, 158), 18, Cream);
        body.alignment = TextAlignmentOptions.MidlineLeft;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.lineSpacing = 18;
        var counter = Text(panel, "PageCount", "1 / 5", new Vector2(-186, -143), new Vector2(420, 28), 12, Cream);
        var buttonRect = Rect(panel, "NextButton", new Vector2(272, -139), new Vector2(248, 54));
        var buttonImage = Fill(buttonRect, Gold);
        buttonImage.raycastTarget = true;
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var colors = button.colors;
        colors.highlightedColor = new Color(1, 1, .75f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.7f, .8f, .9f);
        colors.fadeDuration = 0;
        button.colors = colors;
        var label = Text(buttonRect, "Label", "NEXT >", Vector2.zero, new Vector2(236, 44), 18, Navy);
        UnityEventTools.AddPersistentListener(button.onClick, tutorial.Next);
        ButtonSfx.Attach(button);
        Cat(panel, "Kitty1", kitty1, new Vector2(-490, 164));
        Cat(panel, "Kitty2", kitty2, new Vector2(490, -110));

        var serialized = new SerializedObject(tutorial);
        Set(serialized, "menu", menu);
        Set(serialized, "heading", heading);
        Set(serialized, "body", body);
        Set(serialized, "counter", counter);
        Set(serialized, "nextLabel", label);
        Set(serialized, "nextButton", button);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Set(serializedMenu, "tutorial", tutorial);
        serializedMenu.ApplyModifiedProperties();
        panel.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Upgrade();
    }

    // Upgrade the authored panel in place, preserving its existing references.
    public static void Upgrade()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuScreen>();
        var tutorial = menu.GetComponentInChildren<NewGameTutorial>(true);
        var panel = (RectTransform)tutorial.transform;
        font = panel.Find("Heading").GetComponent<TMP_Text>().font;
        var next = panel.Find("NextButton").GetComponent<Button>();
        var nextRect = (RectTransform)next.transform;
        nextRect.anchoredPosition = new Vector2(124, -139);
        nextRect.sizeDelta = new Vector2(196, 48);
        var label = next.GetComponentInChildren<TMP_Text>();
        label.rectTransform.sizeDelta = new Vector2(188, 40);
        label.fontSize = 15;
        var count = panel.Find("PageCount").GetComponent<TMP_Text>();
        count.text = "1 / 5";
        count.rectTransform.anchoredPosition = new Vector2(-335, -139);
        count.rectTransform.sizeDelta = new Vector2(100, 28);
        var previousTransform = panel.Find("PreviousButton");
        Button previous;
        if (previousTransform == null)
        {
            previous = UnityEngine.Object.Instantiate(next, panel);
            previous.name = "PreviousButton";
            for (int i = previous.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(previous.onClick, i);
            UnityEventTools.AddPersistentListener(previous.onClick, tutorial.Previous);
        }
        else previous = previousTransform.GetComponent<Button>();
        ((RectTransform)previous.transform).anchoredPosition = new Vector2(-104, -139);
        previous.GetComponentInChildren<TMP_Text>().text = "PREVIOUS";
        previous.gameObject.SetActive(false);
        foreach (string name in new[] { "Kitty1", "Kitty2" })
        {
            var cat = (RectTransform)panel.Find(name);
            cat.anchoredPosition = new Vector2(354, -153);
            cat.sizeDelta = new Vector2(144, 144);
            cat.gameObject.SetActive(name == "Kitty1");
        }
        var root = panel.parent.Find("TutorialSpotlight") as RectTransform;
        if (root == null)
        {
            root = Rect(panel.parent, "TutorialSpotlight", Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetSiblingIndex(panel.GetSiblingIndex());
            foreach (string name in new[] { "Left", "Right", "Bottom", "Top" })
                Fill(Rect(root, name, Vector2.zero, Vector2.zero), new Color32(10, 15, 26, 210));
            var frame = Rect(root, "HighlightFrame", Vector2.zero, Vector2.zero);
            for (int i = 0; i < 4; i++)
            {
                var edge = Rect(frame, "Edge" + i, Vector2.zero, Vector2.zero);
                bool horizontal = i < 2;
                edge.anchorMin = horizontal ? new Vector2(0, i) : new Vector2(i - 2, 0);
                edge.anchorMax = horizontal ? new Vector2(1, i) : new Vector2(i - 2, 1);
                edge.sizeDelta = horizontal ? new Vector2(0, 4) : new Vector2(4, 0);
                Fill(edge, Gold);
            }
        }
        var game = UnityEngine.Object.FindAnyObjectByType<LotteryGame>();
        var gameData = new SerializedObject(game);
        var plate = (Button)gameData.FindProperty("plateButton").objectReferenceValue;
        var tickets = (GameObject)gameData.FindProperty("ticketsPanel").objectReferenceValue;
        var data = new SerializedObject(tutorial);
        Set(data, "previousButton", previous);
        Set(data, "kitty1", panel.Find("Kitty1").gameObject);
        Set(data, "kitty2", panel.Find("Kitty2").gameObject);
        Set(data, "game", game);
        Set(data, "plateTarget", plate.transform);
        var shop = (RectTransform)tickets.transform.parent;
        var region = shop.Find("TutorialShopRegion") as RectTransform;
        if (region == null) region = Rect(shop, "TutorialShopRegion", Vector2.zero, Vector2.zero);
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var corners = new Vector3[4];
        foreach (string field in new[] { "ticketsPanel", "gadgetsPanel", "ticketsTabButton", "gadgetsTabButton" })
        {
            var value = gameData.FindProperty(field).objectReferenceValue;
            var target = value is GameObject ? ((GameObject)value).GetComponent<RectTransform>() : ((Component)value).GetComponent<RectTransform>();
            target.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector2 point = shop.InverseTransformPoint(corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        region.sizeDelta = max - min;
        region.anchoredPosition = (min + max) * .5f - shop.rect.center;
        Set(data, "shopTarget", region);
        Set(data, "spotlightRoot", root);
        Set(data, "highlightFrame", root.Find("HighlightFrame"));
        var shades = data.FindProperty("shades");
        shades.arraySize = 4;
        for (int i = 0; i < 4; i++) shades.GetArrayElementAtIndex(i).objectReferenceValue = root.GetChild(i);
        data.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(false);
        panel.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        EditorSceneManager.SaveScene(menu.gameObject.scene);
    }

    private static void Set(SerializedObject owner, string name, UnityEngine.Object value)
    {
        owner.FindProperty(name).objectReferenceValue = value;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Image Fill(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI Text(Transform parent, string name, string value, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var text = Rect(parent, name, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.text = value;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void Cat(Transform parent, string name, Texture2D texture, Vector2 position)
    {
        // Use the complete PNG, independent of any sprite-sheet slicing settings.
        var image = Rect(parent, name, position, new Vector2(176, 176)).gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
    }
}
