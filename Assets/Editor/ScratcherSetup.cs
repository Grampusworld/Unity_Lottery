using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 一键装配自动刮彩票机：三段素材导入 + 机身 + 刮擦特效 + 进度环 + 迷你票容器 + 果冻，
// 同时把 GadgetsPanel 改成「8 行 × 108px 的可滚动 viewport」，腾出机器需要的三个按钮
// 与 MULTIPLE PLATES。
// 菜单：Tools/挂个爽/一键装配自动刮彩票机
//
// 幂等：重复点只会把参数改回目标值，按名字复用已有物体，不会重复创建、不会重复挂 onClick。
// 验收手段固定为：跑完之后 `diff Assets/Scenes/SampleScene.unity <跑之前的备份>` 必须是 0 差异。
//
// 全部用 SerializedObject / TextureImporterSettings 回写：项目里已经踩过三次
// 「改 C# 默认值对场景已有实例无效」的坑（LotteryTicket.prizes、HoverJelly 参数、洗盘机调参）。
public static class ScratcherSetup
{
    private const string Folder = "Assets/Materials/Auto Lottery Machine";
    private const string MachineName = "Auto Lottery Machine";
    private const string EffectName = "ScratcherEffect";
    private const string TicketContainerName = "ScratcherTickets";
    private const string RingName = "ScratcherProgressRing";
    private const string RingSpritePath = "Assets/Materials/Automatic Dish Washer/WasherProgressRing.png";
    private const string PanelName = "GadgetsPanel";
    private const string ContentName = "Content";

    private const float MachineScale = 38f;              // 与洗盘机同高（约 22.4 世界单位），像素格 0.38
    // 机身底边中点。2026-09-29 与海绵一起左移 2（机器间 gap 1.72 → 3.72）——
    // 旧的 (42,-30) 连场景都对不上了，这里补正。
    private static readonly Vector3 MachineFeet = new Vector3(51.1f, -36.5f, 0f);
    private const int MachineSortingOrder = 4;           // 与洗盘机同层；两台机器不同时占同一片区域
    private const int MiniSortingBoost = 6;              // 迷你票：机身 0→6 / 数字 1→7 / 涂层 2→8
    private const float EffectSortingOffset = 5f;        // 特效 → 9，盖在迷你票上面
    // 进度环直径 = 可见内容**宽度** × 该系数（不是高度）。两台机器高度接近（22.9 / 19.8~23.2），
    // 按高度算两个环会几乎一样大；按宽度算才拉得开：洗盘机 29.64×0.18 ≈ 5.34，
    // 刮票机 16.72×0.18 ≈ 3.01（T2 19.76×0.18 ≈ 3.56）。
    // 2026-09-29 后半程：0.36 → 0.18（直径减半），贴图同步 128 → 64，环带仍 4 texel。
    private const float RingSizeRatio = 0.18f;
    private const float RingBottomGap = 0.5f;    // 环底边与机身内容顶边的间隙（世界单位），跟着减半
    private const int RingFillSteps = 0;         // 0 = 不量化（逐帧连续填）
    private const int RingTextureSize = 64;      // 与 WasherSetup 共用同一张图，两处必须一致

    // GadgetsPanel 现在是**可滚动 viewport**（PixelRowScroll + RectMask2D）：
    //   viewport 620×915（比按钮宽 30，左右各 15 装下 HoverJelly 悬停膨胀的 11px）
    //   Content 590 宽居中，高度由 PixelRowScroll 按行数现算（8 行 = 1038px）
    //   行高 108 / 行间隙 22 / 行距 130 / 首行中心距内容顶 64 / 内容上下留白各 10
    // 行高 108 与行间隙 22 是项目约定（见 ref-ui-motion.md），滚动不改它们 ——
    // 8 行 1038px 装进 915px 视口，可滚 123px。
    private const float GadgetViewportWidth = 620f;
    private const float GadgetViewportHeight = 915f;
    // 左边缘：30（旧面板）→ 15（本菜单原来写的）→ **5**（2026-10-01 对齐 EconomyShopSetup）。
    // 两个装配菜单都在写这一个值：EconomyShopSetup.ConfigureScrollbar 里为了给滚动条让出宽度写 5，
    // 本菜单以前写 15 —— 谁最后跑谁说了算，场景里实际是 5。而滚动条 2026-10-01 左移到 x=628
    // （左边缘 620）之后，视口若回到 15，按钮右边缘会到 620，正好被滚动条压住。
    // 所以这里必须也是 5：内容水平位置与场景一致，菜单复跑才有 0 差异。
    private const float GadgetViewportX = 5f;
    private const float GadgetPanelTopY = -285f;
    private const float GadgetContentWidth = 590f;
    private const float GadgetButtonWidth = 590f;
    private const float GadgetButtonHeight = 108f;
    private const float GadgetRowGap = 22f;
    private const float GadgetRowStep = 130f;    // = 108 + 22
    private const float GadgetRowTop = -64f;     // 首行中心距内容顶
    private const float GadgetMargin = 10f;      // 首行顶边之上的留白（= GadgetRowTop - 行高/2）

    // 10 行的顺序。PLATE VALUE 在首行、MULTIPLE PLATES 在第 2 行：它与 PURPLE SPONGE 同属「手工处理盘子」
    // 这一类，放在一起语义连贯；其余 8 行相对旧布局整体下移一行。
    // **这个数组是行数与顺序的唯一来源**：末尾新增一行时，LotteryGame.RefreshGadgetRows 的
    // gadgetRows / visible 两张表必须同步加一项，否则运行时排布会少一行、末尾那行留在原位。
    private static readonly string[] GadgetRowNames =
    {
        "PlateValueButton", "MultiplePlatesButton", "PurpleSpongeButton", "WasherUnlockButton", "SpeedUpgradeButton",
        "CapacityUpgradeButton", "ScratcherUnlockButton", "ScratcherSpeedButton", "ScratcherCapacityButton",
        "AutoFeedButton"
    };

    private class TierDef
    {
        public string fileName;
        public string displayName;
        public float pivotYTexel;       // 内容底边（texel，y 自下往上）；装配时由素材实测覆盖
        public Vector4 workRect;        // x0, y0, w, h（texel）
        public Vector4 contentRect;     // 可见内容包围盒 x0, y0, w, h（texel）；装配时由素材实测覆盖
        public int maxColumns;
    }

    // 等级顺序由用户指定：低 → 高 = Retro CRT → Classic Blue → Gold Deluxe。
    // pivot 全部落在各自内容的底边中心，换级时机身脚底不动、向上长。
    private static readonly TierDef[] Tiers =
    {
        new TierDef { fileName = "Auto Lottery Machine V3 - Retro CRT.png", displayName = "RETRO CRT",
                      pivotYTexel = 9f, workRect = new Vector4(18f, 20f, 28f, 14f), maxColumns = 3 },
        new TierDef { fileName = "Auto Lottery Machine V1 - Classic Blue.png", displayName = "CLASSIC BLUE",
                      pivotYTexel = 3f, workRect = new Vector4(16f, 31f, 32f, 16f), maxColumns = 3 },
        new TierDef { fileName = "Auto Lottery Machine V2 - Gold Deluxe.png", displayName = "GOLD DELUXE",
                      pivotYTexel = 1f, workRect = new Vector4(13f, 30f, 38f, 16f), maxColumns = 2 },
    };

    [MenuItem("Tools/挂个爽/一键装配自动刮彩票机")]
    public static void Run()
    {
        var game = Object.FindAnyObjectByType<LotteryGame>();
        if (game == null)
        {
            Debug.LogError("[ScratcherSetup] 场景里找不到挂 LotteryGame 的 GameManager");
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("ScratcherSetup");

        // 1) 三段素材的导入设置（Single / PPU 100 / pivot 在内容底边 / Point / 不压缩）
        Sprite[] sprites = new Sprite[Tiers.Length];
        for (int i = 0; i < Tiers.Length; i++)
        {
            sprites[i] = PrepareTierSprite(Tiers[i]);
            if (sprites[i] == null) return;
        }

        // 2) 机身
        GameObject machine = EnsureMachine(sprites[0]);
        var body = machine.GetComponent<SpriteRenderer>();

        // 3) 果冻（复用 HoverJelly，WorldBounds 命中，幅度按 22px 屏幕位移标定）
        HoverJelly jelly = AttachJelly(machine, body);

        // 4) 刮擦特效：机身的子物体且 localScale = 1，才能继承 scale=38 与机身共用像素格
        ScratcherEffect effect = BuildEffect(machine, body);

        // 5) 迷你票容器：必须独立、scale = 1、位置在世界原点（和 WasherPlates 同理）
        Transform container = EnsureContainer();

        // 6) 进度环：与机身同级，避免被 scale=38 和果冻缩放带跑
        WasherProgressRing ring = BuildRing(machine, body);

        // 7) 回填机器组件
        AutoScratcher scratcher = WireScratcher(machine, sprites, jelly, ring, effect, container);

        // 8) GadgetsPanel 改成可滚动 viewport + 8 行按钮
        BuildGadgetPanel(game, scratcher);

        // 9) 票 Prefab：补上入场动画与拖拽组件，并把票种写进去
        PrepareTicketPrefabs();

        EditorUtility.SetDirty(machine);
        EditorSceneManager.MarkSceneDirty(machine.scene);
        EditorSceneManager.SaveScene(machine.scene);
        Undo.CollapseUndoOperations(group);

        // 9) 回读校验：这一步是必需的，项目里踩过「以为写进去了其实没生效」的坑
        Debug.Log("[ScratcherSetup] 完成：" + Report(scratcher, body, machine));
    }

    // ---- 素材 --------------------------------------------------------------

    private static Sprite PrepareTierSprite(TierDef tier)
    {
        string path = Folder + "/" + tier.fileName;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError("[ScratcherSetup] 找不到素材：" + path);
            return null;
        }

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMode = (int)SpriteImportMode.Single;

        // 先实测可见内容包围盒，再拿它推导 pivot：
        // 三张素材都在 64×64 画布里留了大小不一的透明边（V3 底边 9 texel / V1 3 / V2 1），
        // 手抄这两个数字迟早会飘。实测之后 pivot 必然落在内容底边，环也必然贴住机身。
        if (MeasureContent(path, out Vector4 content))
        {
            tier.contentRect = content;
            tier.pivotYTexel = content.y;
            Debug.Log(string.Format("[ScratcherSetup] {0} 内容包围盒 x{1} y{2} {3}×{4} texel → pivot.y={2}",
                tier.fileName, content.x, content.y, content.z, content.w));
        }
        else
        {
            Debug.LogWarning("[ScratcherSetup] " + tier.fileName + " 内容包围盒实测失败，沿用预制值");
        }

        // 关键：不把 alignment 设成 Custom（9），spritePivot 会被「居中」直接吃掉。
        settings.spriteAlignment = 9;
        settings.spritePivot = new Vector2(0.5f, tier.pivotYTexel / 64f);
        settings.spritePixelsPerUnit = 100f;
        settings.spriteMeshType = SpriteMeshType.Tight;
        settings.filterMode = FilterMode.Point;
        settings.mipmapEnabled = false;
        settings.alphaIsTransparency = true;
        settings.wrapMode = TextureWrapMode.Clamp;
        importer.SetTextureSettings(settings);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogError("[ScratcherSetup] 素材没有可用的 sprite：" + path);
            return null;
        }
        // 回读确认 pivot 真的生效了（被吃掉的话脚底会对不齐，三张素材会互相跳）。
        if (Mathf.Abs(sprite.pivot.y - tier.pivotYTexel) > 0.51f)
            Debug.LogWarning(string.Format("[ScratcherSetup] {0} 的 pivot.y = {1}，期望 {2}（alignment 没设成 Custom？）",
                tier.fileName, sprite.pivot.y, tier.pivotYTexel));
        return sprite;
    }

    // 实测 PNG 的不透明包围盒（texel；y 自下往上，与 spritePivot / workRect 同一套坐标）。
    // 走 File.ReadAllBytes + Texture2D.LoadImage：脚本临时建的贴图不需要 isReadable 也能读像素，
    // 所以不必为了量一次尺寸去改素材的导入设置。
    private static bool MeasureContent(string path, out Vector4 rect)
    {
        rect = new Vector4(0f, 0f, 64f, 64f);
        Texture2D texture = null;
        try
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes)) return false;

            Color32[] pixels = texture.GetPixels32();   // 行 0 = 贴图底边
            int w = texture.width;
            int h = texture.height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (pixels[y * w + x].a <= 8) continue;      // 忽略近乎全透明的抗锯齿轮廓
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0 || maxY < 0) return false;
            rect = new Vector4(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ScratcherSetup] 量素材尺寸失败：" + path + " → " + e.Message);
            return false;
        }
        finally
        {
            if (texture != null) Object.DestroyImmediate(texture);
        }
    }

    // ---- 物体 --------------------------------------------------------------

    private static GameObject EnsureMachine(Sprite sprite)
    {
        GameObject machine = GameObject.Find(MachineName);
        if (machine == null)
        {
            machine = new GameObject(MachineName, typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(machine, MachineName);
        }
        Undo.RecordObject(machine.transform, "Machine transform");
        machine.transform.SetPositionAndRotation(MachineFeet, Quaternion.identity);
        machine.transform.localScale = new Vector3(MachineScale, MachineScale, 1f);

        var renderer = machine.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(machine);
        Undo.RecordObject(renderer, "Machine sprite");
        renderer.sprite = sprite;
        renderer.sortingLayerName = "Default";
        renderer.sortingOrder = MachineSortingOrder;
        renderer.color = Color.white;
        EditorUtility.SetDirty(renderer);
        return machine;
    }

    private static HoverJelly AttachJelly(GameObject machine, SpriteRenderer body)
    {
        HoverJelly jelly = machine.GetComponent<HoverJelly>();
        if (jelly == null) jelly = Undo.AddComponent<HoverJelly>(machine);

        // 幅度按「22px 屏幕位移」标定：沿用海绵的标定结果做等比换算，
        // amp = 海绵amp × 海绵世界宽 / 机身世界宽。机器比海绵宽，amp 自然更小。
        float amplitude = 0.09f;
        GameObject sponge = GameObject.Find("Dish Sponge - Yellow");
        if (sponge != null)
        {
            var spongeJelly = sponge.GetComponent<HoverJelly>();
            var spongeRenderer = sponge.GetComponent<SpriteRenderer>();
            if (spongeJelly != null && spongeRenderer != null)
            {
                var spongeSo = new SerializedObject(spongeJelly);
                float spongeAmp = spongeSo.FindProperty("amplitude").floatValue;
                float spongeWidth = spongeRenderer.bounds.size.x;
                float machineWidth = body.bounds.size.x;
                if (spongeWidth > 0f && machineWidth > 0f)
                    amplitude = spongeAmp * spongeWidth / machineWidth;
            }
        }

        var so = new SerializedObject(jelly);
        so.FindProperty("hitSource").enumValueIndex = 1;            // WorldBounds
        so.FindProperty("worldRenderer").objectReferenceValue = body;
        so.FindProperty("amplitude").floatValue = amplitude;
        so.FindProperty("squashRatio").floatValue = 0f;             // 四向等幅
        so.FindProperty("damping").floatValue = 0.32f;
        so.FindProperty("verticalLag").floatValue = 0.78f;
        so.FindProperty("settleTime").floatValue = 0.22f;
        so.FindProperty("releaseTime").floatValue = 0.16f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return jelly;
    }

    private static ScratcherEffect BuildEffect(GameObject machine, SpriteRenderer body)
    {
        Transform existing = machine.transform.Find(EffectName);
        GameObject effectObject;
        if (existing == null)
        {
            effectObject = new GameObject(EffectName, typeof(SpriteRenderer));
            Undo.RegisterCreatedObjectUndo(effectObject, EffectName);
            effectObject.transform.SetParent(machine.transform, false);
        }
        else
        {
            effectObject = existing.gameObject;
        }
        effectObject.transform.localPosition = Vector3.zero;
        effectObject.transform.localScale = Vector3.one;

        var renderer = effectObject.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(effectObject);
        // 贴图是运行时生成的，编辑器里既没有 sprite 也应当保持隐藏，免得在场景视图里留个白方块。
        renderer.sprite = null;
        renderer.enabled = false;
        renderer.sortingLayerID = body.sortingLayerID;

        ScratcherEffect effect = effectObject.GetComponent<ScratcherEffect>();
        if (effect == null) effect = Undo.AddComponent<ScratcherEffect>(effectObject);

        var so = new SerializedObject(effect);
        so.FindProperty("pixelsPerUnit").floatValue = 100f;
        so.FindProperty("sortingOffset").floatValue = EffectSortingOffset;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
        return effect;
    }

    private static Transform EnsureContainer()
    {
        GameObject container = GameObject.Find(TicketContainerName);
        if (container == null)
        {
            container = new GameObject(TicketContainerName);
            Undo.RegisterCreatedObjectUndo(container, TicketContainerName);
        }
        container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        container.transform.localScale = Vector3.one;
        return container.transform;
    }

    private static WasherProgressRing BuildRing(GameObject machine, SpriteRenderer body)
    {
        var ringSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath);
        if (ringSprite == null)
        {
            Debug.LogError("[ScratcherSetup] 找不到进度环贴图，请先跑一次洗盘机的装配菜单：" + RingSpritePath);
            return null;
        }

        GameObject ringRoot = GameObject.Find(RingName);
        if (ringRoot == null)
        {
            ringRoot = new GameObject(RingName, typeof(Canvas), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(ringRoot, RingName);
        }
        Undo.RecordObject(ringRoot.transform, "Ring transform");
        ringRoot.transform.rotation = Quaternion.identity;

        // 可见内容包围盒（世界空间）。与运行时 ContentRectWorld 同一套算法：contentRect 是实测的
        // 不透明区、pivot 落在内容底边中点，所以 transform.position 既不是矩形中心、
        // sprite.bounds 也不是可见范围（三张素材都是 64×64 且四边留白不一）。
        Vector3 contentCenter;
        Vector2 contentSize;
        if (Tiers.Length > 0 && Tiers[0].contentRect.z > 0.5f)
        {
            Vector4 r = Tiers[0].contentRect;
            float pivotX = 32f;
            float pivotY = Tiers[0].pivotYTexel;
            float cx = (r.x + r.z * 0.5f - pivotX) / 100f * MachineScale;
            float cy = (r.y + r.w * 0.5f - pivotY) / 100f * MachineScale;
            contentCenter = machine.transform.position + new Vector3(cx, cy, 0f);
            contentSize = new Vector2(r.z / 100f * MachineScale, r.w / 100f * MachineScale);
        }
        else
        {
            contentCenter = body.bounds.center;
            contentSize = body.bounds.size;
        }

        Canvas canvas = ringRoot.GetComponent<Canvas>();
        if (canvas == null) canvas = Undo.AddComponent<Canvas>(ringRoot);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingLayerID = body.sortingLayerID;
        canvas.sortingOrder = Mathf.RoundToInt(MachineSortingOrder + EffectSortingOffset + 1f);
        EditorUtility.SetDirty(canvas);

        CanvasGroup group = ringRoot.GetComponent<CanvasGroup>();
        if (group == null) group = Undo.AddComponent<CanvasGroup>(ringRoot);
        group.alpha = 0f;

        Transform imageTransform = ringRoot.transform.Find("Ring");
        GameObject imageObject;
        if (imageTransform == null)
        {
            imageObject = new GameObject("Ring", typeof(Image));
            Undo.RegisterCreatedObjectUndo(imageObject, "Ring");
            imageObject.transform.SetParent(ringRoot.transform, false);
        }
        else imageObject = imageTransform.gameObject;

        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
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
        so.FindProperty("ringColor").colorValue = new Color(1f, 0.78f, 0.32f, 1f);   // 金色，与洗盘机的青蓝区分
        // 位置参数与运行时同一套字段。不回写就留着场景里的旧值（旧环 = 内容高 × 0.9，直径还随等级变）。
        so.FindProperty("fillSteps").intValue = RingFillSteps;
        so.FindProperty("sizeRatio").floatValue = RingSizeRatio;
        so.FindProperty("bottomGap").floatValue = RingBottomGap;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ring);

        // 位置与尺寸交给环自己算（环落在机身**正上方**，与机身零重叠）。
        ring.SetMachineAnchor(body, contentCenter, contentSize);
        return ring;
    }

    private static AutoScratcher WireScratcher(GameObject machine, Sprite[] sprites, HoverJelly jelly,
        WasherProgressRing ring, ScratcherEffect effect, Transform container)
    {
        AutoScratcher scratcher = machine.GetComponent<AutoScratcher>();
        if (scratcher == null) scratcher = Undo.AddComponent<AutoScratcher>(machine);

        var so = new SerializedObject(scratcher);
        so.FindProperty("jelly").objectReferenceValue = jelly;
        so.FindProperty("ring").objectReferenceValue = ring;
        so.FindProperty("effect").objectReferenceValue = effect;
        so.FindProperty("ticketContainer").objectReferenceValue = container;
        so.FindProperty("miniSortingBoost").intValue = MiniSortingBoost;

        var tiers = so.FindProperty("tiers");
        tiers.arraySize = Tiers.Length;
        for (int i = 0; i < Tiers.Length; i++)
        {
            var element = tiers.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("displayName").stringValue = Tiers[i].displayName;
            element.FindPropertyRelative("sprite").objectReferenceValue = sprites[i];
            element.FindPropertyRelative("workRect").vector4Value = Tiers[i].workRect;
            element.FindPropertyRelative("contentRect").vector4Value = Tiers[i].contentRect;
            element.FindPropertyRelative("maxColumns").intValue = Tiers[i].maxColumns;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(scratcher);
        return scratcher;
    }

    // ---- GadgetsPanel：可滚动 viewport + 8 行 -------------------------------

    private static void BuildGadgetPanel(LotteryGame game, AutoScratcher scratcher)
    {
        // 不能用 GameObject.Find：GadgetsPanel 在 Tickets 页签下是隐藏的，
        // GameObject.Find 只找激活物体，会直接报「找不到面板」。
        Transform panel = FindInactive("Canvas", PanelName);
        if (panel == null)
        {
            Debug.LogError("[ScratcherSetup] 场景里找不到 " + PanelName + "（已含隐藏物体）");
            return;
        }

        BuildScrollViewport(panel);
        Transform content = EnsureContent(panel);
        if (content == null) return;

        // 既有 7 行整体下移一行（第 1 行留给 MULTIPLE PLATES）。
        // 行在改版前是 GadgetsPanel 的直属子物体、之后挂在 Content 下，
        // 所以查找必须从 panel 往下扫整棵子树，两个位置都要能找到 —— 否则第二次跑菜单就只剩新行。
        for (int i = 1; i < GadgetRowNames.Length; i++)
        {
            Transform row = FindInChild(panel, GadgetRowNames[i]);
            if (row == null)
            {
                Debug.LogWarning("[ScratcherSetup] GadgetsPanel 下找不到 " + GadgetRowNames[i]);
                continue;
            }
            if (row.parent != content)
                Undo.SetTransformParent(row, content, "Gadget row → Content");
            PlaceRow(row, i);
        }

        // 新按钮一律从紫色海绵克隆：字体、内缩、HoverJelly 参数、Image 色值全部一次对齐，
        // 比手搓一套 UI 靠谱得多（手搓最容易漏掉的就是 TMP 的字号和 HoverJelly 的幅度）。
        Transform templateRow = content.Find("PurpleSpongeButton");
        GameObject template = templateRow != null ? templateRow.gameObject : null;
        if (template == null)
        {
            Debug.LogError("[ScratcherSetup] 找不到克隆模板 PurpleSpongeButton");
            return;
        }

        // EnsureRowButton 是幂等的：已存在就只重摆位置 + 重连 onClick。
        Button plateValue = EnsureRowButton(content, template, "PlateValueButton", 0, game, game.UpgradePlateValue);
        // 占位文案里的数值一律取 LotteryEconomy —— 手写 "$1 -> $3" 会在经济表改版后变成谎言。
        SetRowLabel(plateValue.transform, "PLATE VALUE  $" + LotteryEconomy.PlateValueCosts[0]
            + "\n$" + LotteryEconomy.PlateValues[0] + " -> $" + LotteryEconomy.PlateValues[1]
            + "  LV 0/" + LotteryEconomy.PlateValueCosts.Length);
        Button multi = EnsureRowButton(content, template, "MultiplePlatesButton", 1,
            game, game.BuyMultiplePlates);
        // 新行是从 PurpleSpongeButton 克隆的，TMP 文案会一起带过来 —— 不覆写的话
        // Hierarchy 预览和场景里那块按钮会一直显示「PURPLE SPONGE  $30」。
        // 这里写**未解锁**态的占位文案，数值直接取 LotteryGame 的常数：
        // 运行时 RefreshUI 每帧会用同一组常数重写，两边永远一致。
        SetRowLabel(multi.transform, "MULTIPLE PLATES  $" + LotteryGame.MultiPlateCost
            + "\nPLATES UP TO " + LotteryGame.MultiPlateBaseCapacity);
        Button unlock = EnsureRowButton(content, template, "ScratcherUnlockButton", 6,
            game, game.BuyScratcher);
        Button speed = EnsureRowButton(content, template, "ScratcherSpeedButton", 7,
            game, game.UpgradeScratcherSpeed);
        Button capacity = EnsureRowButton(content, template, "ScratcherCapacityButton", 8,
            game, game.UpgradeScratcherCapacity);
        // 自动投喂：依附刮票机，未解锁刮票机时 RefreshGadgetRows 会整行隐藏。
        // 文案取 LotteryEconomy 的常数，运行时 RefreshUI 会用同一组常数覆写。
        Button autoFeed = EnsureRowButton(content, template, "AutoFeedButton", 9,
            game, game.BuyAutoFeed);
        SetRowLabel(autoFeed.transform, "AUTO FEED  $" + LotteryEconomy.AutoFeedCost + "\nSKIP THE DRAG");

        // 行数变了就把 content 高度与滚动位置重新落定一次（编辑器里 Awake 不会跑）。
        PixelRowScroll scroll = panel.GetComponent<PixelRowScroll>();
        if (scroll != null)
        {
            var sso = new SerializedObject(scroll);
            sso.FindProperty("content").objectReferenceValue = content as RectTransform;
            sso.FindProperty("rowPitch").floatValue = GadgetRowStep;
            sso.FindProperty("rowHeight").floatValue = GadgetButtonHeight;
            sso.FindProperty("rowGap").floatValue = GadgetRowGap;
            sso.FindProperty("topMargin").floatValue = GadgetMargin;
            sso.FindProperty("bottomMargin").floatValue = GadgetMargin;
            sso.FindProperty("rowCount").intValue = GadgetRowNames.Length;
            sso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scroll);
            scroll.ApplyLayout();
        }

        var so = new SerializedObject(game);
        so.FindProperty("scratcher").objectReferenceValue = scratcher;
        so.FindProperty("multiplePlatesButton").objectReferenceValue = multi;
        so.FindProperty("plateValueButton").objectReferenceValue = plateValue;
        so.FindProperty("scratcherUnlockButton").objectReferenceValue = unlock;
        so.FindProperty("scratcherSpeedButton").objectReferenceValue = speed;
        so.FindProperty("scratcherCapacityButton").objectReferenceValue = capacity;
        so.FindProperty("autoFeedButton").objectReferenceValue = autoFeed;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(game);
    }

    // 面板本身当视口：620 宽（比按钮宽 30，左右各 15 装下 HoverJelly 悬停膨胀的 11px）。
    // 左边缘从 30 挪到 15 —— 内容仍然居中在 325，水平位置一点没动。
    private static void BuildScrollViewport(Transform panel)
    {
        var viewport = panel as RectTransform;
        if (viewport != null)
        {
            Undo.RecordObject(viewport, "Gadget viewport");
            viewport.anchorMin = new Vector2(0f, 1f);
            viewport.anchorMax = new Vector2(0f, 1f);
            viewport.pivot = new Vector2(0f, 1f);
            viewport.anchoredPosition = new Vector2(GadgetViewportX, GadgetPanelTopY);
            viewport.sizeDelta = new Vector2(GadgetViewportWidth, GadgetViewportHeight);
            viewport.localScale = Vector3.one;
            EditorUtility.SetDirty(viewport);
        }

        // RectMask2D 不需要自身有 Graphic：它把裁剪矩形推给子物体的 IClippable 实现。
        // 比 Mask 便宜 —— 不用模板缓冲，Overlay 画布下不多一遍 pass。
        if (panel.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(panel.gameObject);

        PixelRowScroll scroll = panel.GetComponent<PixelRowScroll>();
        if (scroll == null) scroll = Undo.AddComponent<PixelRowScroll>(panel.gameObject);
        EditorUtility.SetDirty(scroll);
    }

    // Content：590 宽居中，高度由 PixelRowScroll 现算。
    // 它自己挂一个全透明的 Image 当**滚动接收面** —— Button 之间 22px 的缝、
    // 两侧各 15px 的余量都没有 raycast target，滚轮落在那里会掉。Image 的 raycast
    // 与颜色无关（alphaHitTestMinimumThreshold 默认 0），透明也能接住事件。
    private static Transform EnsureContent(Transform panel)
    {
        Transform existing = panel.Find(ContentName);
        GameObject content;
        if (existing == null)
        {
            content = new GameObject(ContentName, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            Undo.RegisterCreatedObjectUndo(content, ContentName);
            content.transform.SetParent(panel, false);
        }
        else content = existing.gameObject;

        var rect = (RectTransform)content.transform;
        Undo.RecordObject(rect, "Gadget content");
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(GadgetContentWidth, rect.sizeDelta.y);
        rect.localScale = Vector3.one;
        EditorUtility.SetDirty(rect);

        var catcher = content.GetComponent<UnityEngine.UI.Image>();
        if (catcher == null) catcher = Undo.AddComponent<UnityEngine.UI.Image>(content);
        catcher.color = Color.clear;
        catcher.raycastTarget = true;
        EditorUtility.SetDirty(catcher);
        return content.transform;
    }

    // 在 root 的整棵子树里按名字找物体，**包含隐藏物体**。
    private static Transform FindInactive(string rootName, string childName)
    {
        GameObject root = GameObject.Find(rootName);
        if (root == null) return null;
        return FindInChild(root.transform, childName);
    }

    private static Transform FindInChild(Transform root, string childName)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == childName) return all[i];
        return null;
    }

    // 改掉从模板克隆来的 TMP 文案。只认名为 Label 的那个节点：
    // 按钮里还有一行 Details 小字，扫「第一个 TMP」会写错地方。
    private static void SetRowLabel(Transform row, string text)
    {
        Transform label = FindInChild(row, "Label");
        if (label == null)
        {
            Debug.LogWarning("[ScratcherSetup] " + row.name + " 下找不到 Label 节点，文案未写。");
            return;
        }
        var tmp = label.GetComponent<TMPro.TMP_Text>();
        if (tmp == null) return;
        tmp.text = text;
        EditorUtility.SetDirty(tmp);
    }

    // 行锚在父物体的左上角，x 取父物体宽度的一半 = 水平居中。
    // 用**父物体的**宽度而不是 GadgetsPanel 的：按钮现在挂在 590 宽的 Content 下，
    // 视口是 620 宽，用面板宽度会把 8 个按钮整体推右 15px。
    private static void PlaceRow(Transform row, int index)
    {
        var rect = (RectTransform)row;
        Undo.RecordObject(rect, "Gadget row");
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        float parentWidth = rect.parent is RectTransform parentRect
            ? parentRect.rect.width
            : GadgetContentWidth;
        rect.anchoredPosition = new Vector2(parentWidth * 0.5f, GadgetRowTop - index * GadgetRowStep);
        rect.sizeDelta = new Vector2(GadgetButtonWidth, GadgetButtonHeight);
        rect.localScale = Vector3.one;
        EditorUtility.SetDirty(rect);
    }

    private static Button EnsureRowButton(Transform panel, GameObject template, string name, int index,
        LotteryGame game, UnityAction action)
    {
        Transform existing = panel.Find(name);
        GameObject target;
        if (existing == null)
        {
            target = (GameObject)Object.Instantiate(template, panel);
            target.name = name;
            Undo.RegisterCreatedObjectUndo(target, name);
        }
        else target = existing.gameObject;

        PlaceRow(target.transform, index);

        var button = target.GetComponent<Button>();
        if (button == null) button = Undo.AddComponent<Button>(target);
        button.targetGraphic = target.GetComponent<Image>();

        // 先把克隆来的 onClick 清干净再加：不清的话每跑一次菜单就多挂一层，
        // 点一下会连买好几次。
        var so = new SerializedObject(button);
        var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        calls.ClearArray();
        so.ApplyModifiedPropertiesWithoutUndo();

        int callState = ReadCallState(template.GetComponent<Button>());
        UnityEventTools.AddVoidPersistentListener(button.onClick, action);
        var after = new SerializedObject(button);
        var afterCalls = after.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        if (afterCalls.arraySize > 0)
        {
            afterCalls.GetArrayElementAtIndex(afterCalls.arraySize - 1)
                .FindPropertyRelative("m_CallState").enumValueIndex = callState;
            after.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(button);
        EditorUtility.SetDirty(target);
        return button;
    }

    private static int ReadCallState(Button source)
    {
        if (source == null) return 2;   // EditorAndRuntime
        var so = new SerializedObject(source);
        var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        if (calls != null && calls.arraySize > 0)
            return calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_CallState").enumValueIndex;
        return 2;
    }

    // ---- 票 Prefab ---------------------------------------------------------

    private static readonly string[] TicketPrefabPaths =
    {
        "Assets/Prefabs/LuckyTicket_Base.prefab",
        "Assets/Prefabs/GoldTicket_Base.prefab",
        "Assets/Prefabs/NovaTicket_Base.prefab"
    };

    // 给三张票补上入场动画与拖拽组件。两个组件都是幂等的：已存在就只回写参数。
    // 命中判定 / 结算链路全部走 GetComponent 自取，所以不需要在 Prefab 上连任何引用。
    [MenuItem("Tools/挂个爽/给彩票挂入场动画与拖拽")]
    public static void PrepareTicketPrefabs()
    {
        for (int i = 0; i < TicketPrefabPaths.Length; i++)
        {
            string path = TicketPrefabPaths[i];
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogError("[ScratcherSetup] 打不开 Prefab：" + path);
                continue;
            }
            try
            {
                if (root.GetComponent<TicketFlyIn>() == null) root.AddComponent<TicketFlyIn>();
                if (root.GetComponent<TicketDragger>() == null) root.AddComponent<TicketDragger>();

                // 拖动惯性（2026-09-29）：与 SpongeDrag 同一套，用组件自己的默认值
                // （maxSpeed 90 / 减速度 320 / 反弹 0.4）。TicketDragger.Awake 会把它的
                // Clamp 指到票自己的夹取上，所以这里不需要连任何引用。
                if (root.GetComponent<DragInertia>() == null) root.AddComponent<DragInertia>();

                // 刮开后的退场：先缩小再销毁（2026-09-29）。参数同样用组件默认值
                // （0.3s / 线性 / 缩到 5%，与 AutoScratcher 出槽一致）。
                if (root.GetComponent<ShrinkOut>() == null) root.AddComponent<ShrinkOut>();

                // 票改成**按下即抓**（2026-09-29）。与刮奖的分流不再靠时间，而是靠**按在票面哪一块**：
                // 涂层（票面中间那块 96×40 texel）是刮奖区，它外面那一圈是拖动把手
                // （判定见 ScratchCard.CoversPoint / TicketDragger）。
                // 这个值必须与 TicketDragger 字段默认值一致，否则复跑一次菜单就把按下即抓改回 0.12s。
                var dragSo = new SerializedObject(root.GetComponent<TicketDragger>());
                var holdProp = dragSo.FindProperty("holdTime");
                if (holdProp != null)
                {
                    holdProp.floatValue = 0f;
                    dragSo.ApplyModifiedPropertiesWithoutUndo();
                }

                var ticket = root.GetComponent<LotteryTicket>();
                if (ticket == null)
                {
                    Debug.LogError("[ScratcherSetup] " + path + " 上没有 LotteryTicket");
                    continue;
                }
                var so = new SerializedObject(ticket);
                var kind = so.FindProperty("kind");
                if (kind != null)
                {
                    kind.intValue = i;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        Debug.Log("[ScratcherSetup] 票 Prefab 已补上 TicketFlyIn + TicketDragger，并写回 kind=0/1/2");
    }

    // ---- 回读 --------------------------------------------------------------

    private static string Report(AutoScratcher scratcher, SpriteRenderer body, GameObject machine)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("machine=").Append(machine.name)
          .Append(" pos=").Append(machine.transform.position.ToString("F2"))
          .Append(" scale=").Append(machine.transform.localScale.x.ToString("F1"))
          .Append(" sprite=").Append(body.sprite == null ? "<null>" : body.sprite.name)
          .Append(" bounds=").Append(body.bounds.size.ToString("F2")).Append(" | ");

        var so = new SerializedObject(scratcher);
        string[] fields = { "jelly", "ring", "effect", "ticketContainer" };
        foreach (string field in fields)
        {
            var prop = so.FindProperty(field);
            string value = prop == null || prop.objectReferenceValue == null ? "<null>" : prop.objectReferenceValue.name;
            sb.Append(field).Append('=').Append(value).Append(' ');
        }
        var tiers = so.FindProperty("tiers");
        sb.Append("| tiers=").Append(tiers.arraySize).Append(" : ");
        for (int i = 0; i < tiers.arraySize; i++)
        {
            var element = tiers.GetArrayElementAtIndex(i);
            var sprite = element.FindPropertyRelative("sprite").objectReferenceValue as Sprite;
            sb.Append(element.FindPropertyRelative("displayName").stringValue).Append('(')
              .Append(sprite == null ? "null" : sprite.name + " pivotY=" + sprite.pivot.y.ToString("F1"))
              .Append(" content=").Append(element.FindPropertyRelative("contentRect").vector4Value.ToString())
              .Append(") ");
        }

        var game = Object.FindAnyObjectByType<LotteryGame>();
        if (game != null)
        {
            var gso = new SerializedObject(game);
            sb.Append("| game: scratcher=").Append(Name(gso, "scratcher"))
              .Append(" unlock=").Append(Name(gso, "scratcherUnlockButton"))
              .Append(" speed=").Append(Name(gso, "scratcherSpeedButton"))
              .Append(" cap=").Append(Name(gso, "scratcherCapacityButton"));
        }
        return sb.ToString();
    }

    private static string Name(SerializedObject so, string field)
    {
        var prop = so.FindProperty(field);
        return prop == null || prop.objectReferenceValue == null ? "<null>" : prop.objectReferenceValue.name;
    }
}
