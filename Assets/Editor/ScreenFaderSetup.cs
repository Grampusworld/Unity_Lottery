using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 装配 ScreenFader（主菜单进游戏的淡入淡出遮罩）。
//
// 幂等：已存在就直接返回，不动任何引用 —— 复跑不会覆盖手动编辑。
// 遮罩做成独立的常驻根对象（不是 MainMenuCanvas 的子物体）：
// New Game 走 SceneManager.LoadScene，主菜单画布会被整个销毁，
// 而转场恰恰需要遮罩活着盖住黑屏停留阶段。
public static class ScreenFaderSetup
{
    private const string RootName = "ScreenFader";

    [MenuItem("Tools/Lottery/Add Screen Transition Fader")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Exit Play mode first.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene first.");

        var existing = UnityEngine.Object.FindAnyObjectByType<ScreenFader>();
        if (existing != null)
        {
            Debug.Log("LOTTERY MANIA: ScreenFader already present; nothing changed.");
            return;
        }

        var canvasObject = new GameObject("ScreenFaderCanvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 高于主菜单的 100，确保盖住菜单与游戏 HUD。
        canvas.sortingOrder = 200;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        var rt = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rt.transform.SetParent(canvasObject.transform, false);
        var rect = rt.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        var image = rt.GetComponent<Image>();
        image.color = new Color32(0, 0, 0, 255);
        image.raycastTarget = false;

        var fader = canvasObject.AddComponent<ScreenFader>();
        var data = new SerializedObject(fader);
        var prop = data.FindProperty("overlay");
        if (prop == null) throw new InvalidOperationException("ScreenFader.overlay field missing.");
        prop.objectReferenceValue = image;
        data.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("LOTTERY MANIA: ScreenFader added (Sorting Order 200, DontDestroyOnLoad).");
    }
}