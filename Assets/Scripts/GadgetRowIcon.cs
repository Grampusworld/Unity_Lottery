using UnityEngine;
using UnityEngine.UI;

// GADGETS 行左侧图标的运行时驱动。
//
// 【进度语义只有这一处定义】
//   槽位数固定（= 满级 + 1，由装配菜单写死进场景），点亮数 = 当前等级 + 1。
// 为什么不画真实值：WASHER CAPACITY 真实容量 5→15、SCRATCHER CAPACITY 真实槽位 1→6。
// 按真实值画，15 个盘子挤在 60px 里每个不到 4px（等于没画），而且各行不同构。
// 归一化之后所有行同构，且**满级必然全亮**（filled == total）—— 这是一个可验证的不变量，
// 见 IsFullyLit。
//
// 【为什么不用 LateUpdate 每帧刷】
// RefreshUI 已经在每帧写 Label 文案，图标跟它同一个入口就够了。组件本身无 Update：
// 静态图标（海绵 / 解锁入口 / 自动投喂）只会被写一次，升级型只在等级变化时被写。
//
// 【颜色通道】
// 点亮/未点亮走 Image.color（乘性）。素材本身带颜色，乘一个偏冷的暗蓝 = "空槽"，
// 乘白色 = 原色。满级再乘金色 CompletedColor，与按钮转 Completed 时的金底/金边同色（198,158,64）。
public class GadgetRowIcon : MonoBehaviour
{
    [SerializeField] private Image baseArt;
    [SerializeField] private Image[] slots = new Image[0];
    [SerializeField] private int filled;
    [SerializeField] private bool completed;

    // 点亮 = 原色（白乘）；未点亮 = 冷暗蓝压暗 + 半透，读作"空槽"。
    private static readonly Color LitColor = Color.white;
    private static readonly Color UnlitColor = new Color(0.42f, 0.49f, 0.62f, 0.45f);
    private static readonly Color CompletedColor = new Color32(198, 158, 64, 255);

    public int Total => slots != null ? slots.Length : 0;
    public int Filled => filled;
    public bool Completed => completed;

    /// <summary>装配菜单用（SerializedObject 回写）。运行时只读。</summary>
    public void Configure(Image baseImage, Image[] slotImages)
    {
        baseArt = baseImage;
        slots = slotImages ?? new Image[0];
        filled = 0;
        completed = false;
        ApplyColors();
    }

    /// <summary>换基础图案。SCRATCHER 解锁行用它按 tier 切 V3/V1/V2 三张机身。</summary>
    public void SetBaseSprite(Sprite sprite)
    {
        if (baseArt == null || baseArt.sprite == sprite) return;
        baseArt.sprite = sprite;
        ApplyColors();
    }

    /// <summary>
    /// 写进度。<paramref name="value"/> 是归一化后的点亮数（不是等级）。
    /// 同值早退：RefreshUI 每帧调用，不早退就是每帧给几十个 Graphic 标脏。
    /// </summary>
    public void SetProgress(int value, bool isCompleted)
    {
        int clamped = Mathf.Clamp(value, 0, Total);
        if (clamped == filled && isCompleted == completed) return;
        filled = clamped;
        completed = isCompleted;
        ApplyColors();
    }

    // 满级不变量：等级到顶时点亮数必然等于槽位数。供自检与验收使用。
    public bool IsFullyLit => Total > 0 && filled >= Total;

    private void ApplyColors()
    {
        // **满级金色只叠在槽位上，不叠基础图案**（2026-10-04 改）。
        // Image.color 是乘性通道：金 (198,158,64) × 紫海绵 (150,80,180) = 暗红棕色，
        // 截图里那块海绵变成一坨脏颜色，比不染还糟。基础图案保持原色（白乘），
        // "这行满了"由满亮的金色槽位 + 按钮转 Completed 金底共同表达。
        if (baseArt != null && baseArt.color != LitColor) baseArt.color = LitColor;
        if (slots == null) return;
        Color lit = completed ? CompletedColor : LitColor;
        for (int i = 0; i < slots.Length; i++)
        {
            Image slot = slots[i];
            if (slot == null) continue;
            Color want = i < filled ? lit : UnlitColor;
            if (slot.color != want) slot.color = want;
        }
    }
}
