# Unity Lottery

一款使用 Unity 6 和 C# 制作的 2D 像素风刮刮乐彩票游戏。点击按钮领取彩票，用鼠标刮开涂层，揭晓随机奖金并累积余额。

> **项目状态：** 当前提供 Unity 工程，可在编辑器中试玩。仓库暂未提供可直接双击运行的游戏安装包或可执行文件。

## 玩法

1. 点击 **New Ticket** 生成一张彩票。
2. 按住鼠标左键，在彩票的涂层上拖动以刮开它。
3. 刮开达到指定比例后，系统显示本次奖金并更新 **MONEY** 余额。
4. 彩票稍后自动消失，按钮恢复可用，可以继续领取下一张。

目前奖金从 `0、5、10、20、50` 中随机产生；抽到 `0` 时余额不会增加。余额只保存在本次运行的内存中，重新运行游戏会归零。

## 在 Unity 中试玩

1. 克隆仓库：

   ```bash
   git clone https://github.com/Grampusworld/Unity_Lottery.git
   ```

2. 在 **Unity Hub → Add project from disk** 中选择克隆得到的 `Unity_Lottery` 文件夹。
3. 使用 **Unity 6.5（6000.5.5f1）** 打开项目。首次打开时，Unity 需要导入素材和软件包，可能要等待一段时间。
4. 打开 `Assets/Scenes/SampleScene.unity`，点击编辑器上方的 **Play**。
5. 在 **Game** 窗口点击 **New Ticket**，然后按住鼠标左键刮开彩票。

## 实现内容

- 使用运行时纹理副本擦除涂层像素，保留原始 PNG 素材。
- 根据已擦除的像素比例判定是否完成刮奖，并防止同一张彩票重复结算。
- 使用预制体生成彩票，随机分配奖金，并通过 TextMeshPro 更新余额显示。
- 使用像素风彩票、桌面素材和字体构建 2D 游戏界面。

## 项目目录

```text
Assets/
  Fonts/          像素字体资源
  Lotteries/      彩票底图、涂层和桌面素材
  Prefabs/        彩票预制体
  Scenes/         游戏场景
  Scripts/        刮奖、彩票与结算逻辑
Packages/         Unity 软件包清单
ProjectSettings/  Unity 项目设置
```

项目使用的字体包含 **Press Start 2P**。复用或重新发布素材时，请分别核对相关素材的许可条款。
