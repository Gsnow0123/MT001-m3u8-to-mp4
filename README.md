# MT001 视频转换工具 / Video Converter

Windows 桌面 m3u8 转 MP4 工具：单个视频、清单批量转换、附带资料链接及 PotPlayer 播放。支持中文和 English，右上角切换并记住语言选择。

A Windows desktop m3u8-to-MP4 converter with single-video conversion, batch lists, resource links, and optional PotPlayer playback. Switch between Chinese and English in the top-right corner.

## 下载与运行 / Download and run

从 [Releases](https://github.com/Gsnow0123/MT001-m3u8-to-mp4/releases) 下载完整便携 ZIP，解压后打开 `MT001-m3u8-to-mp4.exe`。Windows 10/11 x64，需 .NET Framework 4.8；不需要 Python。保留同目录的 `ffmpeg.exe`。单独下载 EXE 时，请自行将 FFmpeg 放在旁边。

Download the portable ZIP from Releases, extract it, and open `MT001-m3u8-to-mp4.exe`. Requires Windows 10/11 x64 and .NET Framework 4.8; no Python is needed. Keep `ffmpeg.exe` next to the app. The standalone EXE requires a separately supplied FFmpeg executable.

## 使用 / Usage

- **单个视频 / Single video**：输入 m3u8 地址、可选的 Base64 或 32 位十六进制 AES-128 密钥，并选择 MP4 保存位置。密钥留空时，从播放列表指定地址读取。完成后手动打开视频或目录。
- **清单 / Batch list**：导入 UTF-8 文本清单，勾选课程或章节，按清单顺序转换。搜索保留勾选，单节失败会继续后续课程。可选整批结束后用 PotPlayer 打开。
- **资料资源 / Resources**：选择条目后切到资源页，查看当前条目及其上级的链接与提取码。支持复制网址、提取码、整条分享信息和浏览器打开。不包含网盘登录或文件下载功能。
- **取消与续传 / Cancel and resume**：取消保留已校验分片，下次使用同一地址、密钥和输出目录可续传。正式 MP4 在无损封装和全片音视频校验通过后才保存；已有且验证有效的视频可复用。

Single video accepts an m3u8 URL, an optional Base64/hex AES-128 key, and an output path. Batch mode processes checked items in list order, continuing after individual failures. Resources can be viewed, copied, or opened in a browser. Cancellation preserves verified segments for resuming. Completed MP4 files are remuxed without re-encoding and decoded end-to-end for verification.

![中文演示](docs/demo-zh.png)
![English demo](docs/demo-en.png)
![Single video](docs/single-en.png)

## 清单格式 / List format

清单只是文本，不执行代码；`.txt` 或文本形式的 `.java` 都可导入。Tab 或空格表示层级。`key` 可选。`源码地址`、`其他资料`、`课前预习` 或其他带 HTTP/HTTPS 地址的字段均可作为资料资源；支持地址后空白分隔的四位提取码、显式提取码文本及 `pwd` 查询参数。

Lists are parsed as text only. Indentation defines sections. Resource links attach to the preceding item at the corresponding indentation level. A list can also contain resources without videos.

```text
示例章节 / Example section
    示例视频 / Example video
        key-> AAAAAAAAAAAAAAAAAAAAAA==
        hls-> https://example.invalid/video/sample.m3u8
        源码地址-> https://example.invalid/share/sample    DEMO
        课前预习-> https://example.invalid/reading/sample
    仅含资料 / Resources only
        其他资料-> https://example.invalid/resources/sample?pwd=DEMO
```

内置演示清单全部为虚构数据，仅展示界面。`.invalid` 为保留示例域名，工具禁止请求这些地址。导入自己的清单或输入真实视频地址后即可使用转换功能。

The bundled demo is entirely fictional and display-only. Requests to reserved `.invalid` URLs are blocked. Import your own list or enter a real video URL to convert.

## 支持范围 / Supported formats

- MPEG-TS 点播 HLS；未加密或单密钥标准 AES-128；显式 IV 与媒体序号 IV。
- 主播放列表选择最高带宽版本，处理相对路径和重定向后的基础地址。
- 4 个分片并发、网络失败重试、分片哈希校验、断点续传与整段解码校验。
- 暂不支持直播、DRM、SAMPLE-AES、fMP4、BYTERANGE、独立音轨或多密钥轮换。

Supports MPEG-TS VOD HLS, unencrypted streams and single-key AES-128, explicit and sequence-derived IVs, relative URLs, and the highest-bandwidth variant of a master playlist. Live streams, DRM, SAMPLE-AES, fMP4, BYTERANGE, separate audio tracks, and key rotation are not supported. The bundled minimal FFmpeg covers H.264, HEVC, MPEG-2/MPEG-4 video and common AAC/MP2/MP3/AC3 audio; other codecs may require a compatible FFmpeg build.

## 隐私与本地文件 / Privacy and local files

无遥测或上传功能。程序仅访问你明确转换的视频、播放列表指定的密钥与分片，或你点击打开的资料页面。新转换记录不包含原始网址或密钥，错误日志隐藏地址和敏感参数。资源及课程标题按导入内容原文显示，不自动翻译。

默认 MP4 位于 `mp4`；设置为 `settings.json`；续传缓存为输出根目录下的 `.cache`；批量播放列表为 `mp4/.playlists`。地址和密钥不会保存到设置。源码仓库及发行包只包含虚构示例，不附带任何个人清单、视频或运行记录。

No telemetry or upload feature. Input URLs and keys are not saved to settings. New verification records do not contain source URLs or keys. Imported titles and resource names are preserved as-is. Runtime settings, cached segments, videos and logs are local files excluded from the repository and release packages.

## 编译与测试 / Build and test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1 -Test -StageOnly
```

使用 Windows 自带的 .NET Framework C# 编译器，无 NuGet 依赖。需要旁边有 FFmpeg 才能运行转换测试。测试生成合成短视频和通用清单，仅使用模拟地址与本机回环 HTTP，不读取个人课程清单。构建产物位于 `.build`；去掉 `-StageOnly` 可更新本地程序。

Uses the .NET Framework C# compiler with no NuGet dependencies. Tests generate synthetic videos and lists, using mocked URLs and loopback HTTP only. Place FFmpeg next to the project before testing. Build artifacts are written to `.build`.

FFmpeg 的固定源码版本与交叉编译流程位于 `scripts/build-ffmpeg.sh`，GitHub Actions 构建 Windows x64 版本。便携包包含其完整对应源码、许可证与构建记录。

## 许可证 / License

本工具源码为 [MIT](LICENSE)。FFmpeg 为独立进程，采用其自身的 LGPL 许可证；便携包包含完整对应源码、许可证与构建信息，见包内 `third-party`。FFmpeg 项目：[源码](https://github.com/FFmpeg/FFmpeg)、[许可说明](https://ffmpeg.org/legal.html)。

This application's source is MIT-licensed. FFmpeg is a separate LGPL-licensed executable; its complete corresponding source, license, and build information are supplied in `third-party` in the portable package.