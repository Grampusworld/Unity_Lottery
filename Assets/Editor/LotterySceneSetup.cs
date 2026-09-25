using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// One-time, reviewable scene setup. Existing ticket prefabs and their button listeners are kept.
public static class LotterySceneSetup
{
    private static readonly Color32 Navy = new Color32(20, 27, 43, 255);
    private static readonly Color32 Card = new Color32(28, 44, 69, 255);
    private static readonly Color32 Cream = new Color32(246, 235, 205, 255);
    private static readonly Color32 Gold = new Color32(234, 179, 76, 255);
    private static TMP_FontAsset font;

    [MenuItem("Tools/Lottery/Apply Dishwashing Shop")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open Assets/Scenes/SampleScene.unity first.");
        if (GameObject.Find("ShopPanel") != null)
        {
            Debug.LogWarning("Lottery shop already exists; setup skipped to protect current edits.");
            return;
        }

        var gameObject = GameObject.Find("GameManager");
        var canvasObject = GameObject.Find("Canvas");
        var table = GameObject.Find("Table");
        var ticketSpawn = GameObject.Find("TicketSpawnPoint");
        if (gameObject == null || canvasObject == null || table == null || ticketSpawn == null)
            throw new InvalidOperationException("Required existing scene objects were not found.");
        var game = gameObject.GetComponent<LotteryGame>();
        if (game == null) throw new InvalidOperationException("GameManager has no LotteryGame component.");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PressStart2P-Regular.asset");
        if (font == null) throw new InvalidOperationException("Pixel TMP font asset is missing.");

        const string root = "Assets/Materials/";
        string platePath = root + "Plate/Dirty Plate - 2 Layers.png";
        string dirtPath = root + "Plate/Dirty Plate - 2 Layers (1).png";
        string yellowPath = root + "Sponges/Dish Sponge - Yellow.png";
        string purplePath = root + "Sponges/Dish Sponge - Advanced Purple.png";
        string idlePath = root + "Automatic Dish Washer/Automatic Dish Washer - Idle & Washing.png";
        string workPath = root + "Automatic Dish Washer/Automatic Dish Washer - Idle & Washing (1).png";
        foreach (string path in new[] { platePath, dirtPath, yellowPath, purplePath, idlePath, workPath })
            ConfigureTexture(path, path == dirtPath);
        Sprite cleanSprite = SpriteAt(platePath), dirtSprite = SpriteAt(dirtPath);
        Sprite yellowSprite = SpriteAt(yellowPath), purpleSprite = SpriteAt(purplePath);
        Sprite idleSprite = SpriteAt(idlePath), workSprite = SpriteAt(workPath);

        // The camera already frames roughly 178x100 world units. Reserve its left edge for UI.
        // The source PNG has wide transparent margins around the painted tabletop.
        table.transform.position = new Vector3(70f, -50f, 0f);
        table.transform.localScale = new Vector3(140f, 160f, 1f);
        ticketSpawn.transform.position = new Vector3(42f, 18f, 0f);
        var plateSpawn = new GameObject("PlateSpawnPoint").transform;
        plateSpawn.position = new Vector3(20f, -27f, 0f);

        var platePrefab = CreatePlatePrefab(cleanSprite, dirtSprite);
        var sponge = new GameObject("Dish Sponge - Yellow");
        sponge.transform.position = new Vector3(49f, -34f, 0f);
        sponge.transform.localScale = new Vector3(18f, 18f, 1f);
        var spongeRenderer = sponge.AddComponent<SpriteRenderer>();
        spongeRenderer.sprite = yellowSprite;
        spongeRenderer.sortingOrder = 5;
        var spongeDrag = sponge.AddComponent<SpongeDrag>();
        Assign(spongeDrag, "yellowSprite", yellowSprite);
        Assign(spongeDrag, "purpleSprite", purpleSprite);
        Assign(spongeDrag, "inputCamera", Camera.main);

        var washerObject = new GameObject("Automatic Dish Washer");
        washerObject.transform.position = new Vector3(78f, -27f, 0f);
        washerObject.transform.localScale = new Vector3(20f, 20f, 1f);
        var washerRenderer = washerObject.AddComponent<SpriteRenderer>();
        washerRenderer.sprite = idleSprite;
        washerRenderer.sortingOrder = 4;
        var washer = washerObject.AddComponent<AutomaticDishWasher>();
        Assign(washer, "idleSprite", idleSprite);
        Assign(washer, "workingSprite", workSprite);

        var canvas = canvasObject.GetComponent<Canvas>();
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1440);
        scaler.matchWidthOrHeight = 0.5f;
        var shop = Rect(canvas.transform, "ShopPanel", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, .5f), new Vector2(650, 0), Vector2.zero);
        AddImage(shop.gameObject, Navy);
        var trim = TopRect(shop, "AccentStrip", 638, 0, 12, 1440);
        AddImage(trim.gameObject, new Color32(73, 153, 158, 255));
        Text(shop, "Title", "TICKETS & GADGETS", 25, 28, 590, 55, 30, Gold);

        var money = GameObject.Find("MoneyText").GetComponent<TextMeshProUGUI>();
        money.transform.SetParent(shop, false);
        SetTop(money.rectTransform, 25, 97, 590, 76);
        money.transform.localScale = Vector3.one;
        money.font = font;
        money.fontSize = 37;
        money.color = Cream;
        money.alignment = TextAlignmentOptions.MidlineLeft;
        money.text = "MONEY  $0";

        var ticketsTab = NewButton(shop, "TicketsTab", "TICKETS", 25, 185, 285, 78, new Color32(49, 111, 133, 255), 29);
        var gadgetsTab = NewButton(shop, "GadgetsTab", "GADGETS", 330, 185, 285, 78, Card, 29);
        UnityEventTools.AddPersistentListener(ticketsTab.onClick, game.ShowTickets);
        UnityEventTools.AddPersistentListener(gadgetsTab.onClick, game.ShowGadgets);
        var ticketsPanel = TopRect(shop, "TicketsPanel", 30, 285, 590, 915);
        var gadgetsPanel = TopRect(shop, "GadgetsPanel", 30, 285, 590, 915);

        var lucky = ExistingTicketButton("LuckyTicketButton", ticketsPanel, 0);
        var goldButton = ExistingTicketButton("GoldTicketButton", ticketsPanel, 1);
        var novaButton = ExistingTicketButton("NovaTicketButton", ticketsPanel, 2);
        var luckyBar = ProgressBar(lucky.transform);
        var goldBar = ProgressBar(goldButton.transform);
        var novaBar = ProgressBar(novaButton.transform);

        var purpleButton = NewButton(gadgetsPanel, "PurpleSpongeButton", "PURPLE SPONGE  $30", 0, 0, 590, 190, Card, 25);
        var washerButton = NewButton(gadgetsPanel, "WasherUnlockButton", "UNLOCK WASHER  $200", 0, 215, 590, 190, Card, 25);
        var speedButton = NewButton(gadgetsPanel, "SpeedUpgradeButton", "SPEED +  $1", 0, 430, 590, 190, Card, 25);
        var capacityButton = NewButton(gadgetsPanel, "CapacityUpgradeButton", "CAPACITY +  $1", 0, 645, 590, 190, Card, 25);
        UnityEventTools.AddPersistentListener(purpleButton.onClick, game.BuyPurpleSponge);
        UnityEventTools.AddPersistentListener(washerButton.onClick, game.BuyWasher);
        UnityEventTools.AddPersistentListener(speedButton.onClick, game.UpgradeSpeed);
        UnityEventTools.AddPersistentListener(capacityButton.onClick, game.UpgradeCapacity);

        var plateButton = NewButton(shop, "OneMorePlateButton", "ONE MORE PLATE  +$1", 30, 1230, 590, 125, new Color32(151, 95, 48, 255), 28);
        UnityEventTools.AddPersistentListener(plateButton.onClick, game.OneMorePlate);
        Text(shop, "DebugHint", "F1  DEBUG MENU", 30, 1370, 590, 44, 19, new Color32(132, 155, 164, 255));
        var debugPanel = DebugPanel(canvas.transform, game);
        debugPanel.SetActive(false);
        gadgetsPanel.gameObject.SetActive(false);

        var serialized = new SerializedObject(game);
        Put(serialized, "luckyProgressFill", luckyBar);
        Put(serialized, "goldProgressFill", goldBar);
        Put(serialized, "novaProgressFill", novaBar);
        Put(serialized, "platePrefab", platePrefab);
        Put(serialized, "plateSpawnPoint", plateSpawn);
        Put(serialized, "sponge", spongeDrag);
        Put(serialized, "washer", washer);
        Put(serialized, "plateButton", plateButton);
        Put(serialized, "purpleSpongeButton", purpleButton);
        Put(serialized, "washerUnlockButton", washerButton);
        Put(serialized, "speedUpgradeButton", speedButton);
        Put(serialized, "capacityUpgradeButton", capacityButton);
        Put(serialized, "ticketsPanel", ticketsPanel.gameObject);
        Put(serialized, "gadgetsPanel", gadgetsPanel.gameObject);
        Put(serialized, "ticketsTabButton", ticketsTab);
        Put(serialized, "gadgetsTabButton", gadgetsTab);
        Put(serialized, "debugPanel", debugPanel);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Lottery shop, dish cleaning and washer scene setup completed.");
    }

    private static void ConfigureTexture(string path, bool readable)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing texture: " + path);
        if (importer.isReadable == readable && importer.filterMode == FilterMode.Point) return;
        importer.isReadable = readable;
        importer.filterMode = FilterMode.Point;
        importer.SaveAndReimport();
    }

    private static Sprite SpriteAt(string path)
    {
        var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (sprite == null) throw new InvalidOperationException("No Sprite imported from: " + path);
        return sprite;
    }

    private static DirtyPlate CreatePlatePrefab(Sprite clean, Sprite dirt)
    {
        const string path = "Assets/Prefabs/DirtyPlate.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<DirtyPlate>(path);
        if (existing != null) return existing;
        var root = new GameObject("Dirty Plate");
        root.transform.localScale = new Vector3(30f, 30f, 1f);
        var baseRenderer = root.AddComponent<SpriteRenderer>();
        baseRenderer.sprite = clean;
        baseRenderer.sortingOrder = 2;
        var dirtObject = new GameObject("Dirt - separate layer");
        dirtObject.transform.SetParent(root.transform, false);
        var dirtRenderer = dirtObject.AddComponent<SpriteRenderer>();
        dirtRenderer.sprite = dirt;
        dirtRenderer.sortingOrder = 3;
        var plate = root.AddComponent<DirtyPlate>();
        Assign(plate, "dirtRenderer", dirtRenderer);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<DirtyPlate>();
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static void Assign(UnityEngine.Object owner, string property, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(owner);
        Put(serialized, property, value);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void Put(SerializedObject serialized, string property, UnityEngine.Object value)
    {
        var field = serialized.FindProperty(property);
        if (field == null) throw new InvalidOperationException("Serialized field missing: " + property);
        field.objectReferenceValue = value;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = pos;
        return rt;
    }
    private static RectTransform TopRect(Transform parent, string name, float x, float top, float width, float height)
    {
        return Rect(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(width, height), new Vector2(x, -top));
    }
    private static void SetTop(RectTransform rt, float x, float top, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(x, -top);
    }
    private static Image AddImage(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.sprite = null;
        image.color = color;
        return image;
    }
    private static TextMeshProUGUI Text(Transform parent, string name, string value, float x, float top, float width, float height, float size, Color color)
    {
        var rt = TopRect(parent, name, x, top, width, height);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.text = value;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }
    private static Button NewButton(Transform parent, string name, string label, float x, float top, float width, float height, Color color, float fontSize)
    {
        var rt = TopRect(parent, name, x, top, width, height);
        var image = AddImage(rt.gameObject, color);
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var text = Text(rt, "Label", label, 12, 12, width - 24, height - 24, fontSize, Cream);
        text.textWrappingMode = TextWrappingModes.Normal;
        return button;
    }
    private static Button ExistingTicketButton(string name, Transform panel, int index)
    {
        var go = GameObject.Find(name);
        if (go == null) throw new InvalidOperationException("Missing ticket button: " + name);
        go.transform.SetParent(panel, false);
        go.transform.localScale = Vector3.one;
        SetTop(go.GetComponent<RectTransform>(), 0, index * 230, 590, 210);
        var image = go.GetComponent<Image>();
        image.sprite = null;
        image.color = Card;
        image.type = Image.Type.Simple;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        Text(go.transform, "Label", name, 14, 22, 562, 70, 29, Cream);
        Text(go.transform, "Details", "", 14, 105, 562, 50, 18, Gold);
        return button;
    }
    private static Image ProgressBar(Transform parent)
    {
        var background = TopRect(parent, "ProgressBackground", 20, 180, 550, 13);
        AddImage(background.gameObject, new Color32(17, 28, 35, 255));
        var fill = Rect(background, "ProgressFill", Vector2.zero, new Vector2(0, 1), new Vector2(0, .5f), Vector2.zero, Vector2.zero);
        var image = AddImage(fill.gameObject, new Color32(94, 189, 119, 255));
        return image;
    }
    private static GameObject DebugPanel(Transform canvas, LotteryGame game)
    {
        var rt = Rect(canvas, "DebugPanel", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(960, 1020), Vector2.zero);
        AddImage(rt.gameObject, new Color32(17, 25, 38, 250));
        Text(rt, "DebugTitle", "DEBUG MENU  (F1)", 20, 20, 920, 70, 34, Gold);
        AddDebug(rt, game, "AddMoney", "+ $1000", 110, game.DebugAddMoney);
        AddDebug(rt, game, "UnlockAll", "UNLOCK ALL", 220, game.DebugUnlockAll);
        AddDebug(rt, game, "SpeedUp", "SPEED +", 330, game.DebugSpeedUp, 25);
        AddDebug(rt, game, "SpeedDown", "SPEED -", 330, game.DebugSpeedDown, 495);
        AddDebug(rt, game, "CapacityUp", "CAPACITY +", 440, game.DebugCapacityUp, 25);
        AddDebug(rt, game, "CapacityDown", "CAPACITY -", 440, game.DebugCapacityDown, 495);
        AddDebug(rt, game, "ResetSave", "RESET SAVE", 570, game.DebugResetProgress);
        AddDebug(rt, game, "Close", "CLOSE", 700, game.ToggleDebugMenu);
        Text(rt, "DebugNote", "MILESTONES: 10 / 25 / 50", 25, 860, 910, 70, 23, Cream);
        return rt.gameObject;
    }
    private static void AddDebug(Transform panel, LotteryGame game, string name, string label, float top, UnityAction callback, float x = 25)
    {
        float width = x > 25 ? 440 : (name.Contains("Up") || name.Contains("Down") ? 440 : 910);
        var button = NewButton(panel, name, label, x, top, width, 90, Card, 26);
        UnityEventTools.AddPersistentListener(button.onClick, callback);
    }
}
