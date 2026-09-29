using UnityEngine;

// 「能拖到哪儿」的几何定义，只有一份。
//
// 海绵那套参数（tableSurface + tabletopPixels）是场景里手摆出来的实测值，
// 机器的可拖范围必须读**同一份**，不能手抄第二份 —— 抄了以后改桌面美术就会对不上。
// 所以这里只提供算法，参数由各自从海绵实例读（见 MachineDrag.Awake 的回退）。
public static class DragBounds
{
    // 相机在给定深度上的可视矩形（正交相机；半透明相机用 ViewportToWorldPoint 才拿得到
    // 真实 pixelRect —— 编辑器里 Screen.width 给的是 Game 视图面板尺寸）。
    public static Bounds Visible(Camera camera, float z)
    {
        float depth = Mathf.Abs(camera.transform.position.z - z);
        Vector3 min = camera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 max = camera.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
        return new Bounds((min + max) * 0.5f,
            new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), 1f));
    }

    // Table.png 中「有颜色的桌面」在贴图里的像素矩形 → 世界矩形。
    // 相机可视区只露出一部分桌子时，这个矩形会跑到屏幕外，所以下面还要取交集。
    public static Bounds Tabletop(SpriteRenderer tableSurface, Rect tabletopPixels, Bounds fallback)
    {
        if (tableSurface == null || tableSurface.sprite == null) return fallback;
        Sprite sprite = tableSurface.sprite;
        Vector2 pivot = sprite.pivot;
        float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
        Vector3 low = tableSurface.transform.TransformPoint(new Vector3(
            (tabletopPixels.xMin - pivot.x) / ppu, (tabletopPixels.yMin - pivot.y) / ppu));
        Vector3 high = tableSurface.transform.TransformPoint(new Vector3(
            (tabletopPixels.xMax - pivot.x) / ppu, (tabletopPixels.yMax - pivot.y) / ppu));
        Bounds bounds = new Bounds();
        bounds.SetMinMax(Vector3.Min(low, high), Vector3.Max(low, high));
        return bounds;
    }

    // 可拖范围 = 桌面内表面 ∩ 相机可视区。
    // 屏幕比桌子大时以桌子为准（拖不出桌沿），反过来以屏幕为准（别拖出画面）。
    public static Bounds Playable(SpriteRenderer tableSurface, Rect tabletopPixels, Camera camera, float z)
    {
        Bounds visible = Visible(camera, z);
        Bounds table = Tabletop(tableSurface, tabletopPixels, visible);
        Bounds result = new Bounds();
        result.SetMinMax(Vector3.Max(table.min, visible.min), Vector3.Min(table.max, visible.max));
        return result;
    }

    // 把「可见包围盒中心」夹进可拖范围。half 是可见包围盒的一半，margin 是额外余量系数。
    // 中心小于半宽时（桌子比物体还窄）退化成居中，而不是把 min > max 传进 Clamp。
    public static Vector3 ClampCenter(Vector3 center, Vector2 half, float margin, Bounds area)
    {
        float halfX = half.x * margin;
        float halfY = half.y * margin;
        float minX = area.min.x + halfX;
        float maxX = area.max.x - halfX;
        float minY = area.min.y + halfY;
        float maxY = area.max.y - halfY;
        center.x = minX <= maxX ? Mathf.Clamp(center.x, minX, maxX) : (minX + maxX) * 0.5f;
        center.y = minY <= maxY ? Mathf.Clamp(center.y, minY, maxY) : (minY + maxY) * 0.5f;
        return center;
    }
}
