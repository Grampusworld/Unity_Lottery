# v4 数值重平衡与主菜单转场（2026-10-04）

40 分钟节奏的完整数值表，以及主菜单进游戏的淡入淡出转场。

## 主菜单转场

### 结构

`ScreenFader`（`Assets/Scripts/ScreenFader.cs`）挂在 **独立的常驻根对象** `ScreenFaderCanvas` 上，
`sortingOrder = 200`（高于主菜单的 100）。

**为什么必须是独立根对象而不是 MainMenuCanvas 的子物体**：
`ConfirmNewGame()` 会走 `SceneManager.LoadScene` 重载整个场景，主菜单画布连同子物体一起被销毁。
而转场恰恰需要遮罩活着盖住「黑屏停留」那一刻 —— 所以它必须 `DontDestroyOnLoad`。

装配菜单：`Tools/Lottery/Add Screen Transition Fader`（幂等，已存在则拒绝并原样返回）。

### 时序

| 阶段 | 标准 | REDUCED MOTION |
|---|---|---|
| 淡出到黑 | 0.35s | 0.12s |
| 黑屏停留 | 0.15s | 0.05s |
| 淡入到游戏 | 0.5s | 0.12s |
| 启动淡入（仅一次） | 0.30s | 0.12s |

三个实现要点：

1. **全部走 `Time.unscaledTime`**。菜单期间 `MainMenuScreen` 会把 `Time.timeScale` 设成 0，
   `WaitForSeconds` 在那里会永久冻结。`unscaledTime` 是唯一不受暂停影响的时钟。
2. **`[DefaultExecutionOrder(-200)]`**，比 `MainMenuScreen`（-100）更早。
   否则 `Awake` 还没铺满全黑，菜单第一帧会在半透明遮罩后面闪一下。
3. **场景切换放在 hold 阶段**：`ScreenFader.Transition(action)` 在全黑那一帧调 `action`，
   `New Game` 的 `LoadScene` 就在里面。遮罩是常驻对象，重载不影响它。

### 打断语义

**不可打断，只吞输入**。转场期间 `ScreenFader.Busy == true`，
`MainMenuScreen.Show()` 据此强制保持 `GameplayBlocked`：

```csharp
bool blocked = view != ScreenView.Playing || ScreenFader.Busy;
```

`EnterGameplay()` 在 hold 阶段就被调用（那时 `Busy` 还是 true），所以输入全程锁死，
「不闪烁 / 不残留遮罩」是结构上保证的，不需要手写中间态。
转场结束由 `ScreenFader.Completed` 事件回调 `Show(CurrentView)` 真正放开。

`New Game` 重载后，新实例的 `Awake` 走 `Show(startAfterReload ? Playing : MainMenu)`——
否则 MainMenuPanel 会在淡入的 0.5 秒里闪一下再消失。

## 数值 v4：40 分钟节奏

### 为什么必须换掉「均匀 T」规则

v3 的铁律是「价格 = 该级购买瞬间的收入速率 × 85 秒」，后果是**回本时间恒定 → 购买间隔均匀**。
但目标时间点**不均匀**（4/10/15/20/30/40 分钟，间隔 6/5/5/10/10）。

**均匀 T 下第一个购买点必然落在 t = T**。要让第一个升级出现在 4 分钟，T 就必须是 4 分钟，
37 点 × 4 分钟 = 148 分钟。**「第一个升级在 4 分钟」与「均匀间隔」数学互斥**，不是调参能解决的。

所以 T 改成 per-purchase：每个购买点有自己的回本时间（见下表「回本」列）。
**要调节奏改 `Tools/solve_economy.py` 里 `SCHED` 的目标时刻重跑，不要手改单个价格。**

### 里程碑时刻

| 目标 | 实际 | 项目 |
|---|---|---|
| 3–5 min | **4:00** | 紫海绵 |
| 10 min | **10:00** | 解锁 GOLD |
| 15 min | **15:00** | 解锁洗盘机 |
| 20 min | **20:00** | 解锁刮票机 |
| 30 min | **30:00** | 解锁 ZIGZAG（全部票种齐） |
| 40 min | **40.4 min** | 通关 |

### 里程碑：现金 → 全局倍率

原设计发一次性现金，18 个节点合计 $6,920 —— 在 200 万目标下只占 0.35%，玩家完全感知不到。
改为提升全局倍率 M。

Δ 按几何级数 `d_i = 0.08 × 5^(i/5)`（`MilestoneMultipliers`）：

| 票种 | Δ/节点 | 三节点累计 |
|---|---:|---:|
| LUCKY | +8.0% | +24% |
| GOLD | +11.0% | +33% |
| NOVA | +15.2% | +46% |
| HEARTMATCH | +21.0% | +63% |
| CROSSCODE | +29.0% | +87% |
| ZIGZAG | +40.0% | +120% |

