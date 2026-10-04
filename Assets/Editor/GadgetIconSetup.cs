using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 给 GADGETS 十行加左侧图标，并把 Label 让出图标列。
// 菜单：Tools/挂个爽/给 Gadgets 行加左侧图标
//
// ==================== 布局 ====================
// 图标槽 64×64，按钮 590×108。图标左边距 4 → 占 x[4,68]，Label 左内缩 74（留 6px 缝）。
// 文字区 = 590 − 74 − 12 = 504px。按住 Start 2P 等宽（字宽 = 字号），
// 全场最长文案 `UNLOCK SCRATCHER  $20,851` = 25 字符；MoneyFormat 的金额串恒 ≤ 7 字符，
// 所以 500px 是**稳定上界**而不是巧合。504 − 500 = 4px 余量（每侧 2px），不碰 mask。
//
// 字号 24 → 20：fs24 下最长文案 600px 已经超 566px 可用宽度 34px（每侧 17px），
// 加图标列会变成 41px —— 会被 GadgetsPanel 的 RectMask2D 切掉价格数字。
// 20 已在场景里用了 12 处（票面 Details / Debug 面板），不是新字号。
//
// ==================== 素材 ====================
// 零新增美术素材。十个图标全部由既有素材拼：
//   Plate.png（40×32）· Dish Sponge - Advanced Purple（50×30）·
//   Automatic Dish Washer - Machine（114×88）· Auto Lottery Machine V1/V2/V3（48×61 / 54×63 / 46×54）·
//   LuckyTicket_Base（128×80）
// 只有两张 8×9 / 9×8 的小箭头是脚本画的（绿 94,189,119 = 票面进度条同色），
// 走的是 WasherSetup 重画进度环同一套路：Texture2D → EncodeToPNG → 写盘 → 改导入设置。
//
// ==================== 缩放 ====================
// 所有缩放都是 1/2、1/4、1/8 的**整数比**，Point 采样下像素格不会错位：
// 盘子 40×32 ÷2=20×16、洗盘机 114×88 ÷2=57×44、票 128×80 ÷4=32×20 / ÷8=16×10。
// 机身一律 ÷1：V1 48×61、V2 54×63、V3 46×54 全部塞得进 64×64，
// 非整数缩小会让 1px 描边变成半像素、看着发糊（MEMORY.md 的像素取证底色那一套）。
public static class GadgetIconSetup
{
    private const string MenuRoot = "Tools/挂个爽/";
    private const string IconName = "Icon";
    private const string ArrowUpPath = "Assets/Materials/UI/GadgetArrowUp.png";
    private const string ArrowRightPath = "Assets/Materials/UI/GadgetArrowRight.png";

    // 图标槽与 Label 让位（见文件头布局段）
    private const float IconSlot = 64f;
    private const float IconLeft = 4f;
    private const float LabelInset = 74f;
    private const float LabelInsetY = 12f;
    private const int LabelFontSize = 20;

    // 绿箭头：与 LotterySceneSetup 里票面进度条同色（94,189,119）
    private static readonly Color ArrowGreen = new Color32(94, 189, 119, 255);

    private const string PlatePath = "Assets/Materials/Automatic Dish Washer/Plate.png";
    private const string SpongePath = "Assets/Materials/Sponges/Dish Sponge - Advanced Purple.png";
    private const string WasherPath = "Assets/Materials/Automatic Dish Washer/Automatic Dish Washer - Machine.png";
    private const string TicketPath = "Assets/Lotteries/LuckyTicket/LuckyTicket_Base.png";
    private static readonly string[] ScratcherPaths =
    {
        "Assets/Materials/Auto Lottery Machine/Auto Lottery Machine V3 - Retro CRT.png",
        "Assets/Materials/Auto Lottery Machine/Auto Lottery Machine V1 - Classic Blue.png",
        "Assets/Materials/Auto Lottery Machine/Auto Lottery Machine V2 - Gold Deluxe.png",
    };

