using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CourseStudy
{
    public static class L
    {
        public static bool English;
        private static readonly KeyValuePair<string, string>[] Phrases = new Dictionary<string, string>
        {
            { "MT001 视频转换工具", "MT001 Video Converter" },
            { "单个视频转换", "Single video" }, { "搜索课程", "Search" }, { "更换清单", "Import list" }, { "视频目录", "Videos" }, { "播放器", "Player" },
            { "全选当前结果", "Select visible" }, { "清空勾选", "Clear selection" }, { "请选择一节课程", "Select an item" },
            { "选中课程后，点击按钮才开始下载。", "Select an item, then start to download." }, { "完成后用 PotPlayer 打开", "Open with PotPlayer when finished" },
            { "转换并播放", "Convert and play" }, { "播放已有视频", "Play existing video" }, { "仅转换", "Convert only" }, { "已完成", "Completed" },
            { "取消全部任务", "Cancel queue" }, { "取消转换", "Cancel" }, { "就绪 · 请选择课程", "Ready - select an item" },
            { "勾选课程或章节后批量转换；搜索保留勾选，整批结束后可自动播放。", "Check items or sections to queue them. Search keeps selections. Playback starts after the queue." },
            { "设置暂时无法保存：", "Cannot save settings: " }, { "无法读取课程清单", "Cannot read the list" },
            { "请点击“更换清单”选择课程文件。", "Use Import list to select a list file." }, { "就绪 · 仅在点击转换后联网", "Ready - network access starts only when converting" },
            { "（暂无视频）", " (no video)" }, { "请选择带视频地址的课程。", "Select an item with a video address." },
            { "尚未转换 · 点击后才开始下载", "Not converted - download starts when you click" }, { "已有 MP4 · 将校验后直接使用", "Local MP4 found - verify and reuse" },
            { " · 按清单顺序转换", " - in list order" }, { " · 未勾选时处理高亮课程", " - no checks: use the highlighted item" }, { "已勾选 ", "Checked: " },
            { "（筛选外 ", " (hidden by filter: " }, { " 节）", " items)" }, { " 节课 · 本地已完成 ", " videos - local: " }, { " 节课", " videos" }, { " 节", " items" },
            { "显示 ", "Showing " }, { "正在处理当前课程…", "Processing the current item..." }, { "第 ", "Item " },
            { "正在取消整批任务… 分片缓存会保留供续传。", "Cancelling the queue... cached segments will be kept." },
            { "已取消整批任务 · 未完成课程保持勾选，缓存可续传", "Queue cancelled - unfinished items remain checked; cached segments can resume" },
            { "已取消 · 完成 ", "Cancelled - completed: " }, { "完成 ", "Completed: " }, { " · 全部校验通过", " - all files verified" }, { " · 失败 ", " - failed: " },
            { "部分失败 · 失败课程保持勾选，可再次重试", "Some items failed - they remain checked for retry" }, { "，失败 ", ", failed: " },
            { " · 已交给 PotPlayer 打开", " - opened with PotPlayer" }, { "视频已完成，但播放器未打开。", "Video completed, but the player could not open." },
            { "视频已保留在：", "Video saved in:" }, { "PotPlayer 启动失败", "PotPlayer could not start" },
            { "其余失败原因见 .logs。", "See .logs for the other failures." }, { "失败课程及缓存已保留，可再次点击重试。", "Failed items and cached segments were kept. Start again to retry." },
            { "有课程未完成", "Some items did not finish" }, { "错误记录：", "Error log: " },
            { "选择 PotPlayer 程序", "Select PotPlayer" }, { "播放器程序 (*.exe)", "Player executable (*.exe)" },
            { "已选择播放器：", "Selected player: " }, { "选择课程清单", "Select a list" }, { "课程清单 (*.java;*.txt)", "List (*.java;*.txt)" },
            { "所有文件 (*.*)", "All files (*.*)" }, { "清单无法读取", "Cannot read list" }, { "无法打开视频目录", "Cannot open video folder" },
            { "课程队列还在转换。要取消全部任务并退出吗？\n已完成的视频和分片缓存会保留，下次可继续。", "The queue is running. Cancel all tasks and exit?\nCompleted videos and cached segments will be kept." },
            { "退出课程学习助手", "Exit MT001" }, { "课程学习助手已经在运行。", "MT001 is already running in this folder." },
            { "课程学习助手", "MT001" }, { "详情", "Details" }, { "资料资源", "Resources" },
            { "复制地址", "Copy URL" }, { "复制提取码", "Copy code" }, { "复制分享信息", "Copy share info" }, { "浏览器打开", "Open in browser" },
            { "此条目及其上级暂无资源。", "This item and its parents have no resources." }, { "资源操作失败", "Resource action failed" },
            { "未找到 HTTP/HTTPS 资源地址", "No HTTP/HTTPS resource URL found" }, { "资源地址格式无效", "Invalid resource URL" },
            { "虚拟数据，仅用于展示", "Fictional demo - display only" }, { "虚拟示例地址仅用于展示，请输入真实地址或导入自己的清单。", "Demo URLs are display-only. Enter a real URL or import your own list." },
            { "虚拟示例地址仅用于展示，请输入真实地址。", "Demo URLs are display-only. Enter a real URL." }, { "虚拟地址仅用于展示。", "Demo URL - display only." },
            { "暂无清单，可导入清单或转换单个视频。", "No list loaded. Import a list or convert a single video." },
            { "m3u8 地址", "m3u8 URL" }, { "密钥（可选）", "Key (optional)" }, { "保存 MP4", "Save MP4" }, { "选择文件", "Browse" },
            { "密钥可填 Base64 或 32 位十六进制；留空时自动读取。仅支持 MPEG-TS 点播 HLS。", "Use Base64 or 32 hexadecimal digits, or leave the key blank for automatic retrieval. MPEG-TS VOD HLS only." },
            { "输入地址和保存位置后点击开始；不会自动播放。", "Enter a URL and output path, then start. Playback is manual." },
            { "开始转换", "Start" }, { "打开视频", "Open video" }, { "打开目录", "Open folder" }, { "正在取消…", "Cancelling..." },
            { "当前视频还在转换。取消并关闭吗？缓存会保留。", "The video is still converting. Cancel and close? Cached segments will be kept." },
            { "请选择完整的 MP4 保存路径。", "Choose a full MP4 output path." }, { "输出文件必须使用 .mp4 扩展名。", "The output file must have an .mp4 extension." },
            { "输出文件已存在，是否允许覆盖？校验通过前会保留原文件。", "The output file exists. Allow replacement? The original stays intact until verification passes." },
            { "已完成 · 全片校验通过", "Completed - full-file verification passed" }, { "已取消 · 缓存保留，再次开始可续传", "Cancelled - cached segments kept; start again to resume" },
            { "转换失败，可重试", "Conversion failed - retry available" }, { "密钥须为 16 字节：32 位十六进制或对应的 Base64 文本。", "The key must be 16 bytes: 32 hexadecimal digits or equivalent Base64." },
            { "视频地址必须是有效的 HTTP 或 HTTPS 地址。", "The video URL must be a valid HTTP or HTTPS address." },
            { "未找到 ffmpeg.exe，请将它放在程序目录中。", "ffmpeg.exe was not found. Put it next to the application." },
            { "AES-128 密钥必须为 16 字节。", "An AES-128 key must be 16 bytes." }, { "输出文件已存在，请确认覆盖或选择其他文件名。", "The output exists. Confirm replacement or choose another filename." },
            { "检查本地文件", "Checking local files" }, { "正在查找可复用的视频…", "Looking for a reusable video..." }, { "已验证并复用本地 MP4", "Verified and reused the local MP4" },
            { "读取播放列表", "Reading playlist" }, { "正在读取所选视频的播放列表…", "Reading the selected video's playlist..." }, { "读取密钥", "Reading key" },
            { "正在读取播放列表指定的 AES-128 密钥…", "Reading the AES-128 key specified by the playlist..." },
            { "密钥地址未返回 16 字节的原始 AES-128 密钥，可尝试手动输入。", "The key URL did not return a raw 16-byte AES-128 key. Try entering it manually." },
            { "下载失败（已重试）：", "Download failed after retries: " }, { "下载", "Downloading" }, { "封装", "Remuxing" }, { "校验", "Verifying" },
            { "正在无损封装为 MP4…", "Remuxing to MP4 without re-encoding..." }, { "正在校验整段音视频…", "Decoding and verifying the entire video and audio..." },
            { " 个分片 · 缓存复用 ", " segments - cached: " }, { "正在保存校验记录…", "Saving verification metadata..." },
            { "视频已转换并通过全片校验", "Video converted and fully verified" }, { "视频已完成，部分缓存暂时无法清理。", "Video completed; some cached files could not be removed." },
            { "服务器没有返回有效的 HLS 播放列表。", "The server did not return a valid HLS playlist." }, { "目前仅处理有结束标记的点播视频，不支持直播。", "Only VOD playlists with an end marker are supported, not live streams." },
            { "暂不支持 fMP4 或 BYTERANGE 格式。", "fMP4 and BYTERANGE are not supported yet." }, { "暂不支持独立音轨或外置媒体轨道。", "Separate audio or external media tracks are not supported yet." },
            { "仅支持标准 AES-128；暂不支持 SAMPLE-AES 或 DRM。", "Only standard AES-128 is supported; SAMPLE-AES and DRM are not supported." },
            { "加密播放列表缺少密钥 URI。", "The encrypted playlist is missing a key URI." }, { "本视频轮换了多把密钥，不能使用单一密钥处理。", "This video rotates multiple keys, which is not supported." },
            { "播放列表中的 IV 无效。", "Invalid playlist IV." }, { "分片时长无效。", "Invalid segment duration." }, { "分片缺少对应的时长。", "A segment is missing its duration." },
            { "没有找到视频分片。", "No video segments found." }, { "播放列表嵌套过深。", "Playlist nesting is too deep." }, { "播放列表过大。", "The playlist is too large." },
            { "单个分片超过大小上限。", "A segment exceeds the size limit." }, { "加密分片不完整。", "An encrypted segment is incomplete." },
            { "密钥不匹配，或视频分片格式不受支持。", "Incorrect key or unsupported video segment format." }, { "解密失败，请检查密钥与视频地址是否配套。", "Decryption failed. Check that the key belongs to this video." },
            { "下载未能完成，请重试。", "Download did not finish. Please retry." }, { "视频帧或时长校验未通过。已保留缓存供重试。", "Frame or duration verification failed. Cache kept for retry." },
            { "转换期间出现同名文件，已保留缓存，请重新确认输出位置。", "A file appeared at the output path during conversion. Cache kept; choose the output again." },
            { "缓存路径无效。", "Invalid cache path." }, { "输出文件超出了视频目录。", "Output path is outside the video folder." }, { " 个分片格式无效。", " has an invalid segment format." },
            { "找不到 PotPlayer，请重新选择播放器。", "PotPlayer was not found. Select the player again." }, { "找不到视频文件。", "Video file not found." },
            { "[地址已隐藏]", "[URL redacted]" }, { "[本地路径]", "[local path]" }, { "[已隐藏]", "[redacted]" }, { "失败：", " failed: " },
            { "条目", "items" }, { "资源", "resources" }, { "（地址无效）", " (invalid URL)" }
            ,{ "属性前缺少条目名称。", "An item name is required before its properties." }, { "本节课重复填写了密钥", "Duplicate key in this item" }, { "本节课重复填写了地址", "Duplicate video URL in this item" },
            { "缺少视频地址", "Missing video URL" }, { "清单中没有找到条目。", "No items were found in the list." },
            { "缓存目录不在指定位置，已停止清理。", "Cache directory is outside the expected location. Cleanup stopped." },
            { "缓存中存在目录链接，已停止清理。", "A directory link was found in the cache. Cleanup stopped." }, { "缓存目录是链接，已停止清理。", "The cache directory is a link. Cleanup stopped." }
        }.OrderByDescending(x => x.Key.Length).ToArray();
        public static string T(string text)
        { if (!English || text == null) return text; foreach (var phrase in Phrases) text = text.Replace(phrase.Key, phrase.Value); return text; }
        public static void Apply(Control control)
        {
            if (!(control is TextBox) && !(control is ComboBox) && !(control is ListBox) && !(control is TreeView)) control.Text = T(control.Text);
            foreach (Control child in control.Controls) Apply(child);
        }
    }
}