**跨度只有 5x，是这条曲线的全部意义。** 早期方案按票价线性给 Δ 时跨度达 50x——
理性玩家解锁 ZIGZAG 后永不回头买 LUCKY，低档票直接变成死内容。

**加性累积**（`M += Δ`，M_max = 4.728x）：M 越大，同样 Δ 的相对幅度越小，
ZIGZAG 第 50 张名义 +40%、实际只从 4.13x 到 4.53x（+9.7%）。已接受——
乘性累积会让 M 复利到 147x，指数源失控。

**语义变化**：M 是全局的，同时放大洗盘机和手动擦盘，不再只是彩票线的奖励。
中期最优策略可能变成「只刮最贵的票刷倍率」，低档票退化为过渡。

### 两个指数源

| 来源 | 终局 |
|---|---|
| `BoostPerLevel`（升级链 36 级 × 1.10） | 30.9x |
| `MilestoneMultiplier`（里程碑） | 4.728x |
| 相乘 `TotalMultiplier` | 实测 114.7x |

`LotteryEconomy` 顶部的铁律②已改写：**两者是并列指数源，不要引入第三个。**
链内部跨度（洗盘机容量/周期、盘价值）已冻结，改它们等于重画机器手感。

### 通关目标

```
W = Rf × tail
Rf = $11,577/s（终局速率，累计倍率 114.7x）
最后一个购买点 37.5 min + 积累 2.9 min → W = 11,577 × 173 ≈ $2,000,000
```

**W 是纯乘数，不影响任何购买节奏** —— 实测总时长偏差时优先调 W，别动价格。

20/30/50 万已被排除：它们只够 17–43 秒积累，玩家会在 30 分钟就撞线通关，一半内容还没买。

内容总成本 $3,624,433 vs W = $2,000,000（C/W = 1.81）。内容 ≳ 目标才不会「买光后干等」。

## 存档

`SavePrefix` **v3 → v4**。旧档作废（W 从 100 万抬到 200 万，v3 存档会直接判通关）。

新增 6 个键 `Milestone0..5`（每票已达成的里程碑档数）。
**必须存档**：v4 里倍率是持续生效的，不存等于重开游戏白送一次倍率重置。

## 验证记录

Play 实测（`Application.runInBackground = true`）：

- `WinTarget = 2000000` 生效；`MilestoneMultiplier` 0 档 = 1.0000
- LUCKY 刮 10/25/50 张 → M = 1.0800 / 1.1600 / 1.2400（与 `1 + 0.08×档数` 完全一致）
- 六票全满 → M = **4.7281**（与设计值 4.7281 误差 0.000000）
- `Gain(1000)` 在 M=1.24 下入账 **1240**
- 存档往返：`3,2,1,3,0,2` 写入后读回一致，M = 3.0434
- 通关判定：`W-1` 不触发、`W` 触发、`W+500k` 保持（`hasWon` 单向锁存）
- 启动淡入：`Busy=False`、`alpha=0.000`、菜单显示、`timeScale=0`
- **CONTINUE 全程**：`Busy=True` 且 `blocked=True` → 结束 `alpha=0.000` / `view=Playing` / `timeScale=1` / HUD 可交互
- **NEW GAME 全程（整场重载）**：fader 存活、alpha 归零、直接落`Playing`（无菜单闪一下）、余额清 0、`Gold=0`
- Play 期间场景文件 **0 差异**；测试产生的 30 个 v4 键已逐键删除，原有 48 个存档键与设置**完全一致**

菜单幂等：`Add Screen Transition Fader` 连跑两次 → 第二次报"already present"，场景 **0 差异**。
`Apply Economy And Scrollbars` 连跑两次 → 票资源 / Prefabs **0 差异**；
场景仅 1 行`Icon` 的 `m_AnchoredPosition`（100.99 → 30），是 `TicketButtonIconLayout` 每帧重写的已知占位值。

## 遗留 / 待实测

1. **回本 T 前 5 分钟是 240s，之后骤降到 9–20s**（洗盘机/刮票机解锁点）。
   per-purchase T 的必然结果——解锁机器瞬间速率跳涨。手感上"前 5 分钟慢、中段突然变快"。
   唯一旋钮是把解锁时刻往后挪，但会挤压 20 分钟的刮票机节点。
2. **Rf = $11,577/s 是模型估算**（scratch duty cycle 0.45、洗盘补盘 0.8s、里程碑命中率都是估的），
   2.9 分钟积累段只有约 ±40% 容错。
3. **里程碑倍率飘字** `CoinGainFeedback.ShowMultiplier` 走独立协程，不进 `pendingGains`
   （里程碑不花钱，不能锁余额栏计数）。未在 Play 里目视确认外观。
4. `Tools/simulate_economy.py` 是 v3 快照，已标注过时；`solve_economy.py` 才是当前校准工具。