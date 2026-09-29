#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// 给三个彩票 Prefab 的**根节点（base）**挂悬停果冻，并把入场动画 / 拖拽组件指过去。
// 菜单：Tools/挂个爽/给彩票挂悬停果冻（priority 22）
//
// ── 为什么只挂根节点，不给 cover 单独挂 ──────────────────────────────────
// cover 是根的子物体（`XxxTicket_Cover` 的 m_Father 就是根 Transform），而父级的
// localScale 对子物体是**统一个乘子**：cover 的世界缩放 = root 的 s × cover 自己的
// (1.02, 1.06)。所以：
//   · 只要写 localScale 的只有一个源（root），两层永远同倍缩放，封面「比票面大 2%/6%」
//     这个相对关系被精确保持 —— 数学上不可能露馅。
//   · 反过来，「base 放大而 cover 不放大」在现有层级下**做不出来**：给 cover 单独挂果冻，
//     它写的是自己的 local，父级放大照样继承。真会出现的两个症状是：
//       ① 指针压在封面上时两个果冻同时命中（票的 sprite.bounds 天然含封面）→ 复合放大 + 弹簧错拍；
//       ② 指针移出封面那一帧 cover 复位（欠阻尼还会先冲到 0.99）→ 封面边缘在票面印刷上蹭动。
//   · 真会「露出下层内容」只有一个成因：把 cover 挪出这条父子链（为了做独立命中区挂到
//     scale=1 的容器下），或给 cover 写 1/s 反向缩放。
// 要「分区域手感」就在 cover 上放一个**命中代理**去调 root 的 Pulse，绝不新增第二个缩放写者。
//
// ── 幅度 ────────────────────────────────────────────────────────────────
// 按屏幕像素标定，全场统一 22px：amp = clamp(22 / 宽度px, 0.02, 0.18)。
// 票可见宽 = 1.28 × 48.61111 = 62.222 世界单位；2560×1440 / ortho50 → 14.4 px/世界单位
// → 896.0px → 22 / 896 = 0.0246。（海绵 0.1732 = 22/127px、脏盘子 0.0909 = 22/241.9px
// 是同一套标定，不是「相对比例」。）
//
// 幂等：重复点只会把参数改回目标值，按类型复用已有组件，不会重复挂。
// 全部用 SerializedObject 回写：项目里踩过「改 C# 默认值对 Prefab / 场景已有实例无效」的坑。
public static class TicketJellySetup
{
    const string MenuPath = "Tools/挂个爽/给彩票挂悬停果冻";
    const string Tag = "[TicketJelly] ";

    // 幅度见文件头推算。squashRatio 必须 0：>0 会让上下几乎不动，要四向一致只能是 0。
    const float JellyAmplitude = 0.0246f;

    static readonly string[] Prefabs =
    {
        "Assets/Prefabs/LuckyTicket_Base.prefab",
        "Assets/Prefabs/GoldTicket_Base.prefab",
        "Assets/Prefabs/NovaTicket_Base.prefab",
    };

