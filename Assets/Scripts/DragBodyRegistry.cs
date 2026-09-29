using System.Collections.Generic;
using UnityEngine;

// 可拖/可挡物体的实体占位登记处。只做三件事：
//   ① 谁跟谁能撞（Collides）
//   ② 把「想去的位置」按最小穿透轴推回合法区（Resolve）
//   ③ 这次按下该让给谁（Hit）
//
// 为什么需要全局登记处，而不是各拖拽组件互相持有引用：
//   盘子和票都是运行时 Instantiate 的，编辑器里根本没有连线对象；
//   而「机器 ↔ 海绵 ↔ 盘子」是三条彼此独立的碰撞对，写死在某一方里一定会漏。
//
// 碰撞对（2026-09-29 定）：
//   机器 ↔ 机器    ✓   两台机器互斥，先到的占着
//   机器 ↔ 海绵    ✓   海绵靠拖动去够盘子，被机器碾住就没法玩了
//   机器 ↔ 盘子    ✓   防止机器压住已经落桌的盘子
//   海绵 ↔ 盘子    ✗   **强制排除**。海绵是拿自己的位置去调 DirtyPlate.ScrubAt 擦涂层的，
//                      一旦被挡住，擦盘子这个核心玩法直接失效
//   票 ↔ 任何      ✗   投喂判定用的是**光标位置**（TicketDragger.Drop 传的是 world，
//                      不是票的位置），票被挡住也照样能投喂；而且票比机器还宽，
//                      撞起来会被弹得很怪
//
// 让路规则（抓取仲裁，见 Hit 的调用方）：
//   机器必须把这次按下让给海绵和票。海绵是层次一致的（order 5 > 机器 4）；
//   票画在机器**下面**（order 0~2 < 4），但必须让 —— 票要按住 0.12s 才拿得起来，
//   机器在按下那一帧就抢走了，票一旦被压在机器上就永远抓不回来（软锁）。
//   这是「可用性优先于层次一致」的一处刻意例外。
public enum DragBodyKind
{
    Machine,
    Sponge,
    Plate,
    Ticket
}

public static class DragBodyRegistry
{
    private struct Entry
    {
        // 存组件引用、用 ReferenceEquals 比较，而不是 instance ID：
        // ① 6.5 起 GetInstanceID 已废弃（CS0619 是错误级）；
        // ② Unity 重载了 `==`，两个**都已销毁**的组件会互相「相等」，
        //    用 == 找自己会在极端情况下匹配错行 —— ReferenceEquals 是纯引用身份，不受影响。
        public Component owner;
        public DragBodyKind kind;
        public Transform tr;
        /// transform.position → **可见包围盒中心** 的世界偏移。
        /// 不能假定恒为 0：刮票机的 pivot 在内容底边，洗盘机的 pivot 也不在内容中心，
        /// 拿 transform.position 当矩形中心会让夹取和碰撞整体上下偏一段。
        public Vector3 centerOffset;
        public Vector2 half;
    }

    private const int ResolvePasses = 4;

    private static readonly List<Entry> entries = new List<Entry>();

    // 登记（幂等：同一个 owner 重复登记就是覆盖）。几何量变了直接再调一次即可 ——
    // 刮票机的 footprint 会随等级变宽（44×52 / 46×59 / 52×61 texel），必须能刷新。
    public static void Register(Component owner, DragBodyKind kind, Transform tr,
        Vector3 centerOffset, Vector2 half)
    {
        if (owner == null || tr == null) return;
        for (int i = 0; i < entries.Count; i++)
        {
            if (!ReferenceEquals(entries[i].owner, owner)) continue;
            Entry existing = entries[i];
            existing.centerOffset = centerOffset;
            existing.half = half;
            entries[i] = existing;
            return;
        }
        entries.Add(new Entry
        {
            owner = owner,
            kind = kind,
            tr = tr,
            centerOffset = centerOffset,
            half = half
        });
    }

    public static void Unregister(Component owner)
    {
        if (owner == null) return;
        for (int i = entries.Count - 1; i >= 0; i--)
            if (ReferenceEquals(entries[i].owner, owner)) entries.RemoveAt(i);
    }

    public static bool Collides(DragBodyKind a, DragBodyKind b)
    {
        if (a == b) return true;                    // 同类互斥（两台机器、两台海绵……）
        bool machine = a == DragBodyKind.Machine || b == DragBodyKind.Machine;
        bool sponge = a == DragBodyKind.Sponge || b == DragBodyKind.Sponge;
        bool plate = a == DragBodyKind.Plate || b == DragBodyKind.Plate;
        if (machine && sponge) return true;
        if (machine && plate) return true;
        return false;
    }

    // 这次按下的世界点是否落在某一类物体的包围盒里。给「让路」用。
    public static bool Hit(Vector3 world, DragBodyKind kind)
    {
        PruneDead();
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry.kind != kind || entry.tr == null) continue;
            Vector3 center = entry.tr.position + entry.centerOffset;
            if (Mathf.Abs(world.x - center.x) > entry.half.x) continue;
            if (Mathf.Abs(world.y - center.y) > entry.half.y) continue;
            return true;
        }
        return false;
    }

    // 把 desired 推回合法区。origin 是「这次移动之前的位置」——
    // 推 ResolvePasses 轮还在重叠（被两台物体夹住）时整个作废、退回 origin，
    // 而不是把机身塞进一个非法位置。
    public static Vector3 Resolve(Component self, Vector3 origin, Vector3 desired)
    {
        PruneDead();
        if (self == null) return desired;

        bool hasSelf = false;
        Entry selfEntry = default(Entry);
        for (int i = 0; i < entries.Count; i++)
        {
            if (!ReferenceEquals(entries[i].owner, self)) continue;
            selfEntry = entries[i];
            hasSelf = true;
            break;
        }
        if (!hasSelf || selfEntry.tr == null) return desired;

        Vector3 offset = selfEntry.centerOffset;
        Vector2 half = selfEntry.half;
        DragBodyKind kind = selfEntry.kind;

        Vector3 result = desired;
        for (int pass = 0; pass < ResolvePasses; pass++)
        {
            bool pushed = false;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry other = entries[i];
                if (ReferenceEquals(other.owner, self) || other.tr == null) continue;
                if (!Collides(kind, other.kind)) continue;

                Vector3 center = result + offset;
                Vector3 otherCenter = other.tr.position + other.centerOffset;
                float dx = center.x - otherCenter.x;
                float dy = center.y - otherCenter.y;
                float overlapX = (half.x + other.half.x) - Mathf.Abs(dx);
                float overlapY = (half.y + other.half.y) - Mathf.Abs(dy);
                if (overlapX <= 0f || overlapY <= 0f) continue;

                // 最小穿透轴推回：斜着顶上去的结果就是「沿着对方侧边滑过去」，
                // 和撞桌沿完全同一套手感，玩家不用区分撞到的是桌沿还是另一台机器。
                if (overlapX < overlapY) result.x += overlapX * (dx >= 0f ? 1f : -1f);
                else result.y += overlapY * (dy >= 0f ? 1f : -1f);
                pushed = true;
            }
            if (!pushed) break;
        }

        return Overlaps(self, kind, half, offset, result) ? origin : result;
    }

    private static bool Overlaps(Component self, DragBodyKind kind, Vector2 half, Vector3 offset, Vector3 position)
    {
        Vector3 center = position + offset;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry other = entries[i];
            if (ReferenceEquals(other.owner, self) || other.tr == null) continue;
            if (!Collides(kind, other.kind)) continue;
            Vector3 otherCenter = other.tr.position + other.centerOffset;
            // 判定用「贴边＝已分开」，而且留 1e-4 余量吸收浮点误差。
            // 为什么不能用严格 `>`：上面的推回是**刚好**推出 overlap 那么多，落点必然
            // 精确贴边（|d| 与 half 之和在浮点上可能完全相等、甚至小几个 ULP）。
            // 按「贴边＝还重叠」判，每一次成功推回都会被这里自己否掉，
            // Resolve 退化成「一碰就整段作废、原路退回」——机器顶到别的物体上会直接冻住，
            // 而不是沿边滑过去（正是 2026-09-29 要求的手感），SettleIdle 也永远推不开重叠。
            const float Touch = 1e-4f;
            if (Mathf.Abs(center.x - otherCenter.x) >= half.x + other.half.x - Touch) continue;
            if (Mathf.Abs(center.y - otherCenter.y) >= half.y + other.half.y - Touch) continue;
            return true;
        }
        return false;
    }

    // 运行时被销毁的物体（盘子、票）不会都走到 OnDisable（场景卸载、Destroy 时机），
    // 留在表里就会变成看不见的墙，所以每次查询前扫一遍。
    private static void PruneDead()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].tr == null) entries.RemoveAt(i);
    }
}
