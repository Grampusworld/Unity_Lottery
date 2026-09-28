using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 给三个出票按钮的左侧加对应彩票素材图标，并让「图标 + 文字」整组居中。
// 菜单：Tools/挂个爽/给票按钮加图标
//
// 布局（三个按钮共用同一套数字，改一处即可整体生效）：
//   Label 保持**全宽拉伸 + 左右各内缩 0**，Center 对齐。文字不靠挪 rect 定位，
//   而是由 TicketButtonIconLayout 写 TMP 的 margin 把文本块整体右移 30px
//   （= (iconWidth 48 + gap 12) / 2），这样文字中心恒为「按钮中心 + 30」。
//   图标中心 = 按钮中心 − (gap + 文案宽) / 2，随文案长度实时变。
//   两条加起来 → 无论文案多长，「图标 + 文字」都作为一个整体居中，间距恒为 12px。
//
//   旧方案是把 Label 左内缩推到 60 给图标留固定列，文字在剩余 482px 里居中：
//   短文案（NEW LUCKY  $10 = 264px）时图标离文字约 110px，看着断开。已废弃。
//
// 图标用**点锚点** (0, 0.5) 而不是竖直拉伸锚点：
//   拉伸锚点下 sizeDelta 是「相对锚区的高度差」，要 30px 高就得写 -180，
//   而且按钮高度一改图标就跟着被拉伸。点锚点下 sizeDelta 就是真实尺寸。
//   点锚点还让 anchoredPosition.x 直接等于「图标中心距按钮左边缘」，方便布局组件算。
//
// 幂等：按名字查找复用已有的 Icon 子物体，重复点只改参数、不重复创建。
public static class TicketButtonIconSetup
{
    private const string MenuRoot = "Tools/挂个爽/";
    private const string IconName = "Icon";

    private static readonly string[] ButtonNames = { "LuckyTicketButton", "GoldTicketButton", "NovaTicketButton" };

    private const float IconWidth = 48f;
    private const float IconHeight = 30f;
    private const float IconGap = 12f;        // 图标右边缘 → 文字左边缘
    private const float IconRowY = 43f;       // 与 Label 主标题行同高
    private const float SideMargin = 6f;      // 整组距按钮左右的最小留白

    // 回读校验用的真实运行时文案（取自 LotteryGame.TicketLabel）：
    //   unlocked ? "NEW <NAME>  $<price>" : "UNLOCK <NAME>  $(kind==1 ? 100 : 1000)"
    private static readonly string[] LuckySamples = { "NEW LUCKY  $10" };
    private static readonly string[] GoldSamples = { "NEW GOLD  $50", "UNLOCK GOLD  $100" };
    private static readonly string[] NovaSamples = { "NEW NOVA  $50", "UNLOCK NOVA  $1000" };

    [MenuItem(MenuRoot + "给票按钮加图标", false, 12)]
    public static void Run()
    {
        int done = 0;
        var report = new System.Text.StringBuilder();

        for (int i = 0; i < ButtonNames.Length; i++)
        {
            GameObject button = FindByName(ButtonNames[i]);
            if (button == null)
            {
                Debug.LogWarning($"[TicketIcon] 场景里找不到 {ButtonNames[i]}");
                continue;
            }

            string kind = ButtonNames[i].Replace("TicketButton", "Ticket");
            string path = "Assets/Lotteries/" + kind + "/" + kind + "_Base.png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[TicketIcon] 找不到票面素材：{path}");
                continue;
            }

            Undo.RegisterFullObjectHierarchyUndo(button, "Ticket icon");

            // 1) 图标（位置由 TicketButtonIconLayout 每帧写，这里只给个初值）
            Transform existing = button.transform.Find(IconName);
            GameObject iconObject;
            if (existing == null)
            {
                iconObject = new GameObject(IconName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(iconObject, IconName);
                iconObject.transform.SetParent(button.transform, false);
            }
            else
            {
                iconObject = existing.gameObject;
            }

            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(IconWidth, IconHeight);
            iconRect.anchoredPosition = new Vector2(SideMargin + IconWidth * 0.5f, IconRowY);
            iconRect.localScale = Vector3.one;
            iconRect.localRotation = Quaternion.identity;

            var icon = iconObject.GetComponent<Image>();
            if (icon == null) icon = Undo.AddComponent<Image>(iconObject);
            icon.sprite = sprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;   // 图标 48×30 vs 素材 128×80，比例正好 1.6，无实际作用但保险
            icon.color = Color.white;
            icon.raycastTarget = false;   // 不抢按钮的射线，点击/悬停判定仍落在按钮本体上
            EditorUtility.SetDirty(icon);

            // 2) Label：回到全宽拉伸（左右内缩都是 0），水平位移交给 margin。
            Transform labelTransform = button.transform.Find("Label");
            var labelRect = labelTransform as RectTransform;
            if (labelRect == null)
            {
                Debug.LogWarning($"[TicketIcon] {ButtonNames[i]} 下找不到 Label");
            }
            else
            {
                Vector2 min = labelRect.offsetMin;
                Vector2 max = labelRect.offsetMax;
                min.x = 0f;
                max.x = 0f;
                labelRect.offsetMin = min;
                labelRect.offsetMax = max;
                EditorUtility.SetDirty(labelRect);
            }

            // 3) 布局组件：挂在按钮根上，优先复用已有实例（新加的字段有 C# 默认值，
            //    旧实例上不存在的字段 Unity 会直接用字段初始化器，不用担心丢默认值）。
            var layout = button.GetComponent<TicketButtonIconLayout>();
            if (layout == null) layout = Undo.AddComponent<TicketButtonIconLayout>(button);

            // 强制刷一次画布布局：刚改完 Label 的 offset，不刷的话下面 Apply() 读到的
            // rect.width 可能还是 0，会直接早退、位置不生效（踩过）。
            Canvas.ForceUpdateCanvases();

            WriteLayout(layout, iconRect, labelRect != null ? labelRect.GetComponent<TMP_Text>() : null);
            EditorUtility.SetDirty(layout);

            EditorUtility.SetDirty(iconObject);
            done++;
            report.Append(ButtonNames[i]).Append("->").Append(sprite.name).Append("  ");
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);

