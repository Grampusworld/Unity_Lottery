using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lottery.EditorTools
{
    /// <summary>
    /// 幂等装配「洗盘机 / 自动刮彩票机」的可拖动 + 惯性 + 实体碰撞。
    ///
    /// 挂三样东西：
    ///   ① 两台机器各一个 <see cref="DragInertia"/> + <see cref="MachineDrag"/>
    ///   ② 海绵补一个 <see cref="DragInertia"/>（它本来就能拖，这轮只是接上惯性 + 碰撞）
    ///   ③ 顺手把两处过期的初始布局挪到设计位 —— **只在还停在旧坐标时才动**的一次性迁移
    ///
    /// MachineDrag 的序列化引用全部回填：scratcher / washer / inertia / jelly / inputCamera，
    /// 以及桌面几何。桌面几何（tableSurface + tabletopPixels）**从海绵实例读**，
    /// 不手抄第二份 —— 抄了以后改桌面美术，两台机器和海绵就会各拖各的。
    ///
    /// 幂等：重复点只会把参数改回目标值，组件已存在就复用，不会重复添加。
    /// 菜单：Tools/挂个爽/一键装配机器拖动
    /// </summary>
    public static class MachineDragSetup
    {
        const string MenuPath = "Tools/挂个爽/一键装配机器拖动";
        const string WasherName = "Automatic Dish Washer";
        const string ScratcherName = "Auto Lottery Machine";
        const string ToolName = "Dish Sponge - Yellow";
        const string Tag = "[MachineDrag] ";

        // 桌面几何的兜底值（与 SpongeDrag 的 C# 默认值一致）。只在场景里连海绵都找不到时用。
        static readonly Rect FallbackTabletop = new Rect(58f, 128f, 99f, 57f);

        // 设计位（机身后回位用，见 MachineDrag.ResetToDesignPosition）。
        // 洗盘机没挪过，用不到迁移；刮票机与海绵这一轮各左移了一段。
        static readonly Vector3 ScratcherDesign = new Vector3(51.1f, -36.5f, 0f);
        static readonly Vector3 SpongeDesign = new Vector3(34f, -32.4f, 0f);
        const float StaleScratcherX = 53.1f;
        const float StaleSpongeX = 36f;
        const float MigrateTolerance = 0.05f;

        [MenuItem(MenuPath, false, 21)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError(Tag + "Play 模式下不能改场景（编译会退出 Play 并丢掉改动），先停 Play。");
                return;
            }

            GameObject washer = GameObject.Find(WasherName);
            GameObject scratcher = GameObject.Find(ScratcherName);
            GameObject tool = GameObject.Find(ToolName);
            if (washer == null || scratcher == null)
            {
                Debug.LogError(Tag + "场景里找不到 " + WasherName + " 或 " + ScratcherName + "。");
                return;
            }

            SpongeDrag sponge = tool != null
                ? tool.GetComponent<SpongeDrag>()
                : UnityEngine.Object.FindAnyObjectByType<SpongeDrag>();
            if (sponge == null)
            {
                Debug.LogError(Tag + "场景里找不到 SpongeDrag，拿不到桌面几何参数。");
                return;
            }

            Undo.SetCurrentGroupName("装配机器拖动");
            int group = Undo.GetCurrentGroup();

            string moved = string.Empty;
            if (tool != null && Migrate(tool.transform, StaleSpongeX, SpongeDesign)) moved += "海绵 ";
            if (Migrate(scratcher.transform, StaleScratcherX, ScratcherDesign)) moved += "刮票机 ";

            int added = 0;
            added += Wire(washer, null, washer.GetComponent<AutomaticDishWasher>(), sponge);
            added += Wire(scratcher, scratcher.GetComponent<AutoScratcher>(), null, sponge);
            added += WireTool(tool, sponge);

            Undo.CollapseUndoOperations(group);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

            Debug.Log(Tag + "完成 | 新增组件 " + added + " 个 | 迁移 " + (moved.Length > 0 ? moved : "无")
                + " | 桌面几何取自海绵 tabletop=" + sponge.TabletopPixels
                + " surface=" + (sponge.TableSurface != null ? sponge.TableSurface.name : "缺失")
                + " | 设计位 刮票机" + ScratcherDesign + " 海绵" + SpongeDesign);
        }

        /// <summary>只在还停在旧坐标时才挪 —— 装配菜单不该每次都把机器拽回原位。</summary>
        static bool Migrate(Transform target, float staleX, Vector3 design)
        {
            if (target == null || Mathf.Abs(target.position.x - staleX) > MigrateTolerance) return false;
            Vector3 position = target.position;
            target.position = new Vector3(design.x, design.y, position.z);
            return true;
        }

        static int Wire(GameObject machine, AutoScratcher scratcher, AutomaticDishWasher washer, SpongeDrag sponge)
        {
            int added = 0;
            DragInertia inertia = machine.GetComponent<DragInertia>();
            if (inertia == null) { inertia = Undo.AddComponent<DragInertia>(machine); added++; }

            MachineDrag drag = machine.GetComponent<MachineDrag>();
            if (drag == null) { drag = Undo.AddComponent<MachineDrag>(machine); added++; }

            SerializedObject so = new SerializedObject(drag);
            SetRef(so, "scratcher", scratcher);
            SetRef(so, "washer", washer);
            SetRef(so, "inertia", inertia);
            SetRef(so, "jelly", machine.GetComponent<HoverJelly>());
            SetRef(so, "inputCamera", Camera.main);
            SetRef(so, "tableSurface", sponge != null ? sponge.TableSurface : null);
            SetRect(so, "tabletopPixels", sponge != null ? sponge.TabletopPixels : FallbackTabletop);
            so.ApplyModifiedPropertiesWithoutUndo();
            return added;
        }

        static int WireTool(GameObject tool, SpongeDrag sponge)
        {
            if (tool == null || sponge == null) return 0;
            int added = 0;
            DragInertia inertia = tool.GetComponent<DragInertia>();
            if (inertia == null) { inertia = Undo.AddComponent<DragInertia>(tool); added++; }

            SerializedObject so = new SerializedObject(sponge);
            SetRef(so, "inertia", inertia);
            so.ApplyModifiedPropertiesWithoutUndo();
            return added;
        }

        static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning(Tag + "字段 " + field + " 不存在（序列化字段改名了？），跳过。");
                return;
            }
            property.objectReferenceValue = value;
        }

        static void SetRect(SerializedObject so, string field, Rect value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning(Tag + "字段 " + field + " 不存在（序列化字段改名了？），跳过。");
                return;
            }
            property.rectValue = value;
        }
    }
}
