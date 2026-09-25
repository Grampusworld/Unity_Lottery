using UnityEngine;
using UnityEngine.UI;

// ponytail: 位图（像素）字体必须按整数倍放大，否则每个 texel 会被拉成 2.076 之类的
// 非整数像素 —— 笔画忽粗忽细，字形边缘还会把图集里隔壁的字形采样进来（"A" 变成 "8"）。
//
// CanvasScaler 的 ScaleWithScreenSize 会算出 1.186 这类小数缩放，本脚本沿用它的
// 参考分辨率算法，但把结果吸附到整数，保证 1 个字体 texel = N 个屏幕像素。
[ExecuteAlways]
[RequireComponent(typeof(CanvasScaler))]
public class PixelPerfectCanvasScaler : MonoBehaviour
{
    [Tooltip("缩放下限。低于 1 会把 UI 压到半个像素，比放大更糊，所以默认锁 1。")]
    public int minScaleFactor = 1;

    Canvas canvas;
    CanvasScaler scaler;
    int lastApplied = -1;

    void OnEnable()
    {
        canvas = GetComponent<Canvas>();
        scaler = GetComponent<CanvasScaler>();
        lastApplied = -1;
    }

    void LateUpdate()
    {
        if (canvas == null || scaler == null) return;

        Vector2 refRes = scaler.referenceResolution;
        if (refRes.x <= 0 || refRes.y <= 0) return;

        // 取真实渲染尺寸。不能用 Screen.width/height —— 编辑器里它返回的是 Game 视图
        // 面板尺寸（实测 1786x906），而实际渲染是 2560x1440，会算出完全错误的缩放。
        Rect pr = canvas.rootCanvas.pixelRect;
        float screenW = pr.width > 0f ? pr.width : Screen.width;
        float screenH = pr.height > 0f ? pr.height : Screen.height;
        if (screenW <= 0f || screenH <= 0f) return;

        // 与 CanvasScaler.ScaleWithScreenSize 完全相同的算法
        float logW = Mathf.Log(screenW / refRes.x, 2f);
        float logH = Mathf.Log(screenH / refRes.y, 2f);
        float raw = Mathf.Pow(2f, Mathf.Lerp(logW, logH, scaler.matchWidthOrHeight));

        int snapped = Mathf.Max(minScaleFactor, Mathf.RoundToInt(raw));

        // 只在数值变化时赋值，避免每帧触发 Canvas 重新布局
        if (snapped != lastApplied || !Mathf.Approximately(canvas.scaleFactor, snapped))
        {
            canvas.scaleFactor = snapped;
            lastApplied = snapped;
        }
    }
}
