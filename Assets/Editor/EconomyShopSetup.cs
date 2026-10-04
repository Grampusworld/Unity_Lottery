using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 将公共经济表写入既有六张票，并为两个 PixelRowScroll 装配可见滚动条。
public static class EconomyShopSetup
{
    [MenuItem("Tools/Lottery/Apply Economy And Scrollbars")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene first.");
        var game = UnityEngine.Object.FindAnyObjectByType<LotteryGame>();
        if (game == null) throw new InvalidOperationException("LotteryGame is missing.");
        var data = new SerializedObject(game);
        string[] fields = { "lucky", "gold", "nova", "heartMatch", "crossCode", "zigzagRun" };
        for (int i = 0; i < fields.Length; i++)
        {
            var ticket = (LotteryTicket)data.FindProperty(fields[i] + "TicketPrefab").objectReferenceValue;
            if (ticket == null) throw new InvalidOperationException("Missing prefab: " + fields[i]);
            string path = AssetDatabase.GetAssetPath(ticket);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var config = new SerializedObject(root.GetComponent<LotteryTicket>());
                var pool = config.FindProperty("prizes");
                pool.arraySize = LotteryEconomy.PrizePools[i].Length;
                for (int j = 0; j < pool.arraySize; j++) pool.GetArrayElementAtIndex(j).intValue = LotteryEconomy.PrizePools[i][j];
                config.ApplyModifiedPropertiesWithoutUndo();
                var footer = (TextMeshPro)config.FindProperty("prizeText").objectReferenceValue;
                if (i == 3) footer.text = "PAIR $" + LotteryEconomy.HeartPairPrize + " / TRIPLE $" + LotteryEconomy.HeartTriplePrize;
                if (i == 4) footer.text = "MATCH CENTER = $" + LotteryEconomy.CrossMatchPrize;
                if (i == 5) footer.text = "ASCEND = $" + LotteryEconomy.PrizePools[5][3];
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        ConfigureScrollbar((GameObject)data.FindProperty("ticketsPanel").objectReferenceValue, "TicketsScrollbar", false);
        ConfigureScrollbar((GameObject)data.FindProperty("gadgetsPanel").objectReferenceValue, "GadgetsScrollbar", true);
        // 复用运行时格式生成预览，避免再维护一套价格字符串。不写 PlayerPrefs。
        typeof(LotteryGame).GetMethod("RefreshUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        Canvas.ForceUpdateCanvases();
        TicketButtonIconSetup.Run();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[EconomyShopSetup] Six prize pools, shop prices and two scrollbars saved.");
    }

    private static void ConfigureScrollbar(GameObject panel, string name, bool gadgets)
    {
        if (panel == null) throw new InvalidOperationException("Shop panel is missing.");
        var viewport = (RectTransform)panel.transform;
        Undo.RegisterFullObjectHierarchyUndo(viewport.parent.gameObject, "Add shop scrollbar");
        // 给条留出真实的可点击宽度，Gadgets 原先已占到 x=635。
        if (gadgets) viewport.anchoredPosition = new Vector2(5, viewport.anchoredPosition.y);
        Transform found = viewport.parent.Find(name);
        GameObject root = found != null ? found.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        if (found == null) { root.transform.SetParent(viewport.parent, false); Undo.RegisterCreatedObjectUndo(root, "Shop scrollbar"); }
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0,1);
        rect.pivot = new Vector2(.5f,1);
        rect.sizeDelta = new Vector2(14, viewport.rect.height);
        // x：636 → 628（2026-10-01 左移 8px）。636 时滑块的右边缘紧贴 ShopPanel 右缘那条
        // 青色描边（实测 x644..649 = RGB(73,153,158)），旧滑块也是青色（84,164,173），两者几乎同色
        // → 滚动条和面板边框糊成一条，看不出滚动条在哪。左移 8px 留出一条与描边同宽的缝。
        rect.anchoredPosition = new Vector2(628, viewport.anchoredPosition.y);
        rect.localScale = Vector3.one;
        var track = root.GetComponent<Image>();
        track.color = new Color32(13,24,36,255);
        track.raycastTarget = true;
        var outline = root.GetComponent<Outline>();
        if (outline == null) outline = root.AddComponent<Outline>();
        // 轨道描边改成金色（2026-10-01）：整条滚动条走全场的「金＝重要/可交互」语言，
        // 深色凹槽 + 金描边 + 金滑块，在深蓝面板上对比明确。
        outline.effectColor = new Color32(198,158,64,255);
        outline.effectDistance = new Vector2(1,-1);
        var area = Child(root.transform, "SlidingArea", false);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(3,3); area.offsetMax = new Vector2(-3,-3);
        var handle = Child(area, "Handle", true);
        handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one;
        handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
        var image = handle.GetComponent<Image>();
        // 滑块：青色 (84,164,173) → 金色 (230,180,70)。青色和面板右缘那条青色描边同色系，
        // 金色是全场最亮最暖的一档（满级金 / 烟花金），一眼就能锁定滚动条位置。
        image.color = new Color32(230,180,70,255);
        image.raycastTarget = true;
        var bar = root.GetComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.numberOfSteps = 0;
        bar.handleRect = handle;
        bar.targetGraphic = image;
        bar.transition = Selectable.Transition.ColorTint;
        var colors = bar.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(255,224,132,255);
        colors.pressedColor = new Color32(255,203,88,255);
        colors.selectedColor = Color.white;
        bar.colors = colors;
        var nav = bar.navigation; nav.mode = Navigation.Mode.None; bar.navigation = nav;
        var scroll = panel.GetComponent<PixelRowScroll>();
        if (scroll == null) throw new InvalidOperationException("PixelRowScroll is missing.");
        var config = new SerializedObject(scroll);
        config.FindProperty("scrollbar").objectReferenceValue = bar;
        config.ApplyModifiedProperties();
        scroll.ApplyLayout();
        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(scroll);
    }

    private static RectTransform Child(Transform parent, string name, bool image)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var obj = image ? new GameObject(name, typeof(RectTransform), typeof(Image)) : new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            child = obj.transform;
        }
        child.localScale = Vector3.one;
        return (RectTransform)child;
    }
}
