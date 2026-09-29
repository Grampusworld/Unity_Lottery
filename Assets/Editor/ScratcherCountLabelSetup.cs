using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lottery.EditorTools
{
    /// <summary>
    /// 幂等装配自动刮彩票机下方的计数牌（机内票数 / 容量，格式 X/Y）。
    ///
    /// 两件东西：
    ///   ① Canvas/ScratcherCountText —— 屏幕空间 TMP 文本。**必须挂在主 Canvas 下**，
    ///      不能塞进 ShopPanel：那几块面板切页签时会 SetActive(false)，计数牌会被一起关掉；
    ///      而且它是世界物体（机器）的附属信息，跟哪一页签无关。
    ///      也**不做成机器的子物体**：机身 localScale=38、HoverJelly 还会按 x/y 不同幅度缩放，
    ///      子物体文本会被拉变形。位置由 ScratcherCountLabel 每帧投影现算。
    ///   ② Auto Lottery Machine 上的 ScratcherCountLabel —— 负责定位、显隐与数字/颜色。
    ///
    /// 文本从 GadgetsPanel 里现成的按钮 Label 克隆，而不是手搓一个新 TMP：
    /// 字体资源、图集材质、padding、字号全都一次对齐。手搓最容易漏的就是字体资源和
    /// m_sharedMaterial（漏了就是默认字体，像素风直接破功）。
    ///
    /// 幂等：重复点只会把参数改回目标值，按名字复用已有物体，不会重复创建。
    /// 菜单：Tools/挂个爽/一键装配刮票机计数牌
    /// </summary>
    public static class ScratcherCountLabelSetup
    {
        const string MenuPath = "Tools/挂个爽/一键装配刮票机计数牌";
        const string CanvasName = "Canvas";
        const string LabelName = "ScratcherCountText";
        const string PanelName = "GadgetsPanel";
        const string TemplateButtonName = "PurpleSpongeButton";
        const string TemplateLabelName = "Label";
        const string MachineName = "Auto Lottery Machine";
        const string FontAssetPath = "Assets/Fonts/PressStart2P-Regular.asset";

        /// <summary>字号必须是 8 的倍数（Press Start 2P 的原生网格就是 8px）。</summary>
        const float FontSize = 24f;
        const float LabelWidth = 240f;
        const float LabelHeight = 48f;
        /// <summary>锚点相对机身可见底边的额外下移量（世界单位），与组件默认值保持一致。</summary>
        const float BelowOffset = 2.2f;
        /// <summary>预览文案 "0/1" 的字符数，用来校验框宽够不够。</summary>
        const int PreviewChars = 4;

        static readonly Color TextColor = new Color32(246, 235, 205, 255);
        static readonly Color FullColor = new Color32(198, 158, 64, 255);

        [MenuItem(MenuPath, false, 20)]
        public static void Run()
        {
            GameObject canvasGo = GameObject.Find(CanvasName);
            if (canvasGo == null)
            {
                Debug.LogError("[ScratcherCountLabel] 场景里找不到 " + CanvasName);
                return;
            }
            GameObject machine = GameObject.Find(MachineName);
            if (machine == null)
            {
                Debug.LogError("[ScratcherCountLabel] 场景里找不到 " + MachineName +
                               "（先跑一次「一键装配自动刮彩票机」）");
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScratcherCountLabelSetup");

            // ---- ① 计数文本 --------------------------------------------------
            GameObject labelGo = EnsureLabelObject(canvasGo.transform);
            if (labelGo == null) return;

            RectTransform rect = (RectTransform)labelGo.transform;
            TMP_Text text = labelGo.GetComponent<TMP_Text>();
            ConfigureLabel(canvasGo.transform, rect, text);

            // ---- ② 机器上的组件 ----------------------------------------------
            ScratcherCountLabel label = machine.GetComponent<ScratcherCountLabel>();
            if (label == null) label = Undo.AddComponent<ScratcherCountLabel>(machine);

            Camera cam = Camera.main;
            var so = new SerializedObject(label);
            so.FindProperty("scratcher").objectReferenceValue = machine.GetComponent<AutoScratcher>();
            so.FindProperty("labelRect").objectReferenceValue = rect;
            so.FindProperty("labelCanvas").objectReferenceValue = canvasGo.GetComponent<Canvas>();
            so.FindProperty("labelText").objectReferenceValue = text;
            so.FindProperty("worldCamera").objectReferenceValue = cam;
            so.FindProperty("belowOffset").floatValue = BelowOffset;
            so.FindProperty("normalColor").colorValue = TextColor;
            so.FindProperty("fullColor").colorValue = FullColor;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(label);

            // ---- ③ 编辑器里先摆到目标位置（运行时由 LateUpdate 每帧重算）--------
            Vector3 anchorWorld = machine.transform.position;
            bool hasAnchor = TryContentBottom(machine.transform, out anchorWorld);
            anchorWorld -= Vector3.up * BelowOffset;
            string placement = "anchor=?";
            if (cam != null)
            {
                Vector3 screen = cam.WorldToScreenPoint(anchorWorld);
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        (RectTransform)canvasGo.transform, screen, null, out local))
                {
                    rect.anchoredPosition = local;
                    placement = "anchorWorld=" + anchorWorld.ToString("F3")
                                + " screen=" + screen.ToString("F1")
                                + " canvasLocal=" + local.ToString("F1");
                }
            }

            EditorUtility.SetDirty(rect);
            EditorUtility.SetDirty(labelGo);

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);

            // ---- ④ 回读校验 --------------------------------------------------
            Debug.Log("[ScratcherCountLabel] " + Report(label, labelGo, rect, text, cam, placement, hasAnchor));
        }

        // ---- 文本 --------------------------------------------------------------

        static GameObject EnsureLabelObject(Transform canvas)
        {
            Transform existing = canvas.Find(LabelName);
            if (existing != null) return existing.gameObject;

            TMP_Text template = FindTemplate(canvas);
            if (template == null)
            {
                Debug.LogError("[ScratcherCountLabel] 找不到克隆模板 " + PanelName + "/" + TemplateButtonName +
                               "/" + TemplateLabelName + "（GadgetsPanel 在 Tickets 页签下是隐藏的，已按包含隐藏物体的方式查找）");
                return null;
            }

            GameObject clone = (GameObject)Object.Instantiate(template.gameObject, canvas);
            clone.name = LabelName;
            Undo.RegisterCreatedObjectUndo(clone, LabelName);
            return clone;
        }

        static void ConfigureLabel(Transform canvas, RectTransform rect, TMP_Text text)
        {
            if (rect.parent != canvas) rect.SetParent(canvas, false);
            rect.SetAsLastSibling();                 // 画在所有面板之上
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(LabelWidth, LabelHeight);
            rect.localScale = Vector3.one;

            if (text == null) return;
            // 克隆来的那一个字体/材质已经是对的，别去动它；只有 AddComponent 出来的才需要补字体资源。
            if (text.font == null)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
                if (font != null) text.font = font;
                else Debug.LogWarning("[ScratcherCountLabel] 找不到字体资源：" + FontAssetPath);
            }
            text.fontSize = FontSize;
            text.enableAutoSizing = false;
            text.color = TextColor;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;   // 横向居中溢出，不会被框裁掉
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            text.raycastTarget = false;                        // 别去抢桌面上的拖拽输入
            text.text = "0/1";
            EditorUtility.SetDirty(text);
        }

        static TMP_Text FindTemplate(Transform canvas)
        {
            Transform[] all = canvas.GetComponentsInChildren<Transform>(true);
            Transform button = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == PanelName)
                {
                    Transform panel = all[i];
                    Transform[] children = panel.GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < children.Length; j++)
                        if (children[j].name == TemplateButtonName) { button = children[j]; break; }
                    break;
                }
            }
            if (button == null) return null;
            Transform label = button.Find(TemplateLabelName);
            return label != null ? label.GetComponent<TMP_Text>() : null;
        }

        // ---- 位置 --------------------------------------------------------------

        // 编辑器里拿不到 AutoScratcher.baseLossyScale（只在 Awake 里赋值），
        // 所以这里照 ScratcherSetup 的做法重算一遍：tiers[0].contentRect + sprite.pivot + lossyScale。
        static bool TryContentBottom(Transform machine, out Vector3 bottom)
        {
            bottom = machine != null ? machine.position : Vector3.zero;
            if (machine == null) return false;

            var scratcher = machine.GetComponent<AutoScratcher>();
            var body = machine.GetComponent<SpriteRenderer>();
            if (scratcher == null || body == null || body.sprite == null) return false;

            var so = new SerializedObject(scratcher);
            SerializedProperty tiers = so.FindProperty("tiers");
            if (tiers == null || tiers.arraySize == 0) return false;

            Vector4 rect = tiers.GetArrayElementAtIndex(0).FindPropertyRelative("contentRect").vector4Value;
            if (rect.z <= 0.5f || rect.w <= 0.5f) return false;

            Sprite sprite = body.sprite;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            Vector2 pivot = sprite.pivot;
            Vector3 scale = machine.lossyScale;
            float y0 = (rect.y - pivot.y) / ppu;
            bottom = new Vector3(machine.position.x, machine.position.y + y0 * scale.y, machine.position.z);
            return true;
        }

        // ---- 回读 --------------------------------------------------------------

        static string Report(ScratcherCountLabel label, GameObject labelGo, RectTransform rect,
            TMP_Text text, Camera cam, string placement, bool hasAnchor)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("label=").Append(labelGo.name)
              .Append(" parent=").Append(labelGo.transform.parent != null ? labelGo.transform.parent.name : "<root>")
              .Append(" active=").Append(labelGo.activeSelf)
              .Append(" anchors=").Append(rect.anchorMin).Append("->").Append(rect.anchorMax)
              .Append(" pivot=").Append(rect.pivot)
              .Append(" size=").Append(rect.sizeDelta)
              .Append(" pos=").Append(rect.anchoredPosition.ToString("F1"))
              .Append(" | tmp=").Append(text == null ? "<null>" :
                  (text.font != null ? text.font.name : "<nofont>")
                  + " fs" + text.fontSize
                  + " color" + ((Color32)text.color)
                  + " wrap" + text.textWrappingMode
                  + " raycast" + text.raycastTarget
                  + " txt[" + text.text + "]")
              .Append(" fitsChars=").Append(LabelWidth / FontSize).Append(" need").Append(PreviewChars)
              .Append(" | refs:");

            var so = new SerializedObject(label);
            string[] fields = { "scratcher", "labelRect", "labelCanvas", "labelText", "worldCamera" };
            for (int i = 0; i < fields.Length; i++)
            {
                SerializedProperty prop = so.FindProperty(fields[i]);
                string value = prop == null || prop.objectReferenceValue == null
                    ? "<null>" : prop.objectReferenceValue.name;
                sb.Append(' ').Append(fields[i]).Append('=').Append(value);
            }
            SerializedProperty offset = so.FindProperty("belowOffset");
            sb.Append(" belowOffset=").Append(offset != null ? offset.floatValue.ToString("F2") : "?");
            SerializedProperty normal = so.FindProperty("normalColor");
            SerializedProperty full = so.FindProperty("fullColor");
            sb.Append(" normal=").Append(normal != null ? ((Color32)normal.colorValue).ToString() : "?");
            sb.Append(" full=").Append(full != null ? ((Color32)full.colorValue).ToString() : "?");

            sb.Append(" | cam=").Append(cam != null ? cam.name : "<null>")
              .Append(" contentBottomOK=").Append(hasAnchor)
              .Append(" ").Append(placement);
            return sb.ToString();
        }
    }
}
