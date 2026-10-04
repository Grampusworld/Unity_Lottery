using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 装配「通关目标」三件套：
//   ① 桌子正上方蓝带里的 GOAL 牌子（GoalBanner）
//   ② 主菜单画布下的 VictoryPanel 结算面板（VictoryPanel 脚本）
//   ③ LotteryGame.goalBanner / MainMenuScreen 的三个引用回填
//
// 菜单：Tools/挂个爽/装配 GOAL 牌子和通关结算
//
// ==================== 排版（全部由实测反推，不要凭感觉改） ====================
// 桌子是**世界空间**物体（Table 在 z=0 的 SpriteRenderer，相机 ortho 50 覆盖 178×100 世界单位）。
// 2560×1440 画布下逐像素量出来的「桌子顶边以上的蓝带」= x[650, 2559]、y[0, 96]，高仅 97px。
// 上移桌子去扩高这条带要连带改 PlateSpawnPoint / TicketSpawnPoint / DragBounds /
// 两台机器的设计位一整套世界锚点 —— 为 30px 垂直空间去动世界布局不值得。UI 层加高才是免费的。
//
// 四元素**并排居中**于蓝带中心 x = (650+2559)/2 = 1604.5：
//   GOAL $2,000,000fs40  16字符 × 40 = 640px  x=706   y 30..65（视觉高 7×40/8=35）
//   进度条 700×8x=1386  y 44..52
//   42%                fs24  ≤4字符 × 24 = 96px    x=2126  y 37..58（视觉高 21）
//   MULTIPLIER          fs16  10字符 × 16 = 160px  x=2262  y 20..34（视觉高 14）
//   X114.70             fs32   7字符 × 32 = 224px  x=2262  y 46..74（视觉高 28）
//   总宽 640+40+700+40+96+40+240 = 1796，居中校验 (706 + 1796/2) = 1604 ✓
//   两侧余量各 56px。
//
// ==================== 为什么是这组尺寸（2026-10-04改） ====================
// ① **LabelWidth 320 → 640**：MoneyFormat 不再缩写（要求 2），`GOAL  $2,000,000`
//    是 16 字符 = 640px，原来 320 的框只够`GOAL  $2M`（8 字符）。
// ② **BarWidth 900 → 700**：整行要腾出右侧240px 给倍率文本。**不改字号改条宽**——
//    字号必须留在 8 的倍数上（原生网格 8px），缩字号会让 GOAL 这条最重要的信息变小；
//    进度条从 900 缩到 700 仍然远超「看得清一条进度」所需，且两端各留 40px gap。
// ③ **倍率用「小标题 + 大数值」两行**而不是一行 `MULTIPLIER  X114.70`：
//    单行那串是 19 字符 = 456px，会把条再压到 520px（900→520 太狠）。
//    拆成两行后整块只要 240px，条能保住 700px。
//    小标题 fs16 仍然 > 0 且是 8 的倍数，不引入非整数缩放。
//
// **字号必须是 8 的倍数**（原生网格 8px，字宽 = 字号，见 ref-shop-ui.md §5）。
//
// ==================== 幂等 ====================
// 已存在同名物体就复用并只回填引用，绝不删除重建 —— 复跑必须 0 差异。
public static class GoalSetup
{
    // ---- GOAL 牌子几何（与文件头排版段逐项对应）----
    private const float LabelFontSize = 40f;
    private const float PercentFontSize = 24f;
    // 倍率小标题/ 数值分两行，各占一个文本框，共用一个 240px 的横向槽位。
    private const float MultiplierCaptionFontSize = 16f;
    private const float MultiplierValueFontSize = 32f;
    private const float MultiplierWidth = 240f;
    private const float BarWidth = 700f;
    private const float BarHeight = 8f;
    private const float LabelWidth = 640f;
    private const float PercentWidth = 96f;
    private const float Gap = 40f;
    // 三元素的垂直中心（距画布顶端）。97px 蓝带的正中 = 48。
    private const float RowY = 48f;
    // 倍率两行各自的垂直中心（距画布顶端）。小标题偏上、数值偏下，整体仍落在 97px 带内。
    private const float MultiplierCaptionY = 27f;
    private const float MultiplierValueY = 64f;

    private const string BannerName = "GoalBanner";
    private const string VictoryPanelName = "VictoryPanel";

