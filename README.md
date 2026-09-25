# Unity Lottery（挂个爽）

一款使用 Unity 6 和 C# 制作的 2D 像素风刮刮乐 + 洗碗放置小游戏。点击按钮领取彩票，用鼠标刮开涂层领取随机奖金；也可以手动刷盘子、解锁自动洗碗机，让余额持续增长。

> **项目状态：** 当前提供 Unity 工程，可在编辑器中试玩。仓库暂未提供可直接双击运行的游戏安装包或可执行文件。

## 玩法

### 刮彩票

1. 在左侧商店的 **TICKETS** 页点击票种按钮，花钱生成一张彩票。
2. 按住鼠标左键，在彩票涂层上拖动刮开。
3. 刮开面积达到 80% 后自动揭晓奖金，更新右上角 **MONEY** 余额。
4. 彩票停留约 2 秒后消失，按钮恢复可用。

| 票种 | 售价 | 奖池（等概率） |
| --- | --- | --- |
| LUCKY | $10 | 0 / 5 / 10 / 20 / 50 |
| GOLD | $50（首次 $100 解锁） | 100 / 200 / 500 |
| NOVA | $50（首次 $1000 解锁） | 500 / 1000 / 2000 |

每种票累计刮满 10 / 25 / 50 张时，还会额外发放里程碑奖金。

### 洗碗

- 点击 **One More Plate** 生成一个脏盘子，拖动海绵刷干净即可获得收入。
- **PURPLE SPONGE**（$30）：刷头半径翻倍。
- **UNLOCK WASHER**（$200）：解锁自动洗碗机，每 10 秒自动产出 $5；可继续升级速度（最高 5 级）与容量（最高 5 级）。

余额、解锁状态与升级等级保存在 `PlayerPrefs` 中，重开游戏会保留。

## 操作

| 按键 / 操作 | 作用 |
| --- | --- |
| 鼠标左键拖拽 | 刮彩票、拖动海绵 |
| `F1` | 显示 / 隐藏 Debug 面板 |
| Debug 面板 `Reduce Motion` | 切换降低动效档（悬停果冻降到 80ms、无回弹、幅度减半） |

## 在 Unity 中试玩

1. 克隆仓库：

   ```bash
   git clone https://github.com/Grampusworld/Unity_Lottery.git
   ```

2. 在 **Unity Hub → Add project from disk** 中选择克隆得到的 `Unity_Lottery` 文件夹。
3. 使用 **Unity 6.5（6000.5.5f1）** 打开项目。首次打开时，Unity 需要导入素材和软件包，可能要等待一段时间。
4. 打开 `Assets/Scenes/SampleScene.unity`，点击编辑器上方的 **Play**。
5. 在 **Game** 窗口点击票种按钮，然后按住鼠标左键刮开彩票。

## 实现要点

- **刮涂层**：运行时复制涂层贴图并擦除像素，不修改原始 PNG；按已擦除像素比例判定完成，同一张彩票不会重复结算。
- **奖池数据化**：奖池配置在各票种预制体的 `LotteryTicket.prizes` 上，新增票种只需加预制体 + 按钮 + 回填引用，不需要改脚本逻辑。
- **像素字体**：使用 Press Start 2P，字体图集按 8px 采样点、`Padding = 5` 烘焙；`PixelPerfectCanvasScaler` 把画布缩放吸附为整数，字号取 8 的倍数，保证字形不发糊、笔画粗细一致。
- **悬停果冻反馈**：`HoverJelly` 组件用弹簧-阻尼积分驱动缩放（默认 22px 持续位移 / 30px 峰值，220ms 进入、160ms 移出），子级文字做反向缩放保持清晰，禁用状态与 reduced-motion 自动降级。
- **2D 界面**：全部 UI 使用拉伸锚点 + 对称内缩，配合像素风彩票、桌面素材与字体。

## 项目目录

```text
Assets/
  Editor/         编辑器批量挂载工具（Tools/挂个爽/）
  Fonts/          像素字体资源
  Lotteries/      彩票底图、涂层和桌面素材
  Materials/      共用材质
  Prefabs/        彩票、脏盘子等预制体
  Scenes/         游戏场景
  Scripts/        刮奖、彩票、洗碗、结算与动效逻辑
  Settings/       渲染与输入设置
Packages/         Unity 软件包清单
ProjectSettings/  Unity 项目设置
```

主要脚本：

| 脚本 | 职责 |
| --- | --- |
| `LotteryGame.cs` | 余额、解锁、里程碑、UI 刷新与存档 |
| `LotteryTicket.cs` | 单张彩票的奖池与开奖 |
| `ScratchCard.cs` | 涂层擦除与完成判定 |
| `SpongeDrag.cs` / `DirtyPlate.cs` / `AutomaticDishWasher.cs` | 洗碗玩法 |
| `HoverJelly.cs` / `HoverJellySettings.cs` / `HoverJellyDebugToggle.cs` | 悬停果冻反馈 |
| `PixelPerfectCanvasScaler.cs` | 整数倍画布缩放 |

项目使用的字体包含 **Press Start 2P**。复用或重新发布素材时，请分别核对相关素材的许可条款。