        // 先量再存：SaveScene 必须在 Verify() 之后。
        // 否则存盘时上面那些 Apply() 可能因画布布局还没刷新（rect.width=0）全部早退，
        // 落盘的 icon 位置是初值，和组件自己算出来的对不上（踩过）。
        Debug.Log($"[TicketIcon] 完成 {done}/3 个按钮 | {report}| {Verify()}");

        EditorSceneManager.SaveScene(scene);
    }

    // 用 SerializedObject 写，避免直接改 C# 字段时序列化值不同步（本项目踩过两次）。
    private static void WriteLayout(TicketButtonIconLayout layout, RectTransform icon, TMP_Text label)
    {
        var so = new SerializedObject(layout);
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("iconWidth").floatValue = IconWidth;
        so.FindProperty("iconGap").floatValue = IconGap;
        so.FindProperty("iconRowY").floatValue = IconRowY;
        so.FindProperty("sideMargin").floatValue = SideMargin;
        so.FindProperty("layoutActive").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        layout.Apply();
    }

    // 回读校验：把每个按钮的每一条运行时文案都跑一遍布局，量真实渲染出的
    // 图标中心 / 文字中心 / 文字宽 / 图标–文字间距，确认「整组居中 + 间距恒定」。
    private static string Verify()
    {
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < ButtonNames.Length; i++)
        {
            GameObject button = FindByName(ButtonNames[i]);
            if (button == null) continue;

            string[] samples = i == 0 ? LuckySamples : i == 1 ? GoldSamples : NovaSamples;
            for (int s = 0; s < samples.Length; s++)
            {
                if (s > 0) sb.Append("  ");
                sb.Append(button.name.Replace("TicketButton", "")).Append(':');
                sb.Append(Probe(button, samples[s]));
            }
            sb.Append(" | ");   // 不能换行：Unity 控制台只显示多行日志的第一行
        }

        return sb.ToString();
    }

    private static string Probe(GameObject button, string sample)
    {
        Transform labelTransform = button.transform.Find("Label");
        Transform iconTransform = button.transform.Find(IconName);
        if (labelTransform == null || iconTransform == null) return "(missing)";

        var text = labelTransform.GetComponent<TMP_Text>();
        var iconRect = (RectTransform)iconTransform;
        var layout = button.GetComponent<TicketButtonIconLayout>();
        if (text == null || layout == null) return "(missing)";

        string original = text.text;
        text.text = sample;
        layout.Apply();
        text.ForceMeshUpdate();

        float half = ((RectTransform)button.transform).rect.width * 0.5f;
        float iconCenter = iconRect.anchoredPosition.x - half;
        Bounds bounds = text.textBounds;

        // textBounds 原点在文本矩形中心，所以 center.x 就是「文字中心距按钮中心」。
        // 整组居中 ⇒ iconCenter 与 textCenter 关于 0 对称的量应该是
        //   textCenter = (iconW + gap) / 2 = 30
        //   iconCenter = -(gap + textW) / 2
        // 间距 = 文字左边缘 − 图标右边缘。
        float gap = (bounds.center.x - bounds.size.x * 0.5f) -
                    (iconCenter + IconWidth * 0.5f);
        string result = string.Format("iconC={0:F1} textC={1:F1} textW={2:F0} gap={3:F1}",
            iconCenter, bounds.center.x, bounds.size.x, gap);

        text.text = original;
        layout.Apply();
        text.ForceMeshUpdate();
        return result;
    }

    // 面板可能是 inactive 的，GameObject.Find 找不到 → 连 inactive 一起搜。
    private static GameObject FindByName(string name)
    {
        var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include);
        foreach (Button button in buttons)
            if (button != null && button.name == name) return button.gameObject;
        return null;
    }
}