    private static readonly Color32 Cream = new Color32(246, 235, 205, 255);
    private static readonly Color32 Gold = new Color32(234, 179, 76, 255);
    private static readonly Color32 Navy = new Color32(20, 27, 43, 255);
    private static readonly Color32 Card = new Color32(28, 44, 69, 255);
    private static readonly Color32 TrackDark = new Color32(11, 18, 28, 255);
    private static readonly Color32 Teal = new Color32(73, 153, 158, 255);
    private static readonly Color32 TealDim = new Color32(132, 155, 164, 255);

    [MenuItem("Tools/挂个爽/装配 GOAL 牌子和通关结算")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Exit Play mode first.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
            throw new InvalidOperationException("Open Assets/Scenes/SampleScene.unity first.");

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/PressStart2P-Regular.asset");
        if (font == null) throw new InvalidOperationException("Pixel TMP font asset is missing.");

        var game = UnityEngine.Object.FindAnyObjectByType<LotteryGame>();
        var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuScreen>();
        if (game == null || menu == null)
            throw new InvalidOperationException("GameManager/MenuOverlay missing.");

        BuildBanner(font, game);
        BuildVictoryPanel(font, menu);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("LOTTERY MANIA: GOAL banner + Victory panel assembled.");
    }

    // ==================== ① GOAL 牌子 ====================
    private static void BuildBanner(TMP_FontAsset font, LotteryGame game)
    {
        var root = BuildBannerRoot();

        // 四元素并排，总宽 = 640 + 40 + 700 + 40 + 96 + 40 + 240 = 1796，居中于 x=1604 → 起点 706。
        // 各自中心 = 起点 + 已排过的宽度 + 自身宽度/2。**逐项算出来写死**，
        // 别用「起点 + 累计」在运行时推 —— 那样改一个宽度要重读三行才看得出来对不对。
        // 全部走 At()：下面这些数字是「距画布左上角」，不是 anchoredPosition。
        // 起点不再写死 906 —— 它由总宽居中算出来（StartX()），改任何一段宽度它自动重算，
        // 不会像以前那样「改了一处就默默偏出蓝带」。
        float startX = StartX();
        var label = Text(root, "GoalLabel", "GOAL  $2,000,000", font,
            At(startX + LabelWidth / 2f, RowY), LabelWidth, 44f, LabelFontSize, Gold,
            TextAlignmentOptions.Left);

        var barRoot = Rect(root, "ProgressBar",
            At(startX + LabelWidth + Gap + BarWidth / 2f, RowY),
            new Vector2(BarWidth, BarHeight));
        var track = EnsureImage(barRoot.gameObject);
        track.color = TrackDark; track.raycastTarget = false;

        // Fill 拉伸满父物体，fillAmount 是唯一被每帧写的值。
        // 拉伸锚点下 fill 天然从左边缘长出去，不需要算 pivot 或 anchoredPosition；
        // Filled 模式按 fillAmount 裁剪绘制，0% 时只是不画，不会影响布局。
        var fill = Stretch(barRoot, "Fill", new Vector2(0f, 0.5f));
        // **必须用 EnsureImage 而不是 AddComponent**：复跑时 Fill 已存在 Image，
        // AddComponent 会抛 "already added"，把整个菜单中断在半路——
        // 症状是「日志只有异常、场景半成品」（根节点拉伸写了、Fill 没写）。
        // EnsureImage 就是为这条而存在的，和按钮/内衬的处理一致。
        var fillImage = EnsureImage(fill.gameObject);
        fillImage.color = Cream; fillImage.raycastTarget = false;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0f;

        var percent = Text(root, "PercentLabel", "0%", font,
            At(startX + LabelWidth + Gap + BarWidth + Gap + PercentWidth / 2f, RowY),
            PercentWidth, 30f, PercentFontSize, Cream, TextAlignmentOptions.Left);

        // 倍率：两行 —— 小标题「MULTIPLIER」+ 大数值「X114.70」。
        // **caption 与 value 分开两个框**，而不是一个框里用 \n：GoalBanner 要分别
        // 控制两者的字号与颜色（数值随倍率变、标题恒定），一个框只能共用一个字号。
        float multX = startX + LabelWidth + Gap + BarWidth + Gap + PercentWidth + Gap
            + MultiplierWidth / 2f;
        var multCaption = Text(root, "MultiplierCaption", "MULTIPLIER", font,
            At(multX, MultiplierCaptionY), MultiplierWidth, 20f, MultiplierCaptionFontSize,
            TealDim, TextAlignmentOptions.Left);
        var multValue = Text(root, "MultiplierValue", "X1.00", font,
            At(multX, MultiplierValueY), MultiplierWidth, 36f, MultiplierValueFontSize,
            Gold, TextAlignmentOptions.Left);

        var banner = root.GetComponent<GoalBanner>();
        if (banner == null) banner = root.gameObject.AddComponent<GoalBanner>();
        var data = new SerializedObject(banner);
        Put(data, "goalLabel", label);
        Put(data, "barFill", fillImage);
        PutFloat(data, "barWidth", BarWidth);
        Put(data, "percentLabel", percent);
        Put(data, "multiplierLabel", multValue);
        data.ApplyModifiedPropertiesWithoutUndo();

        var gameData = new SerializedObject(game);
        Put(gameData, "goalBanner", banner);
        gameData.ApplyModifiedPropertiesWithoutUndo();
    }

