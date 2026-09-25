using TMPro;
using UnityEngine;

// Debug 面板上的 reduced-motion 开关。独立组件，不需要改 LotteryGame。
// 挂在有按钮的面板上，把按钮的 Label 拖进来，OnClick 指向 Toggle()。
public class HoverJellyDebugToggle : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private void OnEnable()
    {
        HoverJellySettings.Changed += Refresh;
        Refresh();
    }

    private void OnDisable() => HoverJellySettings.Changed -= Refresh;

    public void Toggle() => HoverJellySettings.Toggle();

    private void Refresh()
    {
        if (label != null)
            label.text = "REDUCE MOTION  " + (HoverJellySettings.ReducedMotion ? "ON" : "OFF");
    }
}
