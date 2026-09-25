using System;
using UnityEngine;

// 悬停果冻反馈的全局开关。
// Unity 没有 prefers-reduced-motion 这种原生 API（那是 CSS 媒体查询），
// 这里用 PlayerPrefs 存一个项目内开关，Debug 面板可实时切换。
// 以后若要接系统设置，只需改 ReducedMotion 的 getter 初值来源，其余代码不用动。
public static class HoverJellySettings
{
    private const string Key = "LotteryPrototype.v1.ReduceMotion";

    // 开关变化时广播，所有 HoverJelly 实例会重读参数并把动画拉回复位。
    public static event Action Changed;

    public static bool ReducedMotion
    {
        get => PlayerPrefs.GetInt(Key, 0) != 0;
        set
        {
            int next = value ? 1 : 0;
            if (PlayerPrefs.GetInt(Key, 0) == next) return;
            PlayerPrefs.SetInt(Key, next);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    public static void Toggle() => ReducedMotion = !ReducedMotion;
}