    // 四段总宽居中于蓝带。**这里返回的是「距画布左边」的距离**，不是 anchoredPosition。
    // 把居中逻辑收在一处：改任何一段宽度，起点自动跟着走，
    // 而调用点只管按顺序摆元素 —— 以前起点是写死的 906，加倍率那段就必然偏出蓝带。
    private static float StartX()
    {
        float rowWidth = LabelWidth + Gap + BarWidth + Gap + PercentWidth + Gap + MultiplierWidth;
        return (BandLeft + BandRight) / 2f - rowWidth / 2f;
    }

    // 蓝带实测左右边界（2560×1440 画布坐标，见文件头排版段）。
    private const float BandLeft = 650f;
    private const float BandRight = 2559f;

    // ==================== ② 结算面板 ====================
    // 挂在 MenuOverlay 下（与 PausePanel / SettingsPanel 同级）——
    // Show() 只负责 SetActive，遮罩与暂停由 MainMenuScreen 统一管。
    private static void BuildVictoryPanel(TMP_FontAsset font, MainMenuScreen menu)
    {
        var overlay = menu.transform.Find("MenuOverlay");
        if (overlay == null) throw new InvalidOperationException("MenuOverlay not found.");

        var panel = FindOrCreate(overlay, VictoryPanelName);
        var rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(760f, 560f);
        // 面板内部的坐标是**面板中心原点、y 向上**（也就是 anchoredPosition 的原样）——
        // 与牌子的画布坐标不同制式，所以这里不套 At()。下面的 y 都以面板中心为 0：
        // 面板半高 280，所以 y=248 距面板顶端 32px，y=-240 距底端 40px。

        // 抄 NewGameConfirmation 的结构：金边框 + NavyInset 内缩 4。
        var inset = EnsureChildObject(panel, "NavyInset");
        var insetRect = inset.GetComponent<RectTransform>();
        insetRect.anchorMin = Vector2.zero; insetRect.anchorMax = Vector2.one;
        insetRect.offsetMin = new Vector2(4f, 4f); insetRect.offsetMax = new Vector2(-4f, -4f);
        EnsureImage(inset).color = Navy;
        EnsureImage(panel.gameObject).color = Gold;

        // 标题 24 字符。fs24 下 = 576px，面板 760 宽减去四边留白后放得下；
        // fs32 会是 768px > 760，NoWrap 下直接溢出金色边框，所以**只能用 fs24**。
        var title = Text(panel, "Title", "YOU REACHED THE GOAL!", font, new Vector2(0f, 228f), 700f, 40f,
            24f, Gold, TextAlignmentOptions.Center);

        var divider = Rect(panel, "Divider", new Vector2(0f, 190f), new Vector2(600f, 3f));
        EnsureImage(divider.gameObject).color = Teal;

        // 四行统计。
        // **x 是「宽度 580 的框的中心」，不是文本左边缘** —— pivot (0.5,0.5) 下
        // 框实际占 [x-290, x+290]。曾经写成 x=-290，框就落在 [-580, 0]，
        // 而面板内容区只有 [-376, 376]：首个字形从 -580 起笔，整个标签列
        // （MONEY/TIME/TICKETS/SPEED）被推到金色边框外面，屏幕上只剩金额。
        // 症状极具迷惑性 —— 面板边框、标题、按钮全都正常，只有前缀消失，
        // 很容易被当成「字体缺字形」或「字符串拼接漏了标签」去查错方向。
        // 正确值：内容区左缘 -376 + 框宽一半 290 = -86。
        // 最长一行 `MULTIPLIER   X114.70` = 20 字符 × 24 = 480px，框内放得下。
        var money = Text(panel, "MoneyLine", "MONEY   $2,000,000", font, new Vector2(-86f, 128f), 580f, 32f,
            24f, Cream, TextAlignmentOptions.Left);
        var time = Text(panel, "TimeLine", "TIME    40 MIN 12 SEC", font, new Vector2(-86f, 82f), 580f, 32f,
            24f, Cream, TextAlignmentOptions.Left);
        var tickets = Text(panel, "TicketLine", "TICKETS 148", font, new Vector2(-86f, 36f), 580f, 32f,
            24f, Cream, TextAlignmentOptions.Left);
        // 标签从 SPEED 换成 MULTIPLIER（10 字符）后，这行成了面板里最长的一行。
        var speed = Text(panel, "SpeedLine", "MULTIPLIER   X114.70", font, new Vector2(-86f, -10f), 580f, 32f,
            24f, Cream, TextAlignmentOptions.Left);

        var hint = Text(panel, "Hint", "ESC TO CONTINUE", font, new Vector2(0f, -60f), 600f, 26f,
            24f, TealDim, TextAlignmentOptions.Center);

        // 两个按钮并排。**不给 NEW GAME**：结算画面上直接清档太危险，
        // 想重开玩家回主菜单点 NEW GAME（那里还有二次确认）。
        var cont = Button(panel, "ContinueButton", "CONTINUE", font, -160f, -172f, 300f, 76f,
            menu.ResumeFromVictory, 24);
        var toMenu = Button(panel, "MainMenuButton", "MAIN MENU", font, 160f, -172f, 300f, 76f,
            menu.VictoryToMainMenu, 24);

        var script = panel.GetComponent<VictoryPanel>();
        if (script == null) script = panel.gameObject.AddComponent<VictoryPanel>();
        var data = new SerializedObject(script);
        Put(data, "titleLabel", title);
        Put(data, "moneyLabel", money);
        Put(data, "timeLabel", time);
        Put(data, "ticketLabel", tickets);
        Put(data, "multiplierLabel", speed);
        Put(data, "hintLabel", hint);
        data.ApplyModifiedPropertiesWithoutUndo();

        var menuData = new SerializedObject(menu);
        Put(menuData, "victoryPanel", panel.gameObject);
        Put(menuData, "victoryContinueButton", cont);
        Put(menuData, "victoryMenuButton", toMenu);
        menuData.ApplyModifiedPropertiesWithoutUndo();

        // 初始隐藏：Show() 会在进入 Playing 时把它关掉，但装配完的场景不该有它开着
        // —— 否则玩家一进游戏就看到结算面板盖在上面。
        panel.gameObject.SetActive(false);
    }

