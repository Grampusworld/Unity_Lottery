#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 给脏盘子 Prefab 一次挂上飞入动画与悬停果冻。
// 菜单：Tools/挂个爽/给脏盘子挂飞入动画与悬停果冻
//
// 幂等：重复点只会把参数改回目标值，按名字复用已有组件，不会重复挂。
//
// 全部用 SerializedObject 回写：项目里踩过三次「改 C# 默认值对 Prefab / 场景已有实例无效」的坑
// （LotteryTicket.prizes、HoverJelly 参数、洗盘机调参），所以参数一律显式回写并读回确认。
public static class PlateFlyInSetup
{
    private const string PrefabPath = "Assets/Prefabs/DirtyPlate.prefab";

    // 桌面内表面（世界坐标）。2560x1440 相机 ortho 50 下逐像素量出来的：
    // 沿行找最长的橙色连续段 → 左内沿 x=-28.3 / 右内沿 x=96.3；
    // 沿列找 → 上内沿 y=37.8 / 下内沿 y=-37.3。两侧各留一点余量。
    private static readonly Rect TableArea = new Rect(-28.3f, -37.3f, 124.6f, 75.1f);

    // 幅度按屏幕像素标定，全场统一 22px：amp = clamp(22 / 宽度px, 0.02, 0.18)。
    // 盘子世界宽 16.8 → 16.8 / 177.78 * 2560 = 241.9px → 22 / 241.9 = 0.0909。
    // 和海绵那套（0.1732 = 22 / 127px）是同一个标定，不是缩小版的相对比例。
    private const float JellyAmplitude = 0.0909f;

