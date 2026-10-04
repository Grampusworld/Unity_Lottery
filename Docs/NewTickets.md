# 三张多数字彩票

## 试玩

打开 `Assets/Scenes/SampleScene.unity` 并运行。鼠标放在 Tickets 列表内向下滚动，可找到 HEARTMATCH、CROSSCODE、ZIGZAG。首次点击支付解锁费用，再次点击购买一张彩票。桌面仍沿用一次一张彩票的规则；拖入自动刮票机后可以继续购买。

| 彩票 | 票种编号 | 解锁价格 | 每张价格 | 中奖规则 |
| --- | --- | --- | --- | --- |
| HeartMatch | 3 | $1,800 | $100 | 任意两数相同 $200；三数相同 $600 |
| CrossCode | 4 | $5,000 | $250 | 四角每个匹配中心目标数，获得 $250 |
| ZigzagRun | 5 | $14,000 | $500 | 沿 Z 路径五数严格递增，获得票面规则栏标明的奖金 |

解锁只检查余额，不要求购买前一种票。数字在出票时生成，图中没有烘焙数字。刮除不透明涂层达到原有 85% 阈值后整张揭晓，统一结算一次。手刮与自动刮票机共用原有金币特效和延迟余额显示。

三种票分别累计完成 10／25／50 张时，心愿票奖励 $140／$210／$280，交叉票奖励 $280／$420／$560，路径票奖励 $600／$900／$1,200，与当次奖金合并到账。输了仍计入完成张数。解锁与计数自动存档；旧存档保持兼容。F1 调试菜单中的全部解锁和重置进度包含新票。

## 配置位置

- 预制体：`Assets/Prefabs/HeartMatchTicket_Base.prefab`、`CrossCodeTicket_Base.prefab`、`ZigzagRunTicket_Base.prefab`。保留 LotteryTicket 的 kind、prizes、prizeText，并增加 numberTexts。
- `prizes` 数组等概率抽取；重复条目增加该结果的概率。心愿票允许 0／200／600；交叉票允许 0／250／500／750／1000；路径票允许 0 或正奖金。数值集中在 `LotteryEconomy.cs`；配对和每角奖金的规则文字从该配置读取。修改奖池后，在编辑模式运行 `Tools > Lottery > Apply Economy And Scrollbars` 写回六个预制体。
- `LotteryEconomy.cs` 的 TicketPrices、UnlockPrices 和 MilestoneBonuses 配置价格与里程碑。新存档键沿用 `LotteryPrototype.v1.` 前缀，增加 HeartMatch、CrossCode、ZigzagRun 和 Scratched3..5。
- base 与 cover 均为 128×80、PPU 100、中心锚点、Point 采样。cover 为 Single、可读、无压缩的完整透明图片，包含所有奖格；不应切成多个 Sprite 或加入 Sprite Atlas。
- 新票 numberTexts 顺序：HeartMatch 左到右；CrossCode 中心、左上、右上、左下、右下；ZigzagRun 左上、右上、中间、左下、右下。
- `Tools > Lottery > Set Up Three New Tickets` 可重新装配。此工具以预制体与按钮名称复用已有对象；重新执行会还原本次配置的奖池、文本布局与导入设置，手动调整后请勿随意重跑。

## 已验证

- Unity Editor 成功编译；干净重新进入 Play 后 Console 无错误或警告。
- 6,000 次组合验证涵盖三玩法的全部奖池结果，数字规则与奖金一致。
- 运行中通过模拟鼠标输入实际擦除三种 cover，达到阈值后结算；重复 Reveal 不重复发钱。
- 新票按钮解锁、购买扣款、余额不足拦截、三阶段里程碑与菜单滚动通过检查。
- 三种新票通过自动刮票机队列，完成后按正确票种发奖与计数。
- 退出后重新运行，新增解锁和计数正确读取。
- 运行画面检查确认 base／cover 对齐、奖格文字和规则栏可见、菜单图标与文本正常。
- 测试结束恢复原余额和存档，并退出 Play 模式。本次未生成独立游戏构建。