    // 每行：按钮名 / 基础图案 / 槽位（图案路径 + 整数缩放分母 + 排布）
    private enum Layout { Row, Grid2x2, Grid3x2, Grid3x2Tall, RowRight, Pair }

    private class RowSpec
    {
        public string button;
        public string basePath;        // 基础图案（可为 null → 只有槽位）
        public int baseDiv;
        public string slotPath;
        public int slotDiv;
        public Layout layout;
        public int slotCount;
        public Vector2 basePos;        // 相对图标槽中心
        public float slotY;
        public float slotStep;         // 槽位中心间距（行内）/ 列距（网格）
    }

    // 槽位数 = 满级 + 1（各链 costs 数组长度 + 1），所以「点亮数 = 等级 + 1」
    // 在满级时必然等于槽位数 —— 见 GadgetRowIcon.IsFullyLit。
    private static RowSpec[] Specs()
    {
        return new[]
        {
            // PLATE VALUE：盘子 + 5 段上升箭头（lv 0..4）
            new RowSpec { button = "PlateValueButton", basePath = PlatePath, baseDiv = 1, basePos = new Vector2(0f, 16f),
                          slotPath = ArrowUpPath, slotDiv = 1, layout = Layout.Row, slotCount = 5, slotY = -20f, slotStep = 13f },
            // MULTIPLE PLATES：2×2 盘子（lv 0..3）
            new RowSpec { button = "MultiplePlatesButton", slotPath = PlatePath, slotDiv = 2,
                          layout = Layout.Grid2x2, slotCount = 4, slotStep = 22f },
            // 一次性购买：静态图标，无槽位
            new RowSpec { button = "PurpleSpongeButton", basePath = SpongePath, baseDiv = 1 },
            new RowSpec { button = "WasherUnlockButton", basePath = WasherPath, baseDiv = 2 },
            // WASHER SPEED：6 道速度箭头（lv 0..5）
            new RowSpec { button = "SpeedUpgradeButton", slotPath = ArrowRightPath, slotDiv = 1,
                          layout = Layout.RowRight, slotCount = 6, slotStep = 10f },
            // WASHER CAPACITY：5 个盘子（lv 0..4），3+2
            new RowSpec { button = "CapacityUpgradeButton", slotPath = PlatePath, slotDiv = 2,
                          layout = Layout.Grid3x2, slotCount = 5, slotY = 9f, slotStep = 22f },
            // SCRATCHER 解锁：机身（运行时按 tier 换 V3/V1/V2）
            new RowSpec { button = "ScratcherUnlockButton", basePath = ScratcherPaths[0], baseDiv = 1 },
            // SCRATCHER SPEED：6 道速度箭头（lv 0..5）
            new RowSpec { button = "ScratcherSpeedButton", slotPath = ArrowRightPath, slotDiv = 1,
                          layout = Layout.RowRight, slotCount = 6, slotStep = 10f },
            // SCRATCHER CAPACITY：6 张票（lv 0..5），3+3
            new RowSpec { button = "ScratcherCapacityButton", slotPath = TicketPath, slotDiv = 8,
                          layout = Layout.Grid3x2Tall, slotCount = 6, slotY = 6f, slotStep = 18f },
            // AUTO FEED：票 + 一支右箭头（静态）
            new RowSpec { button = "AutoFeedButton", basePath = TicketPath, baseDiv = 4, basePos = new Vector2(-14f, 0f),
                          slotPath = ArrowRightPath, slotDiv = 1, layout = Layout.Pair, slotCount = 1, slotY = 0f, slotStep = 0f },
        };
    }

    [MenuItem(MenuRoot + "给 Gadgets 行加左侧图标", false, 13)]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open SampleScene first.");

        WriteArrowSprites();
        AssetDatabase.Refresh();