    [MenuItem("Tools/挂个爽/给脏盘子挂飞入动画与悬停果冻", false, 13)]
    public static void Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[PlateFlyInSetup] 打不开 Prefab：" + PrefabPath);
            return;
        }

        try
        {
            if (root.GetComponent<DirtyPlate>() == null)
            {
                Debug.LogError("[PlateFlyInSetup] " + PrefabPath + " 根节点上没有 DirtyPlate");
                return;
            }

            HoverJelly jelly = root.GetComponent<HoverJelly>();
            if (jelly == null) jelly = root.AddComponent<HoverJelly>();
            SerializedObject jellySo = new SerializedObject(jelly);
            WriteInt(jellySo, "hitSource", 1);               // WorldBounds：不依赖 Collider2D / 射线
            WriteFloat(jellySo, "amplitude", JellyAmplitude);
            WriteFloat(jellySo, "squashRatio", 0f);          // >0 会让上下几乎不动，要四向一致必须 0
            WriteFloat(jellySo, "settleTime", 0.22f);
            WriteFloat(jellySo, "releaseTime", 0.16f);
            WriteFloat(jellySo, "damping", 0.32f);
            WriteFloat(jellySo, "verticalLag", 0.78f);
            jellySo.ApplyModifiedPropertiesWithoutUndo();

            PlateFlyIn flyIn = root.GetComponent<PlateFlyIn>();
            if (flyIn == null) flyIn = root.AddComponent<PlateFlyIn>();
            SerializedObject flySo = new SerializedObject(flyIn);
            WriteRect(flySo, "tableArea", TableArea);
            SerializedProperty jellyProp = flySo.FindProperty("jelly");
            if (jellyProp != null) jellyProp.objectReferenceValue = jelly;
            flySo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Verify();
    }

    // 读回确认 —— 只写不读等于没验证。
    private static void Verify()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("[PlateFlyInSetup] 读回失败：" + PrefabPath);
            return;
        }
        try
        {
            HoverJelly jelly = root.GetComponent<HoverJelly>();
            PlateFlyIn flyIn = root.GetComponent<PlateFlyIn>();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[PlateFlyInSetup] 读回 | jelly=").Append(jelly == null ? "<null>" : "ok");
            if (jelly != null)
            {
                SerializedObject so = new SerializedObject(jelly);
                sb.Append(" hitSource=").Append(so.FindProperty("hitSource").enumValueIndex)
                  .Append(" amp=").Append(so.FindProperty("amplitude").floatValue.ToString("F4"))
                  .Append(" squash=").Append(so.FindProperty("squashRatio").floatValue.ToString("F2"))
                  .Append(" damping=").Append(so.FindProperty("damping").floatValue.ToString("F2"));
            }
            sb.Append(" | flyIn=").Append(flyIn == null ? "<null>" : "ok");
            if (flyIn != null)
            {
                SerializedObject so = new SerializedObject(flyIn);
                SerializedProperty area = so.FindProperty("tableArea");
                SerializedProperty jellyRef = so.FindProperty("jelly");
                sb.Append(" tableArea=").Append(area.rectValue)
                  .Append(" jellyRef=").Append(jellyRef.objectReferenceValue == null ? "<null>" : jellyRef.objectReferenceValue.name)
                  .Append(" flight=").Append(so.FindProperty("flightTime").floatValue.ToString("F2"))
                  .Append("-").Append(so.FindProperty("minFlightTime").floatValue.ToString("F2"))
                  .Append("/").Append(so.FindProperty("maxFlightTime").floatValue.ToString("F2"));
            }
            Debug.Log(sb.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void WriteFloat(SerializedObject so, string name, float value)
    {
        SerializedProperty prop = so.FindProperty(name);
        if (prop == null)
        {
            Debug.LogWarning("[PlateFlyInSetup] 字段不存在：" + name);
            return;
        }
        prop.floatValue = value;
    }

    private static void WriteInt(SerializedObject so, string name, int value)
    {
        SerializedProperty prop = so.FindProperty(name);
        if (prop == null)
        {
            Debug.LogWarning("[PlateFlyInSetup] 字段不存在：" + name);
            return;
        }
        prop.enumValueIndex = value;
    }

    private static void WriteRect(SerializedObject so, string name, Rect value)
    {
        SerializedProperty prop = so.FindProperty(name);
        if (prop == null)
        {
            Debug.LogWarning("[PlateFlyInSetup] 字段不存在：" + name);
            return;
        }
        prop.rectValue = value;
    }

    // ---- 金币不足抖动 ------------------------------------------------------

    // MoneyShake 挂在 LotteryGame.balanceText 指的物体上，不额外连引用
    // （LotteryGame 里也是 balanceText.GetComponent<MoneyShake>() 自取）。
    [MenuItem("Tools/挂个爽/给余额文本挂金币抖动", false, 14)]
    public static void AttachMoneyShake()
    {
        LotteryGame game = Object.FindAnyObjectByType<LotteryGame>();
        if (game == null)
        {
            Debug.LogError("[PlateFlyInSetup] 场景里找不到 LotteryGame");
            return;
        }

        SerializedObject gameSo = new SerializedObject(game);
        SerializedProperty balanceProp = gameSo.FindProperty("balanceText");
        var balance = balanceProp != null ? balanceProp.objectReferenceValue as Component : null;
        if (balance == null)
        {
            Debug.LogError("[PlateFlyInSetup] LotteryGame.balanceText 还没连");
            return;
        }

        MoneyShake shake = balance.GetComponent<MoneyShake>();
        if (shake == null) shake = Undo.AddComponent<MoneyShake>(balance.gameObject);

        SerializedProperty shakeProp = gameSo.FindProperty("moneyShake");
        if (shakeProp != null)
        {
            shakeProp.objectReferenceValue = shake;
            gameSo.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(game);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        EditorSceneManager.SaveScene(game.gameObject.scene);

        SerializedObject so = new SerializedObject(shake);
        Debug.Log("[PlateFlyInSetup] 金币抖动已挂 | target=" + balance.name +
                  " duration=" + so.FindProperty("duration").floatValue.ToString("F2") +
                  " shiftTexels=" + so.FindProperty("shiftTexels").floatValue.ToString("F1") +
                  " shrink=" + so.FindProperty("shrinkRatio").floatValue.ToString("F2") +
                  " color=" + so.FindProperty("shakeColor").colorValue);
    }
}
#endif
