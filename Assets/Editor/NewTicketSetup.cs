using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 复用现有 Nova 预制体和按钮结构，装配三张多数字彩票。可重复执行，不改旧票配置。
public static class NewTicketSetup
{
    private static readonly string[] Names = { "HeartMatch", "CrossCode", "ZigzagRun" };
    private static readonly int[][] Pools = {
        LotteryEconomy.PrizePools[3], LotteryEconomy.PrizePools[4], LotteryEconomy.PrizePools[5]
    };
    private static readonly Vector2[][] Cells = {
        new[] { new Vector2(31,45), new Vector2(64,45), new Vector2(97,45) },
        new[] { new Vector2(64,44), new Vector2(29,28), new Vector2(99,28), new Vector2(29,60), new Vector2(99,60) },
        new[] { new Vector2(23,29), new Vector2(103,29), new Vector2(64,44), new Vector2(23,60), new Vector2(103,60) }
    };

    [MenuItem("Tools/Lottery/Set Up Three New Tickets")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before setting up tickets.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene first.");
        LotteryGame game = UnityEngine.Object.FindAnyObjectByType<LotteryGame>();
        if (game == null) throw new InvalidOperationException("LotteryGame was not found.");
        var gameData = new SerializedObject(game);
        Transform panel = gameData.FindProperty("ticketsPanel").objectReferenceValue is GameObject
            ? ((GameObject)gameData.FindProperty("ticketsPanel").objectReferenceValue).transform : null;
        if (panel == null) throw new InvalidOperationException("TicketsPanel is missing.");
        Button original = (Button)gameData.FindProperty("novaTicketButton").objectReferenceValue;
        if (original == null) throw new InvalidOperationException("Nova button is missing.");
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Add three tickets");
        Undo.RecordObject(game, "Wire new tickets");

        var content = panel.Find("Content") as RectTransform;
        if (content == null)
        {
            var obj = new GameObject("Content", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(obj, "Ticket scroll content");
            obj.transform.SetParent(panel, false);
            content = (RectTransform)obj.transform;
        }
        content.anchorMin = content.anchorMax = new Vector2(0,1);
        content.pivot = new Vector2(0,1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(((RectTransform)panel).rect.width, 1390);
        if (panel.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(panel.gameObject);
        var surface = panel.GetComponent<Image>();
        if (surface == null) { surface = Undo.AddComponent<Image>(panel.gameObject); surface.color = Color.clear; }
        surface.raycastTarget = true;
        var scroll = panel.GetComponent<PixelRowScroll>();
        if (scroll == null) scroll = Undo.AddComponent<PixelRowScroll>(panel.gameObject);
        var scrollData = new SerializedObject(scroll);
        scrollData.FindProperty("content").objectReferenceValue = content;
        scrollData.FindProperty("rowPitch").floatValue = 232;
        scrollData.FindProperty("rowHeight").floatValue = 210;
        scrollData.FindProperty("rowGap").floatValue = 22;
        scrollData.FindProperty("topMargin").floatValue = 10;
        scrollData.FindProperty("bottomMargin").floatValue = 10;
        scrollData.FindProperty("rowCount").intValue = 6;
        scrollData.ApplyModifiedProperties();
        scroll.ApplyLayout();

        string[] oldFields = { "luckyTicketButton", "goldTicketButton", "novaTicketButton" };
        for (int i = 0; i < 3; i++)
        {
            var button = (Button)gameData.FindProperty(oldFields[i]).objectReferenceValue;
            Undo.SetTransformParent(button.transform, content, "Ticket scroll content");
            LayoutRow((RectTransform)button.transform, i);
        }
        UnityAction[] actions = { game.NewHeartMatchTicket, game.NewCrossCodeTicket, game.NewZigzagRunTicket };
        for (int i = 0; i < Names.Length; i++)
        {
            LotteryTicket prefab = MakePrefab(i);
            Transform existing = content.Find(Names[i] + "TicketButton");
            Button button;
            if (existing == null)
            {
                var obj = UnityEngine.Object.Instantiate(original.gameObject, content, false);
                obj.name = Names[i] + "TicketButton";
                Undo.RegisterCreatedObjectUndo(obj, "New ticket button");
                button = obj.GetComponent<Button>();
            }
            else button = existing.GetComponent<Button>();
            LayoutRow((RectTransform)button.transform, i + 3);
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, actions[i]);
            TMP_Text label = button.transform.Find("Label").GetComponent<TMP_Text>();
            label.fontSize = 20;
            label.enableAutoSizing = false;
            string display = i == 0 ? "HEARTMATCH" : i == 1 ? "CROSSCODE" : "ZIGZAG";
            label.text = "UNLOCK " + display + "  $" + LotteryEconomy.UnlockPrices[i + 3];
            button.transform.Find("Details").GetComponent<TMP_Text>().text = "THEN $" + LotteryEconomy.TicketPrices[i + 3] + " / TICKET";
            string field = char.ToLowerInvariant(Names[i][0]) + Names[i].Substring(1);
            gameData.FindProperty(field + "TicketPrefab").objectReferenceValue = prefab;
            gameData.FindProperty(field + "TicketButton").objectReferenceValue = button;
            // Nova 的进度条结构随按钮复制，引用明确指向本按钮的 Fill。
            Image originalFill = (Image)gameData.FindProperty("novaProgressFill").objectReferenceValue;
            string relative = AnimationUtility.CalculateTransformPath(originalFill.transform, original.transform);
            gameData.FindProperty(field + "ProgressFill").objectReferenceValue = button.transform.Find(relative).GetComponent<Image>();
            EditorUtility.SetDirty(button);
        }
        gameData.ApplyModifiedProperties();
        EditorUtility.SetDirty(game);
        Canvas.ForceUpdateCanvases();
        TicketButtonIconSetup.Run();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[NewTicketSetup] Six ticket rows and three multi-number prefabs are configured.");
    }

    private static void LayoutRow(RectTransform row, int index)
    {
        row.anchorMin = row.anchorMax = new Vector2(0,1);
        row.pivot = new Vector2(.5f,.5f);
        row.sizeDelta = new Vector2(550,210);
        row.anchoredPosition = new Vector2(295,-115-index*232);
        row.localScale = Vector3.one;
    }

    private static LotteryTicket MakePrefab(int index)
    {
        string name = Names[index];
        string dir = "Assets/Lotteries/" + name + "Ticket/";
        Import(dir + name + "_Base.png", false);
        Import(dir + name + "_Cover.png", true);
        string path = "Assets/Prefabs/" + name + "Ticket_Base.prefab";
        string source = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null ? path : "Assets/Prefabs/NovaTicket_Base.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(source);
        try
        {
            root.name = name + "Ticket_Base";
            var ticket = root.GetComponent<LotteryTicket>();
            var data = new SerializedObject(ticket);
            data.FindProperty("kind").intValue = index + 3;
            var prizes = data.FindProperty("prizes");
            prizes.arraySize = Pools[index].Length;
            for (int j = 0; j < Pools[index].Length; j++) prizes.GetArrayElementAtIndex(j).intValue = Pools[index][j];
            root.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(dir + name + "_Base.png");
            var cover = root.GetComponentInChildren<ScratchCard>(true);
            cover.name = name + "Ticket_Cover";
            cover.transform.localPosition = Vector3.zero;
            cover.transform.localScale = Vector3.one;
            cover.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(dir + name + "_Cover.png");
            var coverData = new SerializedObject(cover);
            var calls = coverData.FindProperty("onRevealed.m_PersistentCalls.m_Calls");
            // 克隆来的事件应仍指向本预制体的 LotteryTicket；明确重连避免错误引用。
            for (int j = 0; j < calls.arraySize; j++) calls.GetArrayElementAtIndex(j).FindPropertyRelative("m_Target").objectReferenceValue = ticket;
            coverData.ApplyModifiedPropertiesWithoutUndo();

            var footer = (TextMeshPro)data.FindProperty("prizeText").objectReferenceValue;
            ConfigureText(footer, new Vector2(64,73), 4, Color.white);
            footer.text = index == 0 ? "PAIR $200 / TRIPLE $600" : index == 1 ? "MATCH CENTER = $" + LotteryEconomy.CrossMatchPrize : "ASCEND = $" + LotteryEconomy.PrizePools[5][3];
            var texts = data.FindProperty("numberTexts");
            texts.arraySize = Cells[index].Length;
            for (int j = 0; j < Cells[index].Length; j++)
            {
                string childName = "Number" + j;
                Transform child = root.transform.Find(childName);
                TextMeshPro text;
                if (child == null)
                {
                    var obj = UnityEngine.Object.Instantiate(footer.gameObject, root.transform, false);
                    obj.name = childName;
                    text = obj.GetComponent<TextMeshPro>();
                }
                else text = child.GetComponent<TextMeshPro>();
                Color ink = index == 0 ? new Color32(107,36,55,255) : index == 1 ? new Color32(219,255,231,255) : new Color32(13,51,61,255);
                ConfigureText(text, Cells[index][j], 8, ink);
                text.text = "00";
                texts.GetArrayElementAtIndex(j).objectReferenceValue = text;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<LotteryTicket>();
    }

    private static void ConfigureText(TextMeshPro text, Vector2 pixel, float size, Color color)
    {
        text.transform.localPosition = new Vector3((pixel.x-64)/100f,(40-pixel.y)/100f,0);
        text.transform.localScale = new Vector3(.1f,.1f,1);
        text.rectTransform.sizeDelta = new Vector2(13,1.3f);
        text.margin = Vector4.zero;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.horizontalAlignment = HorizontalAlignmentOptions.Center;
        text.verticalAlignment = VerticalAlignmentOptions.Geometry;
        text.color = color;
        text.GetComponent<MeshRenderer>().sortingOrder = 1;
    }

    private static void Import(string path, bool readable)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing texture: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.isReadable = readable;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(.5f,.5f);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }
}
