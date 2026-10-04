using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 只迁移这次新增的首行与桌面边界，不重装机器、素材或既有商店。
public static class TableValueSetup
{
    [MenuItem("Tools/Lottery/Apply Table Bounds And Plate Value")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open SampleScene first.");
        LotteryGame game = UnityEngine.Object.FindAnyObjectByType<LotteryGame>();
        if (game == null) throw new InvalidOperationException("LotteryGame is missing.");
        var gameData = new SerializedObject(game);
        GameObject panel = (GameObject)gameData.FindProperty("gadgetsPanel").objectReferenceValue;
        RectTransform shop = panel.transform.parent as RectTransform;
        PixelRowScroll scroll = panel.GetComponent<PixelRowScroll>();
        var scrollData = new SerializedObject(scroll);
        RectTransform content = (RectTransform)scrollData.FindProperty("content").objectReferenceValue;
        Undo.RegisterFullObjectHierarchyUndo(shop.gameObject, "Plate value first row");
        Transform row = content.Find("PlateValueButton");
        if (row == null)
        {
            GameObject template = content.Find("PurpleSpongeButton").gameObject;
            GameObject clone = UnityEngine.Object.Instantiate(template, content);
            clone.name = "PlateValueButton";
            Undo.RegisterCreatedObjectUndo(clone, "Plate value button");
            row = clone.transform;
        }
        row.SetAsFirstSibling();
        row.gameObject.SetActive(true);
        Button button = row.GetComponent<Button>();
        var buttonData = new SerializedObject(button);
        buttonData.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").ClearArray();
        buttonData.ApplyModifiedProperties();
        UnityEventTools.AddVoidPersistentListener(button.onClick, game.UpgradePlateValue);
        gameData.FindProperty("plateValueButton").objectReferenceValue = button;
        gameData.ApplyModifiedProperties();
        string[] rowNames = { "PlateValueButton", "MultiplePlatesButton", "PurpleSpongeButton", "WasherUnlockButton",
            "SpeedUpgradeButton", "CapacityUpgradeButton", "ScratcherUnlockButton", "ScratcherSpeedButton", "ScratcherCapacityButton" };
        for (int i = 0; i < rowNames.Length; i++)
        {
            var rect = content.Find(rowNames[i]) as RectTransform;
            if (rect == null) throw new InvalidOperationException("Missing row " + rowNames[i]);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -64f - i * 130f);
        }
        scrollData.FindProperty("rowCount").intValue = rowNames.Length;
        scrollData.ApplyModifiedProperties();
        scroll.ApplyLayout();

        SpongeDrag sponge = (SpongeDrag)gameData.FindProperty("sponge").objectReferenceValue;
        Rect interior = new Rect(63f, 133f, 89f, 47f); // Table.png 黑色内框之间的橙色像素区。
        var spongeData = new SerializedObject(sponge);
        spongeData.FindProperty("tabletopPixels").rectValue = interior;
        spongeData.FindProperty("shopPanel").objectReferenceValue = shop;
        spongeData.ApplyModifiedProperties();
        foreach (var drag in UnityEngine.Object.FindObjectsByType<MachineDrag>(FindObjectsInactive.Include))
        {
            var data = new SerializedObject(drag);
            data.FindProperty("tabletopPixels").rectValue = interior;
            data.FindProperty("shopPanel").objectReferenceValue = shop;
            data.FindProperty("edgeMargin").floatValue = HoverJelly.MaxScale;
            data.ApplyModifiedProperties();
        }
        foreach (var feeder in UnityEngine.Object.FindObjectsByType<WasherPlateFeeder>(FindObjectsInactive.Include))
        {
            var data = new SerializedObject(feeder);
            data.FindProperty("window").objectReferenceValue = feeder.GetComponentInChildren<WasherWaterEffect>(true);
            data.ApplyModifiedProperties();
        }
        typeof(LotteryGame).GetMethod("RefreshUI", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[TableValueSetup] Plate value row, inner tabletop bounds and washer window saved.");
    }
}
