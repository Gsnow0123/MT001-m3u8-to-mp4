using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CourseStudy
{
    public static class Files
    {
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }; }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        public static string Identity(string url, byte[] key)
        { return Hash(Encoding.UTF8.GetBytes(url + "\0").Concat(key ?? new byte[0]).ToArray()); }
        public static byte[] ParseKey(string text)
        {
            text = (text ?? "").Trim(); if (text.Length == 0) return null;
            string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text.Substring(2) : text;
            try
            {
                byte[] key;
                if (Regex.IsMatch(hex, "^[0-9a-fA-F]{32}$")) { key = new byte[16]; for (int i = 0; i < 16; i++) key[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16); }
                else key = Convert.FromBase64String(text);
                if (key.Length != 16) throw new FormatException(); return key;
            }
            catch (FormatException) { throw new FormatException("密钥须为 16 字节：32 位十六进制或对应的 Base64 文本。"); }
        }
        public static bool Placeholder(Uri uri)
        { return uri.Host.Equals("example.invalid", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase); }
        public static string Redact(string value)
        {
            value = Regex.Replace(value ?? "", @"https?://[^\s\""<>]+", "[地址已隐藏]", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"(?i)\b(key|token|password|pwd|authorization|cookie)\s*[:=]\s*\S+", "$1=[已隐藏]");
            value = Regex.Replace(value, @"[A-Za-z]:[\\/][^\r\n\""<>]*", "[本地路径]");
            return value;
        }
        public static string HashFile(string path, CancellationToken token)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
            {
                var buffer = new byte[1024 * 1024]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                { token.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, count, buffer, 0); }
                sha.TransformFinalBlock(new byte[0], 0, 0); return Hex(sha.Hash);
            }
        }
        public static void Replace(string source, string destination)
        { if (File.Exists(destination)) File.Replace(source, destination, null); else File.Move(source, destination); }
        public static void SaveJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".part";
            File.WriteAllText(temporary, Json().Serialize(value), Utf8); Replace(temporary, path);
        }
        public static string SafeName(string value)
        {
            string name = Regex.Replace(value, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim(' ', '.');
            if (name.Length > 100) name = name.Substring(0, 100).TrimEnd(' ', '.');
            if (name.Length == 0) name = "未命名";
            if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase)) name = "_" + name;
            return name;
        }
        public static bool Inside(string path, string root)
        { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        public static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(ch);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static void RemoveCache(string directory, string cacheRoot)
        {
            if (!Inside(directory, cacheRoot) || !Regex.IsMatch(Path.GetFileName(directory), @"^[0-9a-f]{20}$")) throw new IOException("缓存目录不在指定位置，已停止清理。");
            if (!Directory.Exists(directory)) return;
            foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new IOException("缓存中存在目录链接，已停止清理。");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("缓存目录是链接，已停止清理。");
            Directory.Delete(directory, true);
        }
    }

    public sealed class Lesson
    {
        public string Title, Url, Identity;
        public string[] Groups;
        public byte[] Key;
        public string DisplayPath { get { return string.Join(" / ", Groups.Concat(new[] { Title })); } }
    }
    public sealed class CatalogNode
    {
        public string Title, RawKey, RawUrl, Error;
        public int Indent;
        public CatalogNode Parent;
        public Lesson Lesson;
        public readonly List<CatalogNode> Children = new List<CatalogNode>();
        public readonly List<ResourceLink> Resources = new List<ResourceLink>();
        public string DisplayPath { get { return Parent == null ? Title : Parent.DisplayPath + " / " + Title; } }
    }
    public sealed class ResourceLink
    {
        public string Name, Url, Code, Error;
        public string ShareText { get { return Name + ": " + Url + (string.IsNullOrEmpty(Code) ? "" : "\r\n" + (L.English ? "Access code: " : "提取码：") + Code); } }
        public bool CanOpen { get { Uri uri; return Error == null && Uri.TryCreate(Url, UriKind.Absolute, out uri) && !Files.Placeholder(uri); } }
        public override string ToString() { return Name + (string.IsNullOrEmpty(Code) ? "" : (L.English ? " · Access code " : " · 提取码 ") + Code) + (Error == null ? "" : L.T("（地址无效）")); }
        public static ResourceLink Parse(string name, string value)
        {
            var result = new ResourceLink { Name = name, Code = "" };
            Match match = Regex.Match(value, @"https?://[^\s<>]+", RegexOptions.IgnoreCase);
            if (!match.Success) { result.Url = value.Trim(); result.Error = "未找到 HTTP/HTTPS 资源地址"; return result; }
            result.Url = match.Value;
            try { Catalog.ValidateUrl(result.Url); }
            catch (FormatException) { result.Error = "资源地址格式无效"; return result; }
            string tail = value.Substring(match.Index + match.Length).Trim();
            Match code = Regex.Match(tail, @"(?:提取码|密码|访问码)\s*[:：=]?\s*([a-zA-Z0-9]+)");
            if (code.Success) result.Code = code.Groups[1].Value;
            else if (Regex.IsMatch(tail, "^[a-zA-Z0-9]{4}$")) result.Code = tail;
            else
            {
                Match query = Regex.Match(new Uri(result.Url).Query, @"(?:\?|&)(?:pwd|password|code)=([^&]+)", RegexOptions.IgnoreCase);
                if (query.Success) result.Code = Uri.UnescapeDataString(query.Groups[1].Value);
            }
            return result;
        }
    }
    public sealed class Catalog
    {
        public readonly List<CatalogNode> Roots = new List<CatalogNode>();
        public readonly List<Lesson> Lessons = new List<Lesson>();
        public bool IsDemo;
        public int ResourceCount { get { return AllNodes().Sum(x => x.Resources.Count); } }
        public IEnumerable<CatalogNode> AllNodes()
        { foreach (CatalogNode node in Roots) foreach (CatalogNode child in Walk(node)) yield return child; }
        private static IEnumerable<CatalogNode> Walk(CatalogNode node)
        { yield return node; foreach (CatalogNode child in node.Children) foreach (CatalogNode item in Walk(child)) yield return item; }
        private static int Indentation(string raw)
        { int count = 0; foreach (char ch in raw) { if (ch == '\t') count += 4 - count % 4; else if (ch == ' ') count++; else break; } return count; }
        public static void ValidateUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new FormatException("视频地址必须是有效的 HTTP 或 HTTPS 地址。");
        }
        public static Catalog Parse(string text)
        {
            var result = new Catalog(); var stack = new List<CatalogNode>(); var all = new List<CatalogNode>(); CatalogNode current = null;
            foreach (string original in text.TrimStart('\ufeff').Replace("\r", "").Split('\n'))
            {
                string raw = original.Replace('\u00a0', ' '), line = raw.Trim();
                if (line == "# MT001-DEMO") { result.IsDemo = true; continue; }
                if (line.Length == 0 || line.StartsWith("#") || Regex.IsMatch(line, @"^[-=]+$")) continue;
                Match property = Regex.Match(line, @"^(.+?)\s*->\s*(.*)$");
                if (property.Success)
                {
                    if (current == null) throw new FormatException("属性前缺少条目名称。");
                    CatalogNode owner = stack.LastOrDefault(x => x.Indent < Indentation(raw)) ?? current;
                    string name = property.Groups[1].Value.Trim(), value = property.Groups[2].Value.Trim();
                    if (name.Equals("key", StringComparison.OrdinalIgnoreCase))
                    { if (owner.RawKey != null) owner.Error = "本节课重复填写了密钥"; owner.RawKey = value; }
                    else if (name.Equals("hls", StringComparison.OrdinalIgnoreCase))
                    { if (owner.RawUrl != null) owner.Error = "本节课重复填写了地址"; owner.RawUrl = value; }
                    else if (Regex.IsMatch(value, @"https?://", RegexOptions.IgnoreCase) || name == "源码地址" || name == "其他资料" || name == "课前预习")
                        owner.Resources.Add(ResourceLink.Parse(name, value));
                    continue;
                }
                if (line.Contains("->") || line.StartsWith("http://") || line.StartsWith("https://")) continue;
                int indent = Indentation(raw);
                while (stack.Count > 0 && stack[stack.Count - 1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
                current = new CatalogNode { Title = line, Indent = indent, Parent = stack.Count == 0 ? null : stack[stack.Count - 1] };
                if (current.Parent == null) result.Roots.Add(current); else current.Parent.Children.Add(current);
                all.Add(current); stack.Add(current);
            }
            foreach (CatalogNode node in all)
            {
                if (node.RawKey == null && node.RawUrl == null) continue;
                try
                {
                    if (node.Error != null) throw new FormatException(node.Error);
                    if (string.IsNullOrWhiteSpace(node.RawUrl)) throw new FormatException("缺少视频地址");
                    byte[] key = Files.ParseKey(node.RawKey);
                    ValidateUrl(node.RawUrl);
                    var groups = new List<string>(); for (CatalogNode p = node.Parent; p != null; p = p.Parent) groups.Insert(0, p.Title);
                    node.Lesson = new Lesson { Title = node.Title, Groups = groups.ToArray(), Url = node.RawUrl, Key = key, Identity = Files.Identity(node.RawUrl, key) };
                    result.Lessons.Add(node.Lesson);
                }
                catch (Exception ex) { if (!(ex is FormatException || ex is ArgumentException)) throw; node.Error = ex.Message; }
            }
            if (all.Count == 0) throw new FormatException("清单中没有找到条目。");
            return result;
        }
    }

    public sealed class AppConfig
    {
        public string CatalogPath = "demo-catalog.txt";
        public string OutputRoot = "mp4";
        public string PotPlayerPath = "";
        public bool AutoPlay = true;
        public string Language = "zh";
        public string LastLessonIdentity = "";
        public static string Resolve(string root, string value) { return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value)); }
        public static AppConfig Load(string root)
        {
            string path = Path.Combine(root, "settings.json"); AppConfig config = new AppConfig();
            if (File.Exists(path))
            {
                try { config = Files.Json().Deserialize<AppConfig>(File.ReadAllText(path, Encoding.UTF8)) ?? config; }
                catch (Exception ex) { if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException)) throw; }
            }
            if (string.IsNullOrWhiteSpace(config.CatalogPath)) config.CatalogPath = new AppConfig().CatalogPath;
            if (string.IsNullOrWhiteSpace(config.OutputRoot)) config.OutputRoot = "mp4";
            if (!File.Exists(config.PotPlayerPath)) config.PotPlayerPath = FindPotPlayer();
            return config;
        }
        public static string FindPotPlayer()
        {
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct())
                foreach (string name in new[] { "PotPlayerMini64.exe", "PotPlayerMini.exe", "PotPlayer64.exe" })
                { string path = Path.Combine(folder, "DAUM", "PotPlayer", name); if (File.Exists(path)) return path; }
            return "";
        }
        public void Save(string root) { Files.SaveJson(Path.Combine(root, "settings.json"), this); }
    }
    public sealed class Receipt
    {
        public string title, source, identity, sha256;
        public double duration_seconds;
        public int segments, frames_checked;
        public long bytes;
        public bool full_decode_verified;
    }
    public sealed class ExistingVideo { public string Path; public Receipt Receipt; }
    public sealed class VideoIndex
    {
        private readonly Dictionary<string, List<ExistingVideo>> entries = new Dictionary<string, List<ExistingVideo>>(StringComparer.Ordinal);
        public void Scan(string outputRoot)
        {
            entries.Clear(); if (!Directory.Exists(outputRoot)) return;
            foreach (string file in Directory.EnumerateFiles(outputRoot, "*.mp4.json", SearchOption.AllDirectories))
            {
                try
                {
                    Receipt receipt = Files.Json().Deserialize<Receipt>(File.ReadAllText(file, Encoding.UTF8));
                    string video = file.Substring(0, file.Length - 5);
                    if (receipt == null || string.IsNullOrEmpty(receipt.identity) || !receipt.full_decode_verified || !File.Exists(video) || new FileInfo(video).Length != receipt.bytes) continue;
                    List<ExistingVideo> list; if (!entries.TryGetValue(receipt.identity, out list)) entries[receipt.identity] = list = new List<ExistingVideo>();
                    list.Add(new ExistingVideo { Path = video, Receipt = receipt });
                }
                catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)) throw; }
            }
        }
        public ExistingVideo Quick(string identity) { List<ExistingVideo> list; return entries.TryGetValue(identity, out list) ? list.FirstOrDefault() : null; }
        public ExistingVideo Verified(string identity, CancellationToken token)
        {
            List<ExistingVideo> list; if (!entries.TryGetValue(identity, out list)) return null;
            foreach (ExistingVideo video in list)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(video.Path) && new FileInfo(video.Path).Length == video.Receipt.bytes && string.Equals(Files.HashFile(video.Path, token), video.Receipt.sha256, StringComparison.OrdinalIgnoreCase)) return video;
            }
            return null;
        }
    }

    public sealed class Segment
    { public Uri Url, KeyUrl; public double Duration; public byte[] Iv; public bool Encrypted, Discontinuity; }
    public sealed class ConversionRequest
    { public string Url, OutputPath; public byte[] Key; public bool Overwrite; }
    public sealed class FetchResult
    { public byte[] Data; public Uri FinalUri; public FetchResult(byte[] data, Uri uri) { Data = data; FinalUri = uri; } }
    public sealed class ConversionProgress
    {
        public string Stage, Message; public int Percent;
        public ConversionProgress(string stage, int percent, string message) { Stage = stage; Percent = Math.Max(0, Math.Min(100, percent)); Message = message; }
    }
    public sealed class ConversionResult
    { public string Path; public bool Reused; public string Warning; }
    public sealed class FfmpegResult
    { public long TimeUs; public int Frames; }

    public sealed class ConversionEngine : IDisposable
    {
        internal static long FetchRequestCount;
        public readonly string OutputRoot, Ffmpeg;
        private readonly HttpClient client;
        public Func<Uri, CancellationToken, Task<FetchResult>> FetchOverride;
        public ConversionEngine(string outputRoot, string ffmpeg)
        {
            OutputRoot = Path.GetFullPath(outputRoot); Ffmpeg = ffmpeg;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = Math.Max(8, ServicePointManager.DefaultConnectionLimit);
            client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            client.Timeout = Timeout.InfiniteTimeSpan; client.DefaultRequestHeaders.UserAgent.ParseAdd("MT001/1.0");
        }
        public void Dispose() { client.Dispose(); }
        public async Task<FetchResult> FetchAsync(Uri uri, CancellationToken token)
        {
            Interlocked.Increment(ref FetchRequestCount);
            Catalog.ValidateUrl(uri.AbsoluteUri);
            if (Files.Placeholder(uri)) throw new InvalidOperationException("虚拟示例地址仅用于展示，请输入真实地址或导入自己的清单。");
            if (FetchOverride != null) return await FetchOverride(uri, token).ConfigureAwait(false);
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        using (HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode(); Catalog.ValidateUrl(response.RequestMessage.RequestUri.AbsoluteUri);
                            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var memory = new MemoryStream())
                            {
                                var buffer = new byte[65536]; int count;
                                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                { if (memory.Length + count > 64 * 1024 * 1024) throw new InvalidDataException("单个分片超过大小上限。"); memory.Write(buffer, 0, count); }
                                return new FetchResult(memory.ToArray(), response.RequestMessage.RequestUri);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is HttpRequestException || ex is IOException || ex is TaskCanceledException)) throw;
                        token.ThrowIfCancellationRequested(); if (attempt >= 2) throw new IOException("下载失败（已重试）：" + Files.Redact(ex.Message));
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token).ConfigureAwait(false);
            }
        }
        public static Dictionary<string, string> Attributes(string line)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(line.Substring(line.IndexOf(':') + 1), @"([A-Z0-9-]+)=(""[^""]*""|[^,]*)")) result[match.Groups[1].Value] = match.Groups[2].Value.Trim('"');
            return result;
        }
        public static byte[] IvForSequence(ulong sequence)
        { var iv = new byte[16]; for (int i = 15; i >= 8; i--) { iv[i] = (byte)(sequence & 255); sequence >>= 8; } return iv; }
        private static byte[] ParseIv(string value)
        {
            string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;
            if (hex.Length > 32 || !Regex.IsMatch(hex, @"^[a-fA-F0-9]+$")) throw new FormatException("播放列表中的 IV 无效。");
            hex = hex.PadLeft(32, '0'); var data = new byte[16];
            for (int i = 0; i < 16; i++) data[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return data;
        }
        public static List<Segment> ParseManifest(string text, Uri baseUri)
        {
            string[] lines = text.TrimStart('\ufeff').Replace("\r", "").Split('\n').Select(x => x.Trim()).ToArray();
            if (lines.Length == 0 || lines[0] != "#EXTM3U") throw new FormatException("服务器没有返回有效的 HLS 播放列表。");
            if (!lines.Contains("#EXT-X-ENDLIST")) throw new FormatException("目前仅处理有结束标记的点播视频，不支持直播。");
            var segments = new List<Segment>(); var keys = new HashSet<string>(); ulong sequence = 0; byte[] iv = null;
            Uri keyUrl = null;
            bool encrypted = false, discontinuity = false; double? duration = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("#EXT-X-MAP:") || line.StartsWith("#EXT-X-BYTERANGE:")) throw new FormatException("暂不支持 fMP4 或 BYTERANGE 格式。");
                if (line.StartsWith("#EXT-X-MEDIA:") && Attributes(line).ContainsKey("URI")) throw new FormatException("暂不支持独立音轨或外置媒体轨道。");
                if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:")) sequence = ulong.Parse(line.Split(':')[1], CultureInfo.InvariantCulture);
                else if (line.StartsWith("#EXT-X-KEY:"))
                {
                    var data = Attributes(line); string method; data.TryGetValue("METHOD", out method);
                    if (method == "NONE") { encrypted = false; iv = null; continue; }
                    if (method != "AES-128" || (data.ContainsKey("KEYFORMAT") && data["KEYFORMAT"] != "identity")) throw new FormatException("仅支持标准 AES-128；暂不支持 SAMPLE-AES 或 DRM。");
                    if (!data.ContainsKey("URI")) throw new FormatException("加密播放列表缺少密钥 URI。");
                    keyUrl = new Uri(baseUri, data["URI"]); Catalog.ValidateUrl(keyUrl.AbsoluteUri); keys.Add(keyUrl.AbsoluteUri);
                    if (keys.Count > 1) throw new FormatException("本视频轮换了多把密钥，不能使用单一密钥处理。");
                    encrypted = true; iv = data.ContainsKey("IV") ? ParseIv(data["IV"]) : null;
                }
                else if (line.StartsWith("#EXTINF:"))
                {
                    double value = double.Parse(line.Substring(8).Split(',')[0], CultureInfo.InvariantCulture);
                    if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new FormatException("分片时长无效。");
                    duration = value;
                }
                else if (line == "#EXT-X-DISCONTINUITY") discontinuity = true;
                else if (line.Length > 0 && !line.StartsWith("#"))
                {
                    if (!duration.HasValue) throw new FormatException("分片缺少对应的时长。");
                    var uri = new Uri(baseUri, line); Catalog.ValidateUrl(uri.AbsoluteUri);
                    segments.Add(new Segment { Url = uri, KeyUrl = encrypted ? keyUrl : null, Duration = duration.Value, Iv = encrypted ? (iv ?? IvForSequence(checked(sequence + (ulong)segments.Count))) : null, Encrypted = encrypted, Discontinuity = discontinuity });
                    duration = null; discontinuity = false;
                }
            }
            if (segments.Count == 0) throw new FormatException("没有找到视频分片。");
            return segments;
        }
        public async Task<List<Segment>> ReadPlaylistAsync(string address, CancellationToken token)
        {
            Uri uri = new Uri(address);
            for (int depth = 0; depth < 4; depth++)
            {
                FetchResult response = await FetchAsync(uri, token).ConfigureAwait(false);
                if (response.Data.Length > 8 * 1024 * 1024) throw new FormatException("播放列表过大。");
                string text = Encoding.UTF8.GetString(response.Data); string[] lines = text.Replace("\r", "").Split('\n').Select(x => x.Trim()).ToArray();
                if (lines.Any(x => x.StartsWith("#EXT-X-MEDIA:") && Attributes(x).ContainsKey("URI"))) throw new FormatException("暂不支持独立音轨或外置媒体轨道。");
                Uri best = null; long bandwidth = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].StartsWith("#EXT-X-STREAM-INF:")) continue;
                    var data = Attributes(lines[i]); long value = data.ContainsKey("BANDWIDTH") ? long.Parse(data["BANDWIDTH"], CultureInfo.InvariantCulture) : 0;
                    string target = lines.Skip(i + 1).FirstOrDefault(x => x.Length > 0 && !x.StartsWith("#"));
                    if (target != null && value > bandwidth) { bandwidth = value; best = new Uri(response.FinalUri, target); }
                }
                if (best == null) return ParseManifest(text, response.FinalUri); uri = best;
            }
            throw new FormatException("播放列表嵌套过深。");
        }
        public static bool ValidTs(byte[] data)
        { if (data.Length == 0 || data.Length % 188 != 0) return false; for (int i = 0; i < data.Length; i += 188) if (data[i] != 0x47) return false; return true; }
        public static byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
        {
            if (data.Length == 0 || data.Length % 16 != 0) throw new InvalidDataException("加密分片不完整。");
            try
            {
                using (Aes aes = Aes.Create())
                { aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    { byte[] plain = decryptor.TransformFinalBlock(data, 0, data.Length); if (!ValidTs(plain)) throw new InvalidDataException("密钥不匹配，或视频分片格式不受支持。"); return plain; }
                }
            }
            catch (CryptographicException) { throw new InvalidDataException("解密失败，请检查密钥与视频地址是否配套。"); }
        }
        private static void Report(IProgress<ConversionProgress> progress, string stage, int percent, string message)
        { if (progress != null) progress.Report(new ConversionProgress(stage, percent, message)); }
        public static string Destination(string root, Lesson lesson)
        {
            string path = root; foreach (string group in lesson.Groups) path = Path.Combine(path, Files.SafeName(group));
            path = Path.Combine(path, Files.SafeName(lesson.Title) + ".mp4");
            if (!Files.Inside(path, root)) throw new IOException("输出文件超出了视频目录。"); return path;
        }
        public async Task<ConversionResult> ConvertAsync(Lesson lesson, CancellationToken token, IProgress<ConversionProgress> progress)
        {
            Report(progress, "检查本地文件", 0, "正在查找可复用的视频…");
            var index = new VideoIndex(); index.Scan(OutputRoot); ExistingVideo existing = index.Verified(lesson.Identity, token);
            if (existing != null) { Report(progress, "已完成", 100, "已验证并复用本地 MP4"); return new ConversionResult { Path = existing.Path, Reused = true }; }
            return await ConvertAsync(new ConversionRequest { Url = lesson.Url, Key = lesson.Key, OutputPath = Destination(OutputRoot, lesson) }, token, progress).ConfigureAwait(false);
        }
        public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken token, IProgress<ConversionProgress> progress)
        {
            Catalog.ValidateUrl(request.Url); token.ThrowIfCancellationRequested();
            if (Files.Placeholder(new Uri(request.Url))) throw new InvalidOperationException("虚拟示例地址仅用于展示，请输入真实地址。");
            if (request.Key != null && request.Key.Length != 16) throw new FormatException("AES-128 密钥必须为 16 字节。");
            if (!File.Exists(Ffmpeg)) throw new FileNotFoundException("未找到 ffmpeg.exe，请将它放在程序目录中。", Ffmpeg);
            string destination = Path.GetFullPath(request.OutputPath);
            if (!destination.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) throw new FormatException("输出文件必须使用 .mp4 扩展名。");
            byte[] key = request.Key; string identity = Files.Identity(request.Url, key);
            if (TryReuse(destination, identity, token)) return new ConversionResult { Path = destination, Reused = true };
            bool existed = File.Exists(destination);
            if (existed && !request.Overwrite) throw new IOException("输出文件已存在，请确认覆盖或选择其他文件名。");
            Report(progress, "读取播放列表", 0, "正在读取所选视频的播放列表…");
            List<Segment> segments = await ReadPlaylistAsync(request.Url, token).ConfigureAwait(false);
            if (key == null && segments.Any(x => x.Encrypted))
            {
                Report(progress, "读取密钥", 0, "正在读取播放列表指定的 AES-128 密钥…");
                FetchResult response = await FetchAsync(segments.First(x => x.Encrypted).KeyUrl, token).ConfigureAwait(false);
                if (response.Data.Length != 16) throw new InvalidDataException("密钥地址未返回 16 字节的原始 AES-128 密钥，可尝试手动输入。");
                key = response.Data;
            }
            identity = Files.Identity(request.Url, key);
            if (TryReuse(destination, identity, token)) return new ConversionResult { Path = destination, Reused = true };
            string cacheRoot = Path.Combine(OutputRoot, ".cache"), cache = Path.Combine(cacheRoot, identity.Substring(0, 20));
            if (!Files.Inside(cache, cacheRoot)) throw new IOException("缓存路径无效。");
            Directory.CreateDirectory(cache); Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporaryMp4 = Path.Combine(cache, "video.partial.mp4");
            string[] files = new string[segments.Count]; int next = -1, completed = 0, hits = 0; Exception firstError = null;
            using (var workersToken = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Func<Task> worker = async delegate
                {
                    try
                    {
                        while (true)
                        {
                            workersToken.Token.ThrowIfCancellationRequested(); int n = Interlocked.Increment(ref next); if (n >= segments.Count) break;
                            Segment segment = segments[n]; string fingerprint = Files.Hash(Encoding.UTF8.GetBytes(segment.Url.AbsoluteUri + (segment.Encrypted ? Files.Hex(segment.Iv) : "plain"))).Substring(0, 12);
                            string path = Path.Combine(cache, n.ToString("D5", CultureInfo.InvariantCulture) + "-" + fingerprint + ".ts");
                            bool reused = false;
                            if (File.Exists(path) && File.Exists(Path.ChangeExtension(path, ".sha256")))
                            { byte[] local = File.ReadAllBytes(path); reused = ValidTs(local) && Files.Hash(local) == File.ReadAllText(Path.ChangeExtension(path, ".sha256"), Encoding.ASCII).Trim(); }
                            if (!reused)
                            {
                                FetchResult response = await FetchAsync(segment.Url, workersToken.Token).ConfigureAwait(false);
                                byte[] plain = segment.Encrypted ? Decrypt(response.Data, key, segment.Iv) : response.Data;
                                if (!ValidTs(plain)) throw new InvalidDataException("第 " + (n + 1) + " 个分片格式无效。");
                                workersToken.Token.ThrowIfCancellationRequested();
                                string part = Path.ChangeExtension(path, ".part"); File.WriteAllBytes(part, plain); Files.Replace(part, path);
                                File.WriteAllText(Path.ChangeExtension(path, ".sha256"), Files.Hash(plain), Encoding.ASCII);
                            }
                            else Interlocked.Increment(ref hits);
                            files[n] = path; int done = Interlocked.Increment(ref completed);
                            Report(progress, "下载", done * 100 / segments.Count, "已完成 " + done + " / " + segments.Count + " 个分片 · 缓存复用 " + Volatile.Read(ref hits));
                        }
                    }
                    catch (Exception ex)
                    { if (!(ex is OperationCanceledException) || !workersToken.IsCancellationRequested) Interlocked.CompareExchange(ref firstError, ex, null); workersToken.Cancel(); }
                };
                Task[] workers = Enumerable.Range(0, Math.Min(4, segments.Count)).Select(x => worker()).ToArray();
                await Task.WhenAll(workers).ConfigureAwait(false); token.ThrowIfCancellationRequested();
                if (firstError != null) throw firstError;
                if (files.Any(x => x == null)) throw new IOException("下载未能完成，请重试。");
            }
            double duration = segments.Sum(x => x.Duration);
            var manifest = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-PLAYLIST-TYPE:VOD\n#EXT-X-TARGETDURATION:");
            manifest.Append(Math.Ceiling(segments.Max(x => x.Duration)).ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < segments.Count; i++)
            { if (segments[i].Discontinuity) manifest.Append("#EXT-X-DISCONTINUITY\n"); manifest.Append("#EXTINF:").Append(segments[i].Duration.ToString("F6", CultureInfo.InvariantCulture)).Append(",\n").Append(Path.GetFileName(files[i])).Append('\n'); }
            manifest.Append("#EXT-X-ENDLIST\n"); string localManifest = Path.Combine(cache, "decrypted.m3u8"); File.WriteAllText(localManifest, manifest.ToString(), Files.Utf8);
            Report(progress, "封装", 0, "正在无损封装为 MP4…");
            await RunFfmpegAsync(new[] { "-loglevel", "warning", "-i", localManifest, "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", "-y", temporaryMp4 }, token, progress, "封装", duration, Path.Combine(cache, "remux.log")).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); Report(progress, "校验", 0, "正在校验整段音视频…");
            FfmpegResult decoded = await RunFfmpegAsync(new[] { "-loglevel", "error", "-xerror", "-i", temporaryMp4, "-map", "0:v:0", "-map", "0:a:0?", "-progress", "pipe:1", "-nostats", "-f", "null", "-" }, token, progress, "校验", duration, Path.Combine(cache, "decode.log")).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (decoded.Frames <= 0 || Math.Abs(decoded.TimeUs / 1000000.0 - duration) > 2) throw new InvalidDataException("视频帧或时长校验未通过。已保留缓存供重试。");
            Report(progress, "校验", 100, "正在保存校验记录…");
            var receipt = new Receipt { identity = identity, duration_seconds = duration, segments = segments.Count, bytes = new FileInfo(temporaryMp4).Length, sha256 = Files.HashFile(temporaryMp4, token), full_decode_verified = true, frames_checked = decoded.Frames };
            token.ThrowIfCancellationRequested();
            if (!request.Overwrite && File.Exists(destination)) throw new IOException("转换期间出现同名文件，已保留缓存，请重新确认输出位置。");
            if (request.Overwrite) Files.Replace(temporaryMp4, destination); else File.Move(temporaryMp4, destination);
            Files.SaveJson(destination + ".json", receipt);
            string warning = null; try { Files.RemoveCache(cache, cacheRoot); } catch (IOException) { warning = "视频已完成，部分缓存暂时无法清理。"; } catch (UnauthorizedAccessException) { warning = "视频已完成，部分缓存暂时无法清理。"; }
            Report(progress, "已完成", 100, "视频已转换并通过全片校验"); return new ConversionResult { Path = destination, Warning = warning };
        }
        private static bool TryReuse(string path, string identity, CancellationToken token)
        {
            try
            {
                if (!File.Exists(path) || !File.Exists(path + ".json")) return false;
                Receipt receipt = Files.Json().Deserialize<Receipt>(File.ReadAllText(path + ".json", Encoding.UTF8));
                return receipt != null && receipt.full_decode_verified && receipt.identity == identity && receipt.bytes == new FileInfo(path).Length && receipt.sha256 == Files.HashFile(path, token);
            }
            catch (Exception ex) { if (ex is OperationCanceledException) throw; if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException)) throw; return false; }
        }
        private Task<FfmpegResult> RunFfmpegAsync(string[] arguments, CancellationToken token, IProgress<ConversionProgress> progress, string stage, double duration, string logPath)
        {
            return Task.Run(delegate
            {
                token.ThrowIfCancellationRequested(); var state = new FfmpegResult(); var errors = new StringBuilder(); object sync = new object();
                using (var process = new Process())
                using (var log = new StreamWriter(logPath, false, Files.Utf8))
                {
                    process.StartInfo = new ProcessStartInfo { FileName = Ffmpeg, Arguments = string.Join(" ", new[] { "-nostdin", "-hide_banner" }.Concat(arguments).Select(Files.Quote)), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return; int equal = e.Data.IndexOf('='); if (equal < 0) return;
                        string name = e.Data.Substring(0, equal), value = e.Data.Substring(equal + 1); long number;
                        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return;
                        if (name == "frame") state.Frames = (int)Math.Min(int.MaxValue, number);
                        if (name == "out_time_us") { state.TimeUs = number; Report(progress, stage, (int)(number / 1000000.0 / duration * 100), stage == "封装" ? "正在无损封装为 MP4…" : "正在校验整段音视频…"); }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    { if (e.Data == null) return; lock (sync) { string safe = Files.Redact(e.Data); log.WriteLine(safe); errors.AppendLine(safe); if (errors.Length > 16000) errors.Remove(0, errors.Length - 16000); } };
                    process.Start();
                    using (token.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }))
                    { process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit(); token.ThrowIfCancellationRequested(); if (process.ExitCode != 0) throw new IOException("FFmpeg " + stage + "失败：\n" + errors); }
                }
                return state;
            }, token);
        }
    }

    public interface IPlayerLauncher { void Open(string player, string video); }
    public sealed class PotPlayerLauncher : IPlayerLauncher
    {
        public void Open(string player, string video)
        {
            if (!File.Exists(player)) throw new FileNotFoundException("找不到 PotPlayer，请重新选择播放器。", player);
            if (!File.Exists(video)) throw new FileNotFoundException("找不到视频文件。", video);
            Process.Start(new ProcessStartInfo { FileName = player, Arguments = Files.Quote(video), UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(player) });
        }
    }
}
