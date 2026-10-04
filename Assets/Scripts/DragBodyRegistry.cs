using System.Collections.Generic;
using UnityEngine;

// 可拖/可挡物体的实体占位登记处。只做三件事：
//   ① 谁跟谁能撞（Collides）
//   ② 把「想去的位置」按最小穿透轴推回合法区（Resolve）
//   ③ 这次按下归谁（Claimant）
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
// 让路规则（抓取仲裁，见 Claimant）：
//   一次按下只能有一个赢家。优先级从高到低：**海绵 > 票 > 机器 > 盘子**。
//   两条判据：
//     ① **按下当帧抓的必须让给长按抓的**。海绵与机器是按下即拖，票与盘子要按住 0.12s。
//        反过来的话，长按型在 0.12s 之后也会抓，两个物体一起跟着手跑
//        （症状：点在票/盘子上按住不动，票/盘子自己溜走，或者两个一起被拖）。
//     ② 同一档里按绘制层次排。海绵 order 5 > 机器 4 > 盘子 2/3。
//   票压过机器是**刻意例外**：票画在机器下面（order 0~2 < 4），但票要按住才能拿起来，
//   机器在按下那一帧就抢走了 —— 票一旦被压在机器上就永远抓不回来（软锁）。
//   这是「可用性优先于层次一致」的一处妥协。
//
//   为什么规则集中写在一处、而不是各组件互相 Hit 一遍：
//   原来是「长按型各自记得让自己让给谁」，四个文件里散着三条判断，必然漏
//   （2026-09-29 实测：PlateDragger 漏了海绵、TicketDragger 一条都没有）。
//   现在每个组件只问一句「这次按下是不是我的」，新增可拖物体不会再漏。
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

    // 「我还在表里吗」。给每帧自检的组件用（SpongeDrag / MachineDrag）。
    // 这张表是 static：Play 中任何一次脚本重编译（域重载）都会把它清空，而丢表**不会**
    // 抛任何异常 —— 只是按下时 Claimant 不再返回这一类，物体直接变成「拖不动」。
    // 2026-09-29 实测：海绵在表里消失后（11 条条目，0 条 Sponge），真实按下帧跑
    // SpongeDrag.Update() 结果是 dragging=false，且没有任何报错 —— 正是这个原因
    // 让「海绵压在盘子上拖不动」看起来像仲裁写反了。顺手扫掉死条目，与 Claimant 同语义。
    public static bool IsRegistered(Component owner)
    {
        if (owner == null) return false;
        PruneDead();
        for (int i = 0; i < entries.Count; i++)
            if (ReferenceEquals(entries[i].owner, owner)) return true;
        return false;
    }

    public static bool Collides(DragBodyKind a, DragBodyKind b)
    {
        if (a == b) return a != DragBodyKind.Plate; // 盘子允许堆叠，其余同类互斥。
        bool machine = a == DragBodyKind.Machine || b == DragBodyKind.Machine;
        bool sponge = a == DragBodyKind.Sponge || b == DragBodyKind.Sponge;
        bool plate = a == DragBodyKind.Plate || b == DragBodyKind.Plate;
        if (machine && sponge) return true;
        if (machine && plate) return true;
        return false;
    }

    // 这次按下的世界点**归谁**。没有命中任何可拖物体时返回 null。
    // 每个拖拽组件都只问这一句：`Claimant(world) != 我这一类` 就整段早退。
    // 优先级顺序只在本数组里定义一次（判据见文件顶部）。
    private static readonly DragBodyKind[] GrabPriority =
    {
        DragBodyKind.Sponge,    // 层次最高（order 5）且按下即拖
        DragBodyKind.Ticket,    // 长按型：必须压过按下即拖的机器，否则被机器压住就再也拿不起来
        DragBodyKind.Machine,   // 按下即拖
        DragBodyKind.Plate      // 长按型，层次也最低（order 2/3）
    };

    public static DragBodyKind? Claimant(Vector3 world)
    {
        PruneDead();
        for (int p = 0; p < GrabPriority.Length; p++)
            if (HitAny(world, GrabPriority[p])) return GrabPriority[p];
        return null;
    }

    // 盘子允许堆叠，但一次按下只拿最上面一只；避免重叠的盘子全部跟着鼠标走。
    public static bool OwnsPlatePress(DirtyPlate owner, Vector3 world)
    {
        PruneDead();
        DirtyPlate top = null;
        int order = int.MinValue;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry.kind != DragBodyKind.Plate || entry.tr == null) continue;
            DirtyPlate plate = entry.owner as DirtyPlate;
            if (plate == null || !plate.Ready) continue;
            PlateFlyIn fly = plate.GetComponent<PlateFlyIn>();
            if (fly != null && fly.IsFlying) continue;
            Vector3 center = entry.tr.position + entry.centerOffset;
            if (Mathf.Abs(world.x - center.x) > entry.half.x || Mathf.Abs(world.y - center.y) > entry.half.y) continue;
            if (plate.StackOrder < order) continue;
            top = plate;
            order = plate.StackOrder;
        }
        return ReferenceEquals(top, owner);
    }

    // 世界点是否落在某一类物体的包围盒里。
    private static bool HitAny(Vector3 world, DragBodyKind kind)
    {
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
    public static Vector3 Resolve(Component self, Vector3 origin, Vector3 desired,
        System.Func<Vector3, Vector3> constrain = null)
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
                if (constrain != null)
                {
                    // 桌沿堵住最短推离方向时，尝试另三面；不能为了避碰退到桌外。
                    Vector3 best = result;
                    float distance = float.PositiveInfinity;
                    for (int side = 0; side < 4; side++)
                    {
                        Vector3 candidate = result;
                        if (side < 2) candidate.x = otherCenter.x + (side == 0 ? -1f : 1f) * (half.x + other.half.x) - offset.x;
                        else candidate.y = otherCenter.y + (side == 2 ? -1f : 1f) * (half.y + other.half.y) - offset.y;
                        candidate = constrain(candidate);
                        Vector3 separation = candidate + offset - otherCenter;
                        if (Mathf.Abs(separation.x) < half.x + other.half.x - 1e-4f
                            && Mathf.Abs(separation.y) < half.y + other.half.y - 1e-4f) continue;
                        float travel = (candidate - result).sqrMagnitude;
                        if (travel >= distance) continue;
                        best = candidate;
                        distance = travel;
                    }
                    result = best;
                }
                else if (overlapX < overlapY) result.x += overlapX * (dx >= 0f ? 1f : -1f);
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