    // ==================== 坐标系换算（唯一入口） ====================
    // 本文件里所有排版常量都用**画布局部坐标：左上为原点、x 向右、y 向下**
    // —— 与逐像素量出来的表格、也与人眼读截图的方式一致。
    // 而 RectTransform.anchoredPosition 是**父中心为原点、y 向上**。
    // 两者差一个 (1280, -720) 的偏移，直接把 906/48 填进 anchoredPosition 的后果是
    // 整排元素跑到 x=2186..3582、y≈768（画面右上方之外）——
    // **而且所有对象状态看着都正常**：activeSelf/alpha/fillAmount 无一异常，
    // 只是「在屏幕外」。所以换算必须收在一处，不许谁直接写 anchoredPosition。
    private const float CanvasW = 2560f;
    private const float CanvasH = 1440f;

    private static Vector2 At(float x, float yFromTop)
    {
        return new Vector2(x - CanvasW / 2f, CanvasH / 2f - yFromTop);
    }

    private static Canvas gameCanvas()
    {
        var go = GameObject.Find("Canvas");
        if (go == null) throw new InvalidOperationException("Game Canvas missing.");
        return go.GetComponent<Canvas>();
    }

    // GOAL 牌子挂在**游戏画布**下（不是 MainMenuCanvas）：它是游戏 HUD 的一部分，
    // 结算面板弹出时它要被遮罩盖住 —— 若挂在 MainMenuCanvas 里（sortingOrder 100），
    // 牌子会和结算面板同级，「遮住游戏画面」这件事就漏了它。
    //
    // **根节点必须拉伸满画布**：子物体全部锚在 (0.5,0.5) 并用画布局部坐标定位，
    // 根节点若保持新建RectTransform 的默认 100×100，它的中心就不在画布中心，
    // 整排牌子会被整体平移（症状：三个元素全部跑到屏幕右上方外，
    // 而所有对象状态看着都正常 —— activeSelf/alpha/fillAmount 无一异常）。
    private static RectTransform BuildBannerRoot()
    {
        var rt = FindOrCreate(gameCanvas().transform, BannerName);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    private static RectTransform FindOrCreate(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing.GetComponent<RectTransform>();
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    // FindOrCreate 返回 RectTransform，但 EnsureImage / AddComponent 要的是 GameObject ——
    // 这里显式收窄一次，别让调用点在 RectTransform 和 GameObject 之间来回 `.gameObject`（易错且啰嗦）。
    private static GameObject EnsureChildObject(Transform parent, string name)
    {
        return FindOrCreate(parent, name).gameObject;
    }

    private static Image EnsureImage(GameObject go)
    {
        var image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        return image;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var rt = FindOrCreate(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    // 拉伸满父物体。anchorMin 给 pivot.x（左对齐就传 0），anchorMax 恒为右上角，
    // offset 全 0 —— 所以子物体的左/右/上/下边分别贴父物体的左/右/上/下边。
    private static RectTransform Stretch(Transform parent, string name, Vector2 pivot)
    {
        var rt = FindOrCreate(parent, name);
        rt.anchorMin = new Vector2(pivot.x, 0f);
        rt.anchorMax = Vector2.one;
        rt.pivot = pivot;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    // pos 直接写进 anchoredPosition —— 父级是画布根就用 At()，父级是面板就用面板中心坐标。
    private static TextMeshProUGUI Text(Transform parent, string name, string value,
        TMP_FontAsset font, Vector2 pos, float width, float height, float size,
        Color color, TextAlignmentOptions align)
    {
        var rt = FindOrCreate(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, height);
        var text = rt.GetComponent<TextMeshProUGUI>();
        if (text == null) text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.text = value;
        text.alignment = align;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(Transform parent, string name, string label, TMP_FontAsset font,
        float x, float y, float width, float height, UnityEngine.Events.UnityAction callback, float fontSize)
    {
        var rt = FindOrCreate(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, height);

        var image = EnsureImage(rt.gameObject);
        image.color = Gold;
        var button = rt.GetComponent<Button>();
        if (button == null) button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        var inset = EnsureChildObject(rt, "Inset");
        var insetRect = inset.GetComponent<RectTransform>();
        insetRect.anchorMin = Vector2.zero; insetRect.anchorMax = Vector2.one;
        insetRect.offsetMin = new Vector2(3f, 3f); insetRect.offsetMax = new Vector2(-3f, -3f);
        EnsureImage(inset).color = Card;

        // 按钮 Label：左右各内缩 12，垂直居中。fs24 下 `MAIN MENU` = 9 字符 = 216px < 276 可用。
        Text(rt, "Label", label, font, new Vector2(0f, 0f), width - 24f, height - 12f,
            fontSize, Cream, TextAlignmentOptions.Center);

        // 幂等的持久化监听：先清掉本组件上已有的同 target 监听再挂，
        // 否则复跑菜单会挂出两个 listener（点一下执行两次）。
        UnityEventTools.RemovePersistentListener(button.onClick, callback);
        UnityEventTools.AddPersistentListener(button.onClick, callback);
        return button;
    }

    private static void Put(SerializedObject data, string field, UnityEngine.Object value)
    {
        var prop = data.FindProperty(field);
        if (prop == null) throw new InvalidOperationException("Missing field: " + field);
        prop.objectReferenceValue = value;
    }

    private static void PutFloat(SerializedObject data, string field, float value)
    {
        var prop = data.FindProperty(field);
        if (prop == null) throw new InvalidOperationException("Missing field: " + field);
        prop.floatValue = value;
    }
}