        Sprite plate = LoadSprite(PlatePath);
        Sprite sponge = LoadSprite(SpongePath);
        Sprite washer = LoadSprite(WasherPath);
        Sprite ticket = LoadSprite(TicketPath);
        Sprite arrowUp = LoadSprite(ArrowUpPath);
        Sprite arrowRight = LoadSprite(ArrowRightPath);
        Sprite[] tiers = new Sprite[ScratcherPaths.Length];
        for (int i = 0; i < ScratcherPaths.Length; i++) tiers[i] = LoadSprite(ScratcherPaths[i]);

        var report = new StringBuilder();
        RowSpec[] specs = Specs();
        for (int i = 0; i < specs.Length; i++)
        {
            RowSpec spec = specs[i];
            Button button = FindButton(spec.button);
            if (button == null) { report.Append(spec.button).Append("=MISSING "); continue; }

            Sprite baseSprite = ResolveBase(spec, tiers);
            Sprite slotSprite = spec.slotPath == null ? null : ResolveSlot(spec);

            BuildRow(button, baseSprite, slotSprite, spec, report);
            report.Append(spec.button).Append("=").Append(spec.slotCount).Append("槽 ");
        }

        // 与 TicketButtonIconSetup 同样的顺序：先量再存。
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[GadgetIcon] 十行图标装配完成 | " + report);
    }

    // ---- 每行的构建（幂等）----------------------------------------------

    private static void BuildRow(Button button, Sprite baseSprite, Sprite slotSprite, RowSpec spec, StringBuilder report)
    {
        Undo.RegisterFullObjectHierarchyUndo(button.gameObject, "Gadget row icon");

        // 1) 图标槽（点锚点，sizeDelta 就是真实尺寸 —— 拉伸锚点下要写 -108 之类，不直观）
        RectTransform icon = EnsureChild(button.transform, IconName);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = new Vector2(IconSlot, IconSlot);
        icon.anchoredPosition = new Vector2(IconLeft + IconSlot * 0.5f, 0f);
        icon.localScale = Vector3.one;
        icon.localRotation = Quaternion.identity;

        // 2) 基础图案
        Image baseImage = EnsureImage(icon, "Base");
        if (baseSprite != null)
        {
            var rect = baseImage.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = SpriteSize(baseSprite, spec.baseDiv);
            rect.anchoredPosition = spec.basePos;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            baseImage.sprite = baseSprite;
            baseImage.type = Image.Type.Simple;
            baseImage.preserveAspect = false;   // 尺寸已经是精确整数倍，别再让引擎缩放
            baseImage.color = Color.white;
            baseImage.raycastTarget = false;
            baseImage.enabled = true;
        }
        else baseImage.enabled = false;

        // 3) 槽位：命名 Slot00.. 按阅读顺序，多跑一次只会覆写不会增生
        Image[] slots = new Image[spec.slotCount];
        for (int i = 0; i < spec.slotCount; i++)
        {
            Image slot = EnsureImage(icon, "Slot" + i.ToString("00"));
            var rect = slot.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = slotSprite != null ? SpriteSize(slotSprite, spec.slotDiv) : Vector2.zero;
            rect.anchoredPosition = SlotPosition(spec, i);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            slot.sprite = slotSprite;
            slot.type = Image.Type.Simple;
            slot.preserveAspect = false;
            slot.color = Color.white;
            slot.raycastTarget = false;
            slot.enabled = true;
            slots[i] = slot;
        }

        // 4) 组件字段（SerializedObject 回写 —— 直接改 C# 字段在已有场景上不生效）
        //
        // **SerializedObject 必须挂在 GadgetRowIcon 上，不能挂 Button**：
        // SerializedObject 只会列出「那个组件自己声明的」序列化字段。
        // 挂在 Button 上 FindProperty("baseArt") 永远返回 null，
        // 后面 driver.objectReferenceValue 就是 NullReferenceException（踩过）。
        var driver = button.GetComponent<GadgetRowIcon>();
        if (driver == null) driver = Undo.AddComponent<GadgetRowIcon>(button.gameObject);
        if (driver == null) { report.Append("(no driver)"); return; }

        var dso = new SerializedObject(driver);
        dso.FindProperty("baseArt").objectReferenceValue = baseImage;
        var slotArray = dso.FindProperty("slots");
        slotArray.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
            slotArray.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        // 场景里存的是「全新存档」那一档（等级 0 = 点亮 1 槽），运行时 RefreshUI 会立刻改写。
        dso.FindProperty("filled").intValue = slots.Length > 0 ? 1 : 0;
        dso.FindProperty("completed").boolValue = false;
        dso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(driver);

        // 5) Label：左内缩让出图标列 + 字号降到 20
        Transform labelObject = button.transform.Find("Label");
        if (labelObject == null) { report.Append("(no Label)"); return; }
        var label = labelObject as RectTransform;
        var tmp = labelObject.GetComponent<TMP_Text>();
        if (tmp == null) return;
        // offsetMin/offsetMax 是**拉伸锚点下的内缩量，正数=向内缩**：
        //   left = 父左边 + anchorMin.x*父宽 + offsetMin.x
        // 所以左边内缩 74 要写 **offsetMin.x = +74**、右边内缩 12 要写 **offsetMax.x = -12**。
        // 符号写反会把 Label 往左推 74px，正好压在图标上（2026-10-04 踩，
        // 截图里 "MULTIPLE PLATES" 的 M 被盘子盖住了）。
        // 佐证：场景里原本四边内缩 12 的写法是 sizeDelta(-24,-24)，即 offsetMin(+12,+12)/offsetMax(-12,-12)。
        label.offsetMin = new Vector2(LabelInset, LabelInsetY);
        label.offsetMax = new Vector2(-LabelInsetY, -LabelInsetY);
        EditorUtility.SetDirty(label);

        var tso = new SerializedObject(tmp);
        tso.FindProperty("m_fontSize").floatValue = LabelFontSize;
        tso.FindProperty("m_fontSizeBase").floatValue = LabelFontSize;
        tso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tmp);
    }

    // 槽位中心坐标。阅读顺序：先左后右、先上后下。
    private static Vector2 SlotPosition(RowSpec spec, int index)
    {
        switch (spec.layout)
        {
            case Layout.Row:      // 单行横排，5 支上箭头
            case Layout.RowRight: // 单行横排，6 支右箭头
                return new Vector2((index - (spec.slotCount - 1) * 0.5f) * spec.slotStep, spec.slotY);
            case Layout.Grid2x2:
                return new Vector2((index % 2 == 0 ? -1f : 1f) * spec.slotStep * 0.5f,
                                   (index < 2 ? 1f : -1f) * spec.slotStep * 0.5f);
            case Layout.Grid3x2:      // 3 + 2（下行居中）
            {
                bool top = index < 3;
                int col = top ? index : index - 3;
                int cols = top ? 3 : 2;
                return new Vector2((col - (cols - 1) * 0.5f) * spec.slotStep, top ? spec.slotY : -spec.slotY);
            }
            case Layout.Grid3x2Tall:  // 3 + 3
                return new Vector2((index % 3 - 1f) * spec.slotStep, index < 3 ? spec.slotY : -spec.slotY);
            case Layout.Pair:         // 基础图案右侧一支箭头
                return new Vector2(20f, spec.slotY);
            default:
                return Vector2.zero;
        }
    }

    private static Sprite ResolveBase(RowSpec spec, Sprite[] tiers)
    {
        if (spec.button == "ScratcherUnlockButton") return tiers[0];
        return spec.basePath == null ? null : LoadSprite(spec.basePath);
    }

    private static Sprite ResolveSlot(RowSpec spec)
    {
        if (spec.slotPath == ArrowUpPath) return LoadSprite(ArrowUpPath);
        if (spec.slotPath == ArrowRightPath) return LoadSprite(ArrowRightPath);
        return LoadSprite(spec.slotPath);
    }

    private static Vector2 SpriteSize(Sprite sprite, int divisor)
    {
        Vector2 size = sprite.rect.size;
        float d = Mathf.Max(1, divisor);
        // 整数缩放：四舍五入到整数倍，宁可少 1px 也不要半像素位置。
        return new Vector2(Mathf.RoundToInt(size.x / d), Mathf.RoundToInt(size.y / d));
    }

    // ---- 绿箭头贴图（脚本生成，字节相同则不重写 → 复跑菜单 0 差异）--------

    private static void WriteArrowSprites()
    {
        WriteArrow(ArrowUpPath, 8, 9, UpArrowPixels());
        WriteArrow(ArrowRightPath, 9, 8, RightArrowPixels());
    }

    // 上箭头 8×9：箭头 4 行（宽 2/4/6/8）+ 竖干 5 行（宽 2）
    private static bool[,] UpArrowPixels()
    {
        bool[,] px = new bool[9, 8];
        int[] headWidth = { 2, 4, 6, 8 };
        for (int row = 0; row < 4; row++)
        {
            int w = headWidth[row];
            int x0 = (8 - w) / 2;
            for (int x = x0; x < x0 + w; x++) px[row, x] = true;
        }
        for (int y = 4; y < 9; y++)
            for (int x = 3; x < 5; x++) px[y, x] = true;
        return px;
    }

    // 右箭头 9×8 = 上箭头转 90°：竖干 5 列 + 箭头 4 列（高 2/4/6/8）
    private static bool[,] RightArrowPixels()
    {
        bool[,] px = new bool[8, 9];
        for (int y = 3; y < 5; y++)
            for (int x = 0; x < 5; x++) px[y, x] = true;
        int[] headHeight = { 2, 4, 6, 8 };
        for (int col = 0; col < 4; col++)
        {
            int h = headHeight[col];
            int x = 5 + col;
            int y0 = (8 - h) / 2;
            for (int y = y0; y < y0 + h; y++) px[y, x] = true;
        }
        return px;
    }

    private static void WriteArrow(string path, int width, int height, bool[,] mask)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        Color32 green = ArrowGreen;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // **必须翻转 y**：SetPixels32 的第 0 行是贴图的**最下一行**，
                // 而 mask 的第 0 行按「从上往下」设计。照抄会把上箭头画成下箭头
                // ——右箭头左右对称所以看不出来，只有上箭头暴露这个坑（2026-10-04 踩）。
                int sourceRow = height - 1 - y;
                pixels[y * width + x] = mask[sourceRow, x] ? green : new Color32(0, 0, 0, 0);
            }
        texture.SetPixels32(pixels);
        texture.Apply();

        byte[] png = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // 内容一致就别碰文件：避免无谓的 mtime 抖动（复跑菜单要 0 差异）。
        if (File.Exists(path) && SameBytes(File.ReadAllBytes(path), png)) return;
        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        ConfigureImport(path);
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void ConfigureImport(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.spritePixelsPerUnit = 100f;
        importer.SaveAndReimport();
    }

    // ---- 通用工具 --------------------------------------------------------

    private static Sprite LoadSprite(string path)
    {
        // Multiple 素材（spriteMode 2）必须从 LoadAllAssets 里挑，LoadAssetAtPath 只保证拿到主图。
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
            if (assets[i] is Sprite sprite) return sprite;
        Debug.LogWarning("[GadgetIcon] 素材里没有 sprite：" + path);
        return null;
    }

    private static RectTransform EnsureChild(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found == null)
        {
            var created = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(created, name);
            created.transform.SetParent(parent, false);
            found = created.transform;
        }
        return (RectTransform)found;
    }

    private static Image EnsureImage(Transform parent, string name)
    {
        RectTransform rect = EnsureChild(parent, name);
        var image = rect.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(rect.gameObject);
        return image;
    }

    private static Button FindButton(string name)
    {
        // GadgetsPanel 在 Tickets 页签下是隐藏的，必须连 inactive 一起搜。
        var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include);
        foreach (Button button in buttons)
            if (button != null && button.name == name) return button;
        return null;
    }
}
