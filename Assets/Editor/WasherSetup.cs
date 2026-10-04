using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 一键装配洗盘机：机身素材 + 盘子入场 + 水流特效 + 果冻 + 圆环进度。
// 菜单：Tools/挂个爽/一键装配洗盘机入场与进度环
//
// 幂等：重复点只会把参数改回目标值，不会重复创建物体（按名字查找复用）。
//
// 全部用 SerializedObject 回写：项目里已经踩过两次「改 C# 默认值对场景已有实例无效」的坑。
public static class WasherSetup
{
    private const string WasherName = "Automatic Dish Washer";
    private const string WaterName = "WasherWater";
    private const string PlateFolder = "Assets/Materials/Automatic Dish Washer";
    private const string PlateOldName = "Plate .png";
    private const string PlateNewName = "Plate.png";
    private const string RingPath = "Assets/Materials/Automatic Dish Washer/WasherProgressRing.png";
    private const string MachinePath = "Assets/Materials/Automatic Dish Washer/Automatic Dish Washer - Machine.png";

    // 26 = 场景的实际值（20 → 24 → 26，26 是「再大会顶到屏幕右边界」前最后一档）。
    // 曾经写 24 而场景是 26：菜单一跑就把机身缩回去，环/槽位/水流/碰撞会连带全错 —— 菜单必须与场景一致才能保持幂等。
    private const float WasherScale = 26f;
    // 环贴图与环带。2026-09-29 后半程整体减半：128→64 / 外径 62→31 / 内径 54→27，
    // 环带仍是 4 texel。等比缩小是必须连贴图一起做的 —— 128 texel 铺到减半后的直径上，
    // 1 个环 texel 只剩 0.6 屏幕像素，Point 采样下环带边缘会跳 1px、看起来全是毛边。
    private const int RingTextureSize = 64;
    private const float RingOuter = 31f;
    private const float RingInner = 27f;        // 4 texel 环带（原 8 texel 的一半）
    // 进度环：直径 = 洗盘机可见宽（29.64）× 0.18 ≈ 5.34 世界单位（= 76.8 屏幕像素），
    // 环底边离机身内容顶边 0.5。两台机器共用这一套参数（都归 WasherProgressRing 算）。
    // 用**宽度**而不是高度：两台机器高度接近，按高度算两个环几乎一样大，按宽度算才拉得开
    // （洗盘机 5.34 vs 刮票机 3.01）。旧值是 0.36 / gap 1.0，整体是现在的一倍。
    private const float RingSizeRatio = 0.18f;
    private const float RingBottomGap = 0.5f;
    private const int RingFillSteps = 0;        // 0 = 不量化（逐帧连续填）

    // 玻璃窗：机身 sprite 局部 x 12..101 / y 28..61，即 90×34 texel，中心只偏半个 texel。
    private const int WindowWidthTexels = 90;
    private const int WindowHeightTexels = 34;
    private static readonly Vector2 WindowCenterOffsetTexels = new Vector2(-0.5f, 0.5f);

    // 排序：机身 +4 → 水体 +1（盘后）→ 盘子 +2 → 气泡 +3（盘前）
    private const int PlateSortingOffset = 2;
    private const int WaterBodySortingOffset = 1;
    private const int BubbleSortingOffset = 3;

