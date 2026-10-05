# LOTTERY MANIA — 29 秒实机宣传片

成片：`LotteryMania_29s_1080p60.mp4`，1920×1080，60 fps，H.264 + AAC，横屏 16:9，8.3 Mbps，29 MB。

## 在线播放版本

| 文件 | 体积 | 用途 |
|---|---|---|
| `LotteryMania_29s_1080p60.mp4` | 29 MB / 8.3 Mbps | 高码率母版，供本地播放与下载 |
| `LotteryMania_29s_1080p60.web.mp4` | 14.1 MiB / 4.1 Mbps | README 与网页内联播放 |

**为什么需要 web 版本：** GitHub raw（`raw.githubusercontent.com` 与 `github.com/.../raw/`）不识别 `.mp4`，返回 `Content-Type: application/octet-stream` 加 `X-Content-Type-Options: nosniff`。浏览器据此拒绝内联播放，点击只会触发下载。同一仓库的 `.mp3`、`.png` 都返回正确 MIME，只有 mp4 是例外。`media.githubusercontent.com/media/...` 对仓库内普通文件返回 404，仅用于 Release 附件。

因此网页播放走 jsDelivr CDN，它返回正确的 `video/mp4`、支持 Range 分段请求并开放 CORS：

```text
https://cdn.jsdelivr.net/gh/Grampusworld/Unity_Lottery@main/Docs/TrailerPlan/Production/LotteryMania_29s_1080p60.web.mp4
```

**jsDelivr 单文件上限 20 MiB**，这是 web 版本压到 14.1 MiB 的原因。重新生成：

```bash
FFMPEG=/Applications/VideoFusion-macOS.app/Contents/Resources/ffmpeg
"$FFMPEG" -hide_banner -y -i LotteryMania_29s_1080p60.mp4 \
  -c:v h264_videotoolbox -b:v 5000k -maxrate 6250k -bufsize 10000k \
  -allow_sw 1 -pix_fmt yuv420p -tag:v avc1 -movflags +faststart \
  -c:a aac -b:a 128k -ac 2 LotteryMania_29s_1080p60.web.mp4
```

`-movflags +faststart` 让 moov 原子排在 mdat 之前，浏览器才能边下边播（默认 moov 在尾部，不加这个参数会一直转圈到下载完成）。画质经逐像素核对，1.5s／12s／20s 三个时间点平均误差 0.27–0.55/255，明显差异像素均低于 0.2%。

备选托管方式：Cloudflare Pages / Vercel 等静态托管同样可用，只需注意 Pages 的构建产物需包含视频文件；若改用 GitHub Pages，注意 Pages 有 1 GB 站点上限，且本仓库页面未启用。

## 内容与节奏

| 时间 | 内容 |
|---|---|
| 0–4 秒 | 实际刮开 Lucky 彩票、中奖烟花和金币反馈；局部推近与慢放 |
| 4–7 秒 | 原教程中的猫咪、200 万猫粮目标 |
| 7–10.5 秒 | 黄海绵擦盘、紫海绵一擦清洁 |
| 10.5–14.5 秒 | Lucky、Gold、Nova、Heart Match、Cross Code、Zigzag Run 六种彩票快切 |
| 14.5–18.5 秒 | 实际升级 UI，沿长按拖拽路线跟随彩票入刮票机 |
| 18.5–22 秒 | 自动洗盘与自动刮票近景 |
| 22–25 秒 | 第 10 张 Lucky 的实际 +8% MULTIPLIER 飘字和目标 UI |
| 25–29 秒 | 原主菜单完整构图、NEW GAME 悬停，最后两秒固定镜头 |

字幕采用游戏的深蓝、金色和米白配色。没有增加新角色、场景、关卡、奖金额或玩法；字幕为后期宣传文案。

## 素材与声音

全部游戏画面来自当前 Unity 项目运行画面的 2560×1440 PNG 序列。配乐使用项目已有 `Assets/Resources/Music/happy_adveture.mp3`；操作音使用 `Assets/Materials/Audios` 中已接入的原始音效。音效触发帧来自实际音效管理器的事件记录，按剪辑速度和切点对齐。

## 拍摄说明

采用独立的未开始存档会话，在运行时准备资金、解锁、机器升级和里程碑计数，并从实际奖池选择正奖录制。刮擦、拖拽投料、洗盘、金币、机器效果和菜单悬停均由现有组件运行产生。演示片段不表示连续游玩的资金进度或中奖概率。

片中使用慢放、加速和局部裁切；机器运行参数没有修改。录制前后核对 33 个存档及设置键，数值全部一致；退出运行后场景未标记修改。临时录制组件已从 Assets 中移除。

## 可复现剪辑

`edit-manifest.json` 保存时间线、取材区间、运镜与字幕，`sound-events.json` 保存声音切点，`raw/` 保存原始实机帧与每段触发记录。

项目根目录的 `Tools/TrailerProduction/render_trailer.py` 可重新合成。`TrailerCaptureRunner.cs.txt` 是录制工具存档，不参与游戏编译。编码需要本机 FFmpeg 和 macOS VideoToolbox。