    [MenuItem(MenuPath, false, 22)]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError(Tag + "Play 模式下不能改 Prefab（编译会退出 Play 并丢掉改动），先停 Play。");
            return;
        }

        int configured = 0;
        for (int i = 0; i < Prefabs.Length; i++)
            if (Configure(Prefabs[i])) configured++;

        Debug.Log(Tag + "完成 | 装配 " + configured + "/" + Prefabs.Length + " 个票 Prefab");
        for (int i = 0; i < Prefabs.Length; i++) Verify(Prefabs[i]);
    }

    static bool Configure(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogError(Tag + "打不开 Prefab：" + path);
            return false;
        }

        try
        {
            SpriteRenderer body = root.GetComponent<SpriteRenderer>();
            TicketFlyIn fly = root.GetComponent<TicketFlyIn>();
            TicketDragger dragger = root.GetComponent<TicketDragger>();
            if (body == null || fly == null || dragger == null)
            {
                Debug.LogError(Tag + path + " 根节点缺组件（SpriteRenderer=" + (body != null)
                    + " TicketFlyIn=" + (fly != null) + " TicketDragger=" + (dragger != null) + "）");
                return false;
            }

            HoverJelly jelly = root.GetComponent<HoverJelly>();
            if (jelly == null) jelly = root.AddComponent<HoverJelly>();

            SerializedObject jellySo = new SerializedObject(jelly);
            WriteEnum(jellySo, "hitSource", 1);          // WorldBounds：不加 Collider2D、不走射线
            SetRef(jellySo, "worldRenderer", body);      // 命中区 = 整张票的 sprite.bounds（含封面）
            SetRef(jellySo, "worldCamera", null);        // 留空 → 运行时用 Camera.main
            WriteFloat(jellySo, "amplitude", JellyAmplitude);
            WriteFloat(jellySo, "squashRatio", 0f);
            WriteFloat(jellySo, "settleTime", 0.22f);
            WriteFloat(jellySo, "releaseTime", 0.16f);
            WriteFloat(jellySo, "damping", 0.32f);
            WriteFloat(jellySo, "verticalLag", 0.78f);
            // 只缩底图不缩文字：PrizeText 是 TMP 子节点，反向缩放让它保持 1.0 采样不被拉伸糊掉。
            // 它跟着票中心走（pivot 居中），所以不会因为票放大而从封面边缘露出来。
            WriteBool(jellySo, "counterScaleText", true);
            WriteBool(jellySo, "pressPulse", true);
            WriteFloat(jellySo, "pressPulseStrength", 1.8f);
            WriteFloat(jellySo, "pressPulseDuration", 0.1f);
            jellySo.ApplyModifiedPropertiesWithoutUndo();

            // 两个「写 localScale」的组件都必须拿到同一个果冻引用：它们在各自独占期把它挂起/交还。
            SerializedObject flySo = new SerializedObject(fly);
            SetRef(flySo, "jelly", jelly);
            flySo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject dragSo = new SerializedObject(dragger);
            SetRef(dragSo, "jelly", jelly);
            dragSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 读回确认 —— 只写不读等于没验证。顺带守住那条不变量：cover 必须还在根的子物体链上。
    static void Verify(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogError(Tag + "读回失败：" + path);
            return;
        }

        try
        {
            HoverJelly jelly = root.GetComponent<HoverJelly>();
            TicketFlyIn fly = root.GetComponent<TicketFlyIn>();
            TicketDragger dragger = root.GetComponent<TicketDragger>();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(Tag).Append(root.name).Append(" | jelly=");

            if (jelly == null) sb.Append("<null>");
            else
            {
                SerializedObject so = new SerializedObject(jelly);
                sb.Append("ok hitSource=").Append(so.FindProperty("hitSource").enumValueIndex)
                  .Append(" enable=").Append(jelly.enabled)
                  .Append(" worldRenderer=").Append(so.FindProperty("worldRenderer").objectReferenceValue == null ? "<null>" : "ok")
                  .Append(" amp=").Append(so.FindProperty("amplitude").floatValue.ToString("F4"))
                  .Append(" squash=").Append(so.FindProperty("squashRatio").floatValue.ToString("F2"))
                  .Append(" damping=").Append(so.FindProperty("damping").floatValue.ToString("F2"))
                  .Append(" counterText=").Append(so.FindProperty("counterScaleText").boolValue);
            }

            SerializedObject flySo = new SerializedObject(fly);
            SerializedObject dragSo = new SerializedObject(dragger);
            sb.Append(" | flyIn.jelly=").Append(Name(flySo.FindProperty("jelly")))
              .Append(" dragger.jelly=").Append(Name(dragSo.FindProperty("jelly")));

            // 层级守卫：cover 必须挂在根下面。一旦被挪走，「父级统一乘子」这个前提就没了。
            Transform cover = FindCover(root.transform);
            if (cover == null) sb.Append(" | cover=<找不到>");
            else
            {
                Vector3 s = cover.localScale;
                sb.Append(" | cover 子物体=").Append(cover.parent == root.transform)
                  .Append(" localScale=(").Append(s.x.ToString("F2")).Append(",")
                  .Append(s.y.ToString("F2")).Append(",").Append(s.z.ToString("F0")).Append(")");
            }

            Debug.Log(sb.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindCover(Transform root)
    {
        ScratchCard[] cards = root.GetComponentsInChildren<ScratchCard>(true);
        return cards.Length > 0 ? cards[0].transform : null;
    }

    static string Name(SerializedProperty property)
    {
        if (property == null) return "<字段不存在>";
        return property.objectReferenceValue == null ? "<null>" : property.objectReferenceValue.name;
    }

    static void SetRef(SerializedObject so, string field, Object value)
    {
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning(Tag + "字段不存在：" + field);
            return;
        }
        property.objectReferenceValue = value;
    }

    static void WriteFloat(SerializedObject so, string field, float value)
    {
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning(Tag + "字段不存在：" + field);
            return;
        }
        property.floatValue = value;
    }

    static void WriteBool(SerializedObject so, string field, bool value)
    {
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning(Tag + "字段不存在：" + field);
            return;
        }
        property.boolValue = value;
    }

    static void WriteEnum(SerializedObject so, string field, int index)
    {
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning(Tag + "字段不存在：" + field);
            return;
        }
        property.enumValueIndex = index;
    }
}
#endif