    [MenuItem("Tools/挂个爽/一键装配洗盘机入场与进度环")]
    public static void Run()
    {
        GameObject washer = GameObject.Find(WasherName);
        if (washer == null)
        {
            Debug.LogError($"[WasherSetup] 场景里找不到 {WasherName}");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("WasherSetup");

        // 1) 素材：机身 + 盘子，都归正导入设置
        Sprite machineSprite = PrepareMachineSprite();
        if (machineSprite == null) return;

        Sprite plateSprite = PreparePlateSprite();
        if (plateSprite == null) return;

        // 2) 洗盘机本体放大（先改 scale，再挂组件：组件的 baseScale 在 Awake 里取样）
        var washerRenderer = washer.GetComponent<SpriteRenderer>();
        Undo.RecordObject(washer.transform, "Washer scale");
        washer.transform.localScale = new Vector3(WasherScale, WasherScale, 1f);
        if (washerRenderer != null)
        {
            Undo.RecordObject(washerRenderer, "Washer sprite");
            washerRenderer.sprite = machineSprite;   // 回填后编辑器里也能直接看到机身
        }

        // 3) 果冻组件（复用 HoverJelly，幅度按 22px 屏幕位移标定，与海绵同标准）
        HoverJelly jelly = AttachJelly(washer, washerRenderer);

        // 4) 盘子入场组件
        WasherPlateFeeder feeder = AttachFeeder(washer, washerRenderer, jelly, plateSprite);

        // 5) 圆环进度（洗盘机的同级物体，避免被 scale=26 和果冻缩放影响）
        WasherProgressRing ring = BuildRing(washer, washerRenderer);

        // 6) 水流特效（反过来必须是**子物体**：继承 scale=26 才能和机身共用像素格）
        WasherWaterEffect water = BuildWater(washer, washerRenderer);

        // 7) 回填洗盘机的引用
        WireWasher(washer, machineSprite, feeder, ring, water);

        // 8) 落盘
        EditorUtility.SetDirty(washer);
        EditorSceneManager.MarkSceneDirty(washer.scene);
        EditorSceneManager.SaveScene(washer.scene);
        Undo.CollapseUndoOperations(group);

        // 回读校验：这一步是必需的，项目里踩过「以为写进去了其实没生效」的坑。
        var check = new SerializedObject(washer.GetComponent<AutomaticDishWasher>());
        string[] names = { "displaySprite", "feeder", "ring", "water" };
        var report = new System.Text.StringBuilder();
        foreach (string field in names)
        {
            var prop = check.FindProperty(field);
            string value = prop == null ? "<字段不存在>"
                : prop.propertyType == SerializedPropertyType.ObjectReference
                    ? (prop.objectReferenceValue == null ? "<null>" : prop.objectReferenceValue.name)
                    : prop.ToString();
            report.Append(field).Append('=').Append(value).Append("  ");
        }

        var waterCheck = new SerializedObject(water);
        report.Append("| water: size=").Append(waterCheck.FindProperty("windowSize").vector2IntValue)
              .Append(" offset=").Append(waterCheck.FindProperty("windowCenterOffset").vector2Value)
              .Append(" bodyOrder=+").Append(waterCheck.FindProperty("bodySortingOffset").intValue)
              .Append(" bubbleOrder=+").Append(waterCheck.FindProperty("bubbleSortingOffset").intValue);

        Debug.Log($"[WasherSetup] 完成：scale={WasherScale} machine={machineSprite.name} " +
                  $"plate={plateSprite.name} ring={ring.name} | {report}");
    }

    // ---- 素材 --------------------------------------------------------------

    // 机身素材：一个 sprite 同时用于「未解锁隐藏 / 待机 / 运行」三种情形。
    //
    // 这里刻意**保持 spriteImportMode = Multiple**，不跟随项目其他贴图的 Single：
    // 源图 128×96 在机身边缘有四边透明留白（左 8 / 右 8 / 下 3 / 上 7），
    // Multiple 把它裁成精确的 114×88，pivot 恰好落在美术内容的中心；
    // 若改成 Single，sprite 会变成整张 128×96 → 机身在视觉上偏 2 texel、
    // 进度环直径涨 9%、盘子槽位区涨 12%。三个下游数值全部要跟着重算，得不偿失。
    private static Sprite PrepareMachineSprite()
    {
        var importer = AssetImporter.GetAtPath(MachinePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError("[WasherSetup] 找不到机身素材：" + MachinePath);
            return null;
        }

        bool dirty = false;
        if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
        if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
        if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            dirty = true;
        }
        if (!Mathf.Approximately(importer.spritePixelsPerUnit, 100f))
        {
            importer.spritePixelsPerUnit = 100f;
            dirty = true;
        }
        if (dirty) importer.SaveAndReimport();

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MachinePath);
        if (sprite == null) Debug.LogError("[WasherSetup] 机身素材没有可用的 sprite：" + MachinePath);
        return sprite;
    }

    private static Sprite PreparePlateSprite()
    {
        string oldPath = PlateFolder + "/" + PlateOldName;
        string newPath = PlateFolder + "/" + PlateNewName;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(oldPath) != null)
        {
            string error = AssetDatabase.RenameAsset(oldPath, PlateNewName);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError("[WasherSetup] 重命名盘子素材失败：" + error);
                return null;
            }
        }

        var importer = AssetImporter.GetAtPath(newPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError("[WasherSetup] 找不到盘子素材：" + newPath);
            return null;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.spritePivot = new Vector2(0.5f, 0.5f);   // 居中
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(newPath);
    }

    private static Sprite BuildRingSprite()
    {
        {
            // 每次都重画：环带内外径改了以后，不重画的话贴图还是旧的。
            var texture = new Texture2D(RingTextureSize, RingTextureSize, TextureFormat.RGBA32, false);
            float center = (RingTextureSize - 1) * 0.5f;
            for (int y = 0; y < RingTextureSize; y++)
            {
                for (int x = 0; x < RingTextureSize; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    bool band = distance <= RingOuter && distance >= RingInner;
                    texture.SetPixel(x, y, band ? Color.white : Color.clear);
                }
            }
            texture.Apply(false);
            System.IO.File.WriteAllBytes(RingPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(RingPath);
        }

        var importer = AssetImporter.GetAtPath(RingPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spritePivot = new Vector2(0.5f, 0.5f);   // 居中
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
    }

    // ---- 组件 --------------------------------------------------------------

    private static HoverJelly AttachJelly(GameObject washer, SpriteRenderer washerRenderer)
    {
        HoverJelly jelly = washer.GetComponent<HoverJelly>();
        if (jelly == null) jelly = Undo.AddComponent<HoverJelly>(washer);

        // 幅度按 22px 屏幕位移标定：直接沿用海绵的标定结果做等比换算，
        // amp = 22 / 屏幕宽度px，等价于 海绵amp × 海绵世界宽 / 洗盘机世界宽。
        float amplitude = 0.06f;
        GameObject sponge = GameObject.Find("Dish Sponge - Yellow");
        if (sponge != null)
        {
            var spongeJelly = sponge.GetComponent<HoverJelly>();
            var spongeRenderer = sponge.GetComponent<SpriteRenderer>();
            if (spongeJelly != null && spongeRenderer != null)
            {
                var so = new SerializedObject(spongeJelly);
                float spongeAmp = so.FindProperty("amplitude").floatValue;
                float spongeWidth = spongeRenderer.bounds.size.x;
                float washerWidth = washerRenderer.bounds.size.x;
                if (spongeWidth > 0f && washerWidth > 0f)
                    amplitude = spongeAmp * spongeWidth / washerWidth;
            }
        }

        var target = new SerializedObject(jelly);
        target.FindProperty("hitSource").enumValueIndex = 1;                 // WorldBounds
        target.FindProperty("worldRenderer").objectReferenceValue = washerRenderer;
        target.FindProperty("amplitude").floatValue = amplitude;
        target.FindProperty("squashRatio").floatValue = 0f;                  // 四向等幅
        target.FindProperty("damping").floatValue = 0.32f;
        target.FindProperty("verticalLag").floatValue = 0.78f;
        target.FindProperty("settleTime").floatValue = 0.22f;
        target.FindProperty("releaseTime").floatValue = 0.16f;
        target.ApplyModifiedPropertiesWithoutUndo();
        return jelly;
    }

    private static WasherPlateFeeder AttachFeeder(GameObject washer, SpriteRenderer washerRenderer,
        HoverJelly jelly, Sprite plateSprite)
    {
        WasherPlateFeeder feeder = washer.GetComponent<WasherPlateFeeder>();
        if (feeder == null) feeder = Undo.AddComponent<WasherPlateFeeder>(washer);

        GameObject container = GameObject.Find("WasherPlates");
        if (container == null)
        {
            container = new GameObject("WasherPlates");
            Undo.RegisterCreatedObjectUndo(container, "WasherPlates");
            container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            container.transform.localScale = Vector3.one;
        }

        var so = new SerializedObject(feeder);
        so.FindProperty("washerRenderer").objectReferenceValue = washerRenderer;
        so.FindProperty("washerJelly").objectReferenceValue = jelly;
        so.FindProperty("plateContainer").objectReferenceValue = container.transform;
        so.FindProperty("plateSprite").objectReferenceValue = plateSprite;
        so.FindProperty("plateScale").floatValue = 20f;
        // 盘子从 +1 挪到 +2：+1 让给水体，气泡在 +3（盘子前面）。
        so.FindProperty("plateSortingOffset").intValue = PlateSortingOffset;
        so.FindProperty("fastStrengthFloor").floatValue = 0.35f;
        so.FindProperty("minPulseDuration").floatValue = 0.06f;
        so.FindProperty("maxPulseDuration").floatValue = 0.22f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return feeder;
    }

    private static WasherProgressRing BuildRing(GameObject washer, SpriteRenderer washerRenderer)
    {
        Sprite ringSprite = BuildRingSprite();

        GameObject ringRoot = GameObject.Find("WasherProgressRing");
        if (ringRoot == null)
        {
            ringRoot = new GameObject("WasherProgressRing", typeof(Canvas), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(ringRoot, "WasherProgressRing");
        }
        Undo.RecordObject(ringRoot.transform, "Ring transform");
        ringRoot.transform.rotation = Quaternion.identity;

        Canvas canvas = ringRoot.GetComponent<Canvas>();
        if (canvas == null) canvas = Undo.AddComponent<Canvas>(ringRoot);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingLayerID = washerRenderer.sortingLayerID;
        canvas.sortingOrder = washerRenderer.sortingOrder + 2;
        EditorUtility.SetDirty(canvas);

        CanvasGroup group = ringRoot.GetComponent<CanvasGroup>();
        if (group == null) group = Undo.AddComponent<CanvasGroup>(ringRoot);
        group.alpha = 0f;   // 默认隐藏，入场结束后由 WasherProgressRing.Show() 淡入

        Transform imageTransform = ringRoot.transform.Find("Ring");
        GameObject imageObject;
        if (imageTransform == null)
        {
            imageObject = new GameObject("Ring", typeof(Image));
            Undo.RegisterCreatedObjectUndo(imageObject, "Ring");
            imageObject.transform.SetParent(ringRoot.transform, false);
        }
        else
        {
            imageObject = imageTransform.gameObject;
        }

        var rect = imageObject.transform as RectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(RingTextureSize, RingTextureSize);

        Image image = imageObject.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(imageObject);
        image.sprite = ringSprite;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = (int)Image.Origin360.Top;
        image.fillClockwise = true;
        image.fillAmount = 0f;
        image.raycastTarget = false;
        EditorUtility.SetDirty(image);

        WasherProgressRing ring = ringRoot.GetComponent<WasherProgressRing>();
        if (ring == null) ring = Undo.AddComponent<WasherProgressRing>(ringRoot);
        var so = new SerializedObject(ring);
        so.FindProperty("ringImage").objectReferenceValue = image;
        so.FindProperty("canvasGroup").objectReferenceValue = group;
        // 位置/尺寸参数与运行时是同一套字段：不回写就会留着场景里的旧值（旧环是「身高 × 1.45」，
        // 而且尺寸只在场景里手写过一次 —— 机身从 24 长到 26 之后它就没再跟着变过）。
        so.FindProperty("fillSteps").intValue = RingFillSteps;
        so.FindProperty("sizeRatio").floatValue = RingSizeRatio;
        so.FindProperty("bottomGap").floatValue = RingBottomGap;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ring);

        // 位置与尺寸交给环自己算 —— 编辑器里看到的就是运行时算出来的同一个位置。
        PlaceRing(ring, washerRenderer);
        return ring;
    }

    // 洗盘机的可见内容包围盒（世界空间）：它只有一个 sprite 且不透明区贴着子矩形边缘，
    // 所以 sprite.bounds（乘静止缩放）就是可见范围。用 sprite.bounds 而不是 renderer.bounds ——
    // 后者在 renderer 被禁用（未解锁）时不可靠。
    private static void PlaceRing(WasherProgressRing ring, SpriteRenderer machineRenderer)
    {
        Vector3 scale = machineRenderer.transform.lossyScale;
        Vector3 size = Vector3.Scale(machineRenderer.sprite.bounds.size, scale);
        Vector3 center = machineRenderer.transform.position
            + Vector3.Scale(machineRenderer.sprite.bounds.center, scale);
        ring.SetMachineAnchor(machineRenderer, center, new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)));
    }

    // 水流特效：挂在洗盘机**下面**（子物体），才能继承 scale=26 与机身共用像素格。
    //
    // 场景里只落一个空的 WasherWater 物体（SpriteRenderer + WasherWaterEffect），
    // 贴图与气泡都在运行时生成：气泡是组件的内部细节，不是场景数据，
    // 和 WasherPlateFeeder 动态生成盘子是同一个思路。
    private static WasherWaterEffect BuildWater(GameObject washer, SpriteRenderer washerRenderer)
    {
        Transform existing = washer.transform.Find(WaterName);
        GameObject waterObject;
        if (existing == null)
        {
            waterObject = new GameObject(WaterName, typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(waterObject, WaterName);
            waterObject.transform.SetParent(washer.transform, false);
        }
        else
        {
            waterObject = existing.gameObject;
        }

        WasherWaterEffect water = waterObject.GetComponent<WasherWaterEffect>();
        if (water == null) water = Undo.AddComponent<WasherWaterEffect>(waterObject);

        var so = new SerializedObject(water);
        so.FindProperty("washerRenderer").objectReferenceValue = washerRenderer;
        so.FindProperty("windowSize").vector2IntValue = new Vector2Int(WindowWidthTexels, WindowHeightTexels);
        so.FindProperty("windowCenterOffset").vector2Value = WindowCenterOffsetTexels;
        so.FindProperty("pixelsPerUnit").floatValue = 100f;
        so.FindProperty("bodySortingOffset").intValue = WaterBodySortingOffset;
        so.FindProperty("bubbleSortingOffset").intValue = BubbleSortingOffset;

        // 调参也在这里回写：组件已经挂到场景上了，光改 C# 默认值对它无效
        // （项目里踩过两次的坑：序列化值优先级高于新默认值）。
        so.FindProperty("bodyColor").colorValue = new Color32(72, 142, 178, 170);
        so.FindProperty("surfaceColor").colorValue = new Color32(214, 232, 233, 235);
        so.FindProperty("surfaceGlowColor").colorValue = new Color32(126, 196, 226, 150);
        so.FindProperty("streakColor").colorValue = new Color32(150, 208, 232, 150);
        so.FindProperty("bubbleColor").colorValue = new Color32(46, 96, 130, 235);
        so.FindProperty("levelBaseRatio").floatValue = 0.84f;   // 必须高于盘子顶部，否则水线被挡
        so.FindProperty("levelAmplitude").floatValue = 2f;
        so.FindProperty("levelPeriod").floatValue = 2.4f;
        so.FindProperty("waveAmplitude").floatValue = 1.2f;
        so.FindProperty("waveWavelength").floatValue = 34f;
        so.FindProperty("surfaceScrollSpeed").floatValue = 13f;
        so.FindProperty("streakScrollSpeed").floatValue = 21f;
        so.FindProperty("streakSpacing").intValue = 9;
        so.FindProperty("bubbleCount").intValue = 8;
        so.ApplyModifiedPropertiesWithoutUndo();

        water.ApplyNow();   // 编辑模式下先把几何/层级对齐（贴图与气泡要运行时才有）
        EditorUtility.SetDirty(water);
        EditorUtility.SetDirty(waterObject);
        return water;
    }

    private static void WireWasher(GameObject washer, Sprite machineSprite,
        WasherPlateFeeder feeder, WasherProgressRing ring, WasherWaterEffect water)
    {
        var washerComponent = washer.GetComponent<AutomaticDishWasher>();
        if (washerComponent == null)
        {
            Debug.LogError("[WasherSetup] 洗盘机上没有 AutomaticDishWasher");
            return;
        }
        var so = new SerializedObject(washerComponent);
        so.FindProperty("displaySprite").objectReferenceValue = machineSprite;
        so.FindProperty("feeder").objectReferenceValue = feeder;
        so.FindProperty("ring").objectReferenceValue = ring;
        so.FindProperty("water").objectReferenceValue = water;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(washerComponent);
    }
}
