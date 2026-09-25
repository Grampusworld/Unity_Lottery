#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 一键给场景里的可交互元素挂上悬停果冻，省掉逐个拖拽。
// 新加的按钮只要再点一次这个菜单就会自动补挂。
public static class HoverJellySetup
{
    private const string MenuRoot = "Tools/挂个爽/";

    [MenuItem(MenuRoot + "给所有 Button 挂悬停果冻", false, 10)]
    public static void AttachToButtons()
    {
        int count = 0;
        int pivoted = 0;
        Button[] buttons = Object.FindObjectsByType<Button>();
        foreach (Button button in buttons)
        {
            if (button == null || button.GetComponent<HoverJelly>() != null) continue;
            Undo.AddComponent<HoverJelly>(button.gameObject);
            count++;

            // 关键：UI 的 localScale 是绕 pivot 缩放的。pivot 留在 (0,1)/(0,0) 这类角点时，
            // 果冻只会往右下（或右上）长，看起来像整体位移而不是中心向外的挤压。
            // 挂载时顺手把 pivot 归到中心，并按世界包围盒补偿位置，视觉上一像素不动。
            if (button.transform is RectTransform rect && CenterPivot(rect)) pivoted++;
        }
        Debug.Log($"[HoverJelly] 新挂 {count} 个 Button（场景内共 {buttons.Length} 个）；其中 {pivoted} 个 pivot 已归中心。");
    }

    // 把 RectTransform 的 pivot 归到中心，并补偿位置使矩形在世界空间原地不动。
    // 返回 true 表示确实改过。解析法给初值，再用世界包围盒做一次数值兜底
    // （拉伸锚点改 pivot 的位移语义和点锚点不同，不能只靠公式）。
    private static bool CenterPivot(RectTransform rect)
    {
        Vector2 delta = new Vector2(0.5f - rect.pivot.x, 0.5f - rect.pivot.y);
        if (delta.sqrMagnitude < 1e-12f) return false;

        Vector2 size = rect.rect.size;
        Vector3 scale = rect.localScale;
        float[] before = WorldBox(rect);

        Undo.RecordObject(rect, "Center Pivot For HoverJelly");
        Shift(rect, new Vector2(delta.x * size.x * scale.x, delta.y * size.y * scale.y));
        rect.pivot = new Vector2(0.5f, 0.5f);
        Canvas.ForceUpdateCanvases();

        Vector3 drift = new Vector3(WorldBox(rect)[0] - before[0], WorldBox(rect)[2] - before[2], 0f);
        if (drift.sqrMagnitude > 1e-6f)
        {
            Vector3 local = rect.parent != null ? rect.parent.InverseTransformVector(drift) : drift;
            Shift(rect, new Vector2(-local.x, -local.y));
            Canvas.ForceUpdateCanvases();
        }
        EditorUtility.SetDirty(rect);
        return true;
    }

    private static void Shift(RectTransform rect, Vector2 offset)
    {
        bool stretchX = !Mathf.Approximately(rect.anchorMin.x, rect.anchorMax.x);
        bool stretchY = !Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y);
        if (stretchX)
        {
            rect.offsetMin = new Vector2(rect.offsetMin.x + offset.x, rect.offsetMin.y);
            rect.offsetMax = new Vector2(rect.offsetMax.x + offset.x, rect.offsetMax.y);
        }
        else
        {
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x + offset.x, rect.anchoredPosition.y);
        }
        if (stretchY)
        {
            rect.offsetMin = new Vector2(rect.offsetMin.x, rect.offsetMin.y + offset.y);
            rect.offsetMax = new Vector2(rect.offsetMax.x, rect.offsetMax.y + offset.y);
        }
        else
        {
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y + offset.y);
        }
    }

    private static float[] WorldBox(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float x0 = Mathf.Min(Mathf.Min(corners[0].x, corners[1].x), Mathf.Min(corners[2].x, corners[3].x));
        float x1 = Mathf.Max(Mathf.Max(corners[0].x, corners[1].x), Mathf.Max(corners[2].x, corners[3].x));
        float y0 = Mathf.Min(Mathf.Min(corners[0].y, corners[1].y), Mathf.Min(corners[2].y, corners[3].y));
        float y1 = Mathf.Max(Mathf.Max(corners[0].y, corners[1].y), Mathf.Max(corners[2].y, corners[3].y));
        return new[] { x0, x1, y0, y1 };
    }

    [MenuItem(MenuRoot + "给海绵挂悬停果冻（World 模式）", false, 11)]
    public static void AttachToSponge()
    {
        int count = 0;
        SpongeDrag[] sponges = Object.FindObjectsByType<SpongeDrag>();
        foreach (SpongeDrag sponge in sponges)
        {
            if (sponge == null) continue;
            HoverJelly jelly = sponge.GetComponent<HoverJelly>();
            if (jelly == null) jelly = Undo.AddComponent<HoverJelly>(sponge.gameObject);
            SerializedObject so = new SerializedObject(jelly);
            SerializedProperty source = so.FindProperty("hitSource");
            if (source != null && source.enumValueIndex != 1)
            {
                source.enumValueIndex = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            count++;
        }
        Debug.Log($"[HoverJelly] 海绵已设为 WorldBounds 模式：{count} 个。");
    }

    [MenuItem(MenuRoot + "一键挂载悬停果冻（Button + 海绵）", false, 1)]
    public static void AttachAll()
    {
        AttachToButtons();
        AttachToSponge();
    }
}
#endif
