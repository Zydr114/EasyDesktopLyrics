# EasyDesktopLyrics

Windows 桌面歌词工具。通过 [SMTC](https://learn.microsoft.com/windows/uwp/audio-video-camera/system-media-transport-controls) 跟随外部播放器同步显示歌词，歌词窗口常驻桌面并支持鼠标穿透（锁定后完全不挡操作），透明、轻量、即开即用。

![Platform](https://img.shields.io/badge/platform-Windows%2010%201909+-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.1-8A2BE2)

## 功能

| 功能 | 说明 |
|------|------|
| **SMTC 同步** | 自动跟随 Spotify、网易云音乐、QQ 音乐、Apple Music、foobar2000 等任何发布 SMTC 会话的播放器 |
| **自动匹配歌词** | 网易云音乐 / QQ 音乐 / LRCLIB / 本地 LRC 目录多源自动匹配，来源顺序可配置 |
| **逐字歌词（实验性）** | 网易云 yrc 逐字时间戳 → 卡拉 OK 式按字高亮；无逐字数据自动回退整行高亮 |
| **已唱 / 未唱样式隔离** | 颜色、不透明度、描边、辉光均可独立设置；未唱段默认弱化，保证逐字明暗对比 |
| **文字特效** | 阴影、描边（宽度 0.1–4）、辉光可自由组合，任意背景下保证可读性 |
| **窗口锁定** | 锁定后鼠标穿透且不可拖动，歌词完全悬浮于桌面，不干扰任何操作 |
| **歌词封面** | 常驻封面显示 + 切歌居中淡入动画，尺寸/时长可调 |
| **位置预设** | 九宫格一键定位 + 水平/垂直百分比微调 |
| **手动校正** | 自动匹配不理想时手动指定歌词源/歌曲，支持单曲时间偏移 |
| **全局时间偏移** | 歌词整体提前/延后微调 |
| **托盘控制** | 显示/隐藏歌词、锁定、退出均可在托盘完成 |
| **轻量** | 单 exe 绿色运行，无任何后台服务，静态渲染时 CPU 占用约 0% |

## 使用

1. 从 [Release](https://github.com/Zydr114/EasyDesktopLyrics/releases) 下载压缩包，解压后双击 `EasyDesktopLyrics.exe`。
2. 打开任意支持的播放器开始播放，歌词窗口自动出现。
3. 托盘图标 → 锁定歌词（鼠标穿透），歌词即完全悬浮于桌面。

> 首次运行会自动从各来源拉取歌词并缓存到 `%APPDATA%\EasyDesktopLyrics\`。

## 构建

环境要求：.NET 10 SDK、Windows 10 1909+。

```bash
git clone https://github.com/Zydr114/EasyDesktopLyrics.git
cd EasyDesktopLyrics
dotnet build -c Release
```

## 歌词来源

- **网易云音乐**：搜索 + LRC + 逐字 yrc（weapi，无需登录）
- **QQ 音乐**：搜索 + LRC（klyric 逐字不可用，仅行级）
- **LRCLIB**：开放社区歌词库（欧美/日韩覆盖好）
- **本地目录**：指定文件夹内的 `*.lrc` 文件，按曲名/歌手自动匹配

歌词缓存于 `%APPDATA%\EasyDesktopLyrics\cache\lyrics\`，清空即强制重新拉取。

## 隐私

- 应用不收集任何数据，不注册开机自启（除非自行配置）。
- 歌词接口均为各平台非官方公开接口，仅供个人学习使用；请在符合平台条款的前提下使用。

## License

[MIT](LICENSE)
