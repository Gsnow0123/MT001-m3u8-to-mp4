using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CourseStudy;

public sealed class DirectProgress : IProgress<ConversionProgress>
{
    public readonly List<ConversionProgress> Events = new List<ConversionProgress>();
    public Action<ConversionProgress> OnValue;
    public void Report(ConversionProgress value) { lock (Events) Events.Add(value); if (OnValue != null) OnValue(value); }
}
public sealed class RecordingPlayer : IPlayerLauncher
{
    public int Calls; public string LastPath;
    public void Open(string player, string video) { if (!File.Exists(video)) throw new Exception("Missing video"); Calls++; LastPath = video; }
}
public sealed class Fixture
{
    public string Manifest;
    public readonly Dictionary<string, byte[]> Segments = new Dictionary<string, byte[]>();
    public readonly Dictionary<string, int> Requests = new Dictionary<string, int>();
    public bool Slow;
    public readonly byte[] Key;
    public Fixture(string folder, byte[] key, bool encrypted = true, bool sequenceIv = false)
    {
        Key = key; string plain = File.ReadAllText(Path.Combine(folder, "index.m3u8"), Encoding.UTF8);
        if (sequenceIv) plain = plain.Replace("#EXT-X-MEDIA-SEQUENCE:0", "#EXT-X-MEDIA-SEQUENCE:1");
        Manifest = encrypted ? plain.Replace("#EXTM3U", "#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"test.key\"" + (sequenceIv ? "" : ",IV=0x00000000000000000000000000000001")) : plain;
        int sequence = 0;
        foreach (string line in plain.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith("#")) continue;
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, line));
            sequence++;
            if (!encrypted) { Segments[line] = bytes; continue; }
            using (Aes aes = Aes.Create())
            { aes.Key = key; aes.IV = ConversionEngine.IvForSequence((ulong)(sequenceIv ? sequence : 1)); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; using (ICryptoTransform transform = aes.CreateEncryptor()) Segments[line] = transform.TransformFinalBlock(bytes, 0, bytes.Length); }
        }
    }
    public async Task<FetchResult> Fetch(Uri uri, CancellationToken token)
    {
        string name = Path.GetFileName(uri.AbsolutePath);
        if (name.EndsWith(".m3u8")) return new FetchResult(Encoding.UTF8.GetBytes(Manifest), uri);
        if (name == "test.key") return new FetchResult(Key, uri);
        lock (Requests) { if (!Requests.ContainsKey(name)) Requests[name] = 0; Requests[name]++; }
        if (Slow) await Task.Delay(name.Contains("00") ? 20 : 600, token);
        token.ThrowIfCancellationRequested(); return new FetchResult(Segments[name], uri);
    }
}
public sealed class HttpFixture : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    public int RetryRequests;
    public readonly string BaseUri;
    public HttpFixture()
    {
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        BaseUri = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
        Task.Run(async delegate
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); } catch { break; }
                Task.Run(async delegate
                {
                    using (client)
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream(); string request;
                            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                            { request = await reader.ReadLineAsync(); string line; while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { } }
                            bool slow = request != null && request.Contains("/slow");
                            bool fail = !slow && Interlocked.Increment(ref RetryRequests) <= 2;
                            if (slow) await Task.Delay(3000, stop.Token);
                            byte[] body = Encoding.UTF8.GetBytes(fail ? "retry" : "ok");
                            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + (fail ? "503 Service Unavailable" : "200 OK") + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                            await stream.WriteAsync(header, 0, header.Length); await stream.WriteAsync(body, 0, body.Length);
                        }
                        catch { }
                    }
                });
            }
        });
    }
    public void Dispose() { stop.Cancel(); listener.Stop(); stop.Dispose(); }
}

public static class CourseTests
{
    private static string Root, Work, Ffmpeg, FixtureFolder, CatalogPath;
    private static Catalog Courses;
    private static int passed;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Run(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }
    private static T Wait<T>(Task<T> task) { return task.GetAwaiter().GetResult(); }
    private static Lesson MakeLesson(byte[] key, string title)
    { return Catalog.Parse(title + "\n\tkey-> " + Convert.ToBase64String(key) + "\n\thls-> https://fixture.test/index.m3u8").Lessons[0]; }
    private static void Pump(Task task)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); if (clock.Elapsed.TotalSeconds > 90) throw new Exception("UI test timed out"); }
        task.GetAwaiter().GetResult(); Application.DoEvents();
    }
    private static void StartAndPump(MainForm form)
    {
        // Each standalone test starts a UI action outside Application.Run.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Pump(form.StartSelectedAsync());
    }
    private static TreeNode Find(TreeNodeCollection nodes, string identity)
    {
        foreach (TreeNode node in nodes)
        { var source = node.Tag as CatalogNode; if (source != null && source.Lesson != null && source.Lesson.Identity == identity) return node; TreeNode found = Find(node.Nodes, identity); if (found != null) return found; }
        return null;
    }
    private static void CreateFixture()
    {
        FixtureFolder = Path.Combine(Work, "fixture"); Directory.CreateDirectory(FixtureFolder);
        string[] args = { "-nostdin", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=15", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=44100", "-t", "8", "-c:v", "mpeg2video", "-g", "15", "-sc_threshold", "0", "-c:a", "aac", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(FixtureFolder, "segment-%02d.ts"), "-y", Path.Combine(FixtureFolder, "index.m3u8") };
        using (var process = Process.Start(new ProcessStartInfo { FileName = Ffmpeg, Arguments = string.Join(" ", args.Select(Files.Quote)), UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }))
        { string error = process.StandardError.ReadToEnd(); process.WaitForExit(); Check(process.ExitCode == 0, error); }
    }
    private static string MakeUiRoot(string name, string output, bool autoPlay)
    {
        string path = Path.Combine(Work, name); Directory.CreateDirectory(path);
        new AppConfig { CatalogPath = CatalogPath, OutputRoot = output, AutoPlay = autoPlay, PotPlayerPath = AppConfig.FindPotPlayer() }.Save(path); return path;
    }
    private static string MakeBatchRoot(string name, bool autoPlay)
    {
        string path = MakeUiRoot(name, Path.Combine(Work, name + "-output"), autoPlay);
        string catalog = Path.Combine(path, "batch.txt");
        File.WriteAllText(catalog, "测试章节\n\t第一节\n\t\tkey-> AAAAAAAAAAAAAAAAAAAAAA==\n\t\thls-> https://fixture.test/a/index.m3u8\n\t第二节\n\t\tkey-> AAAAAAAAAAAAAAAAAAAAAA==\n\t\thls-> https://fixture.test/b/index.m3u8\n\t第三节\n\t\tkey-> AAAAAAAAAAAAAAAAAAAAAA==\n\t\thls-> https://fixture.test/c/index.m3u8\n\t重复的第一节\n\t\tkey-> AAAAAAAAAAAAAAAAAAAAAA==\n\t\thls-> https://fixture.test/a/index.m3u8\n\t暂无视频\n", Files.Utf8);
        AppConfig config = AppConfig.Load(path); config.CatalogPath = catalog; config.Save(path); return path;
    }
    private static void ShowOffscreen(MainForm form)
    {
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false;
        form.Show(); Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
    private static void BatchTests()
    {
        Run("chapter selection, search persistence, unavailable entries and global clear stay offline", delegate
        {
            long calls = ConversionEngine.FetchRequestCount; string uiRoot = MakeBatchRoot("batch-selection", false);
            using (var form = new MainForm(uiRoot, new RecordingPlayer()))
            {
                ShowOffscreen(form); TreeNode chapter = form.CourseTree.Nodes[0];
                chapter.Checked = true; Check(form.CheckedCount == 3, "Chapter selection must deduplicate identities");
                TreeNode unavailable = chapter.Nodes[4]; unavailable.Checked = true; Check(!unavailable.Checked, "Unavailable lesson can be checked");
                form.SearchBox.Text = "第二节"; Check(form.CheckedCount == 3, "Search lost hidden choices");
                form.CourseTree.Nodes[0].Checked = false; Check(form.CheckedCount == 2, "Filtered chapter must only affect visible lessons");
                form.SelectVisibleButton.PerformClick(); Check(form.CheckedCount == 3, "Select visible failed");
                form.ClearSelectionButton.PerformClick(); Check(form.CheckedCount == 0, "Clear must include hidden selections");
                form.SelectVisibleButton.PerformClick(); form.SearchBox.Clear();
                Check(form.CheckedCount == 1 && form.CourseTree.Nodes[0].Nodes[1].Checked, "Selection was not restored after clearing search");
                Check(ConversionEngine.FetchRequestCount == calls, "Checkbox or search performed a network request");
            }
        });
        foreach (bool play in new[] { true, false })
        {
            bool autoPlay = play;
            Run(autoPlay ? "batch converts once per identity in catalog order and opens one UTF8 playlist" : "batch convert-only creates all MP4s without player or playlist", delegate
            {
                string uiRoot = MakeBatchRoot(autoPlay ? "batch-play" : "batch-only", autoPlay); var launcher = new RecordingPlayer();
                var fixture = new Fixture(FixtureFolder, new byte[16]); var order = new List<string>();
                Func<Uri, CancellationToken, Task<FetchResult>> fetch = delegate(Uri uri, CancellationToken token)
                { if (uri.AbsolutePath.EndsWith("index.m3u8")) order.Add(uri.AbsolutePath); return fixture.Fetch(uri, token); };
                using (var form = new MainForm(uiRoot, launcher, (output, ignored) => new ConversionEngine(output, Ffmpeg) { FetchOverride = fetch }))
                {
                    ShowOffscreen(form);
                    TreeNode chapter = form.CourseTree.Nodes[0]; chapter.Nodes[2].Checked = true; chapter.Nodes[0].Checked = true; chapter.Nodes[1].Checked = true;
                    StartAndPump(form);
                    Check(string.Join(",", order) == "/a/index.m3u8,/b/index.m3u8,/c/index.m3u8", "Wrong queue order or duplicate conversion");
                    string output = AppConfig.Resolve(uiRoot, AppConfig.Load(uiRoot).OutputRoot);
                    Check(Directory.GetFiles(output, "*.mp4", SearchOption.AllDirectories).Length == 3, "Batch output count incorrect");
                    Check(!form.Busy && form.CheckedCount == 0 && form.JobProgress.Value == 100, "Completed queue did not clear selections");
                    Check(launcher.Calls == (autoPlay ? 1 : 0), "Batch auto-play invoked multiple times");
                    if (autoPlay)
                    {
                        string[] lines = File.ReadAllLines(launcher.LastPath, Encoding.UTF8);
                        Check(lines[0] == "#EXTM3U" && lines.Length == 4 && lines.Skip(1).All(File.Exists), "Invalid UTF8 playlist");
                        Check(Path.GetFileName(lines[1]) == "第一节.mp4" && Path.GetFileName(lines[3]) == "第三节.mp4", "Playlist order changed");
                    }
                    else Check(!Directory.Exists(Path.Combine(output, ".playlists")), "Convert-only created a playlist");
                    form.CourseTree.Nodes[0].Checked = true; order.Clear(); StartAndPump(form);
                    Check(order.Count == 0, "Existing batch MP4s were downloaded again");
                }
            });
        }
        Run("one failed lesson does not block later lessons and remains selected for retry", delegate
        {
            string uiRoot = MakeBatchRoot("batch-failure", true); var fixture = new Fixture(FixtureFolder, new byte[16]); var order = new List<string>(); var launcher = new RecordingPlayer();
            Func<Uri, CancellationToken, Task<FetchResult>> fetch = delegate(Uri uri, CancellationToken token)
            {
                if (uri.AbsolutePath.EndsWith("index.m3u8")) { order.Add(uri.AbsolutePath); if (uri.AbsolutePath.StartsWith("/b/")) throw new IOException("Synthetic network failure"); }
                return fixture.Fetch(uri, token);
            };
            using (var form = new MainForm(uiRoot, launcher, (output, ignored) => new ConversionEngine(output, Ffmpeg) { FetchOverride = fetch }))
            {
                ShowOffscreen(form); int notices = 0; form.NotificationOverride = delegate { notices++; };
                form.CourseTree.Nodes[0].Checked = true; StartAndPump(form);
                string output = AppConfig.Resolve(uiRoot, AppConfig.Load(uiRoot).OutputRoot);
                Check(order.Count == 3 && Directory.GetFiles(output, "*.mp4", SearchOption.AllDirectories).Length == 2, "Queue stopped at failed lesson");
                Check(form.CheckedCount == 1 && form.CourseTree.Nodes[0].Nodes[1].Checked && notices == 1, "Failed lesson not selected or multiple popups");
                Check(launcher.Calls == 1 && File.ReadAllLines(launcher.LastPath, Encoding.UTF8).Length == 3, "Partial-success playback includes failed lesson");
                order.Clear(); fetch = fixture.Fetch; StartAndPump(form);
                Check(form.CheckedCount == 0 && Directory.GetFiles(output, "*.mp4", SearchOption.AllDirectories).Length == 3, "Retry failed");
            }
        });
        Run("cancel stops current and queued lessons, prevents playback, and resumes cached fragments", delegate
        {
            string uiRoot = MakeBatchRoot("batch-cancel", true); var fixture = new Fixture(FixtureFolder, new byte[16]);
            var launcher = new RecordingPlayer(); int manifests = 0; bool initialPass = true;
            Func<Uri, CancellationToken, Task<FetchResult>> fetch = delegate(Uri uri, CancellationToken token)
            { if (uri.AbsolutePath.EndsWith("index.m3u8")) { Interlocked.Increment(ref manifests); if (initialPass && uri.AbsolutePath.StartsWith("/b/")) fixture.Slow = true; } return fixture.Fetch(uri, token); };
            using (var form = new MainForm(uiRoot, launcher, (output, ignored) => new ConversionEngine(output, Ffmpeg) { FetchOverride = fetch }))
            {
                ShowOffscreen(form); form.CourseTree.Nodes[0].Checked = true;
                string output = AppConfig.Resolve(uiRoot, AppConfig.Load(uiRoot).OutputRoot);
                using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
                {
                    timer.Tick += delegate
                    {
                        string cache = Path.Combine(output, ".cache");
                        if (Volatile.Read(ref manifests) == 2 && Directory.Exists(cache) && Directory.GetFiles(cache, "*.sha256", SearchOption.AllDirectories).Length > 0)
                        { Check(!form.StartButton.Enabled && !form.ClearSelectionButton.Enabled && !form.CourseTree.Enabled, "Queue controls active during conversion"); timer.Stop(); form.CancelButton.PerformClick(); }
                    };
                    timer.Start(); StartAndPump(form);
                }
                Check(manifests == 2 && launcher.Calls == 0 && form.CheckedCount == 2 && !form.CourseTree.Nodes[0].Nodes[0].Checked, "Cancel advanced queue, played, or reselected successful lesson");
                Check(Directory.GetFiles(output, "*.mp4", SearchOption.AllDirectories).Length == 1, "Cancelled queue lost completed video");
                Check(Directory.GetFiles(Path.Combine(output, ".cache"), "*.sha256", SearchOption.AllDirectories).Length > 0, "Cancel removed cache");
                initialPass = false; fixture.Slow = false; StartAndPump(form);
                Check(Directory.GetFiles(output, "*.mp4", SearchOption.AllDirectories).Length == 3 && form.CheckedCount == 0 && launcher.Calls == 1, "Cancelled queue failed to resume");
            }
        });
    }

    private static ConversionRequest Request(string name, byte[] key = null)
    { return new ConversionRequest { Url = "https://fixture.test/" + name + "/index.m3u8", Key = key, OutputPath = Path.Combine(Work, "outputs", name + ".mp4") }; }
    private static ConversionEngine Engine(Fixture fixture, string output = null)
    { return new ConversionEngine(output ?? Path.Combine(Work, "outputs"), Ffmpeg) { FetchOverride = fixture.Fetch }; }
    private static void Throws<T>(Action action, string message) where T : Exception
    { bool thrown = false; try { action(); } catch (T) { thrown = true; } Check(thrown, message); }
    private static void CoreTests()
    {
        Run("key formats and legacy identity compatibility", delegate
        {
            Check(Files.ParseKey("") == null, "Blank key must be optional");
            Check(Files.Hex(Files.ParseKey("000102030405060708090a0b0c0d0e0f")) == "000102030405060708090a0b0c0d0e0f", "Hex key failed");
            Check(Files.Hex(Files.ParseKey("AAECAwQFBgcICQoLDA0ODw==")) == "000102030405060708090a0b0c0d0e0f", "Base64 key failed");
            Throws<FormatException>(() => Files.ParseKey("bad"), "Invalid key accepted");
            byte[] key = new byte[16], prefix = Encoding.UTF8.GetBytes("https://fixture.test/index.m3u8\0");
            Check(Files.Identity("https://fixture.test/index.m3u8", key) == Files.Hash(prefix.Concat(key).ToArray()), "Legacy identity changed");
        });
        Run("resource ownership, access code formats and resource-only list", delegate
        {
            var list = Catalog.Parse("Section\n\t其他资料-> https://fixture.test/share?pwd=QUERY\n\tVideo\n\t\thls-> https://fixture.test/index.m3u8\n\t\t源码地址-> https://fixture.test/a\tABCD\n\t\t课前预习-> https://fixture.test/b 提取码：TEXT\n\t\t参考-> https://fixture.test/c?pwd=CODE\n\tOnly resources\n\t\t其他资料-> invalid\n");
            Check(list.Lessons.Count == 1 && list.ResourceCount == 5 && list.Roots[0].Resources[0].Code == "QUERY", "Resource parse count wrong");
            var child = list.Roots[0].Children[0]; Check(child.Resources.Select(x => x.Code).SequenceEqual(new[] { "ABCD", "TEXT", "CODE" }), "Access codes wrong");
            Check(child.Resources[2].Url.EndsWith("?pwd=CODE") && list.Roots[0].Children[1].Resources[0].Error != null, "Query changed or invalid resource lost");
            Check(Catalog.Parse("Only resources\n  other-> https://fixture.test/read").Lessons.Count == 0, "Resource-only list rejected");
        });
        Run("HLS IV, relative URL and unsupported formats", delegate
        {
            string manifest = "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:258\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.key\"\n#EXTINF:1,\n0.ts\n#EXTINF:1,\n1.ts\n#EXT-X-ENDLIST";
            var parts = ConversionEngine.ParseManifest(manifest, new Uri("https://fixture.test/a/index.m3u8"));
            Check(Files.Hex(parts[0].Iv).EndsWith("0102") && Files.Hex(parts[1].Iv).EndsWith("0103"), "Sequence IV wrong");
            Check(parts[0].KeyUrl.AbsoluteUri == "https://fixture.test/a/key.key" && parts[0].Url.AbsoluteUri == "https://fixture.test/a/0.ts", "Relative URL wrong");
            Check(Files.Hex(ConversionEngine.ParseManifest(manifest.Replace("key.key\"", "key.key\",IV=0x1"), new Uri("https://fixture.test/index.m3u8"))[1].Iv).EndsWith("0001"), "Explicit IV ignored");
            foreach (string invalid in new[] { manifest.Replace("AES-128", "SAMPLE-AES"), manifest.Replace("#EXT-X-ENDLIST", ""), manifest + "\n#EXT-X-MAP:URI=\"init.mp4\"", manifest + "\n#EXT-X-KEY:METHOD=AES-128,URI=\"different.key\"", manifest + "\n#EXT-X-MEDIA:TYPE=AUDIO,URI=\"audio.m3u8\"" })
                Throws<FormatException>(() => ConversionEngine.ParseManifest(invalid, new Uri("https://fixture.test/index.m3u8")), "Unsupported HLS accepted");
        });
        Run("highest bandwidth master playlist selection and redirects", delegate
        {
            var fixture = new Fixture(FixtureFolder, new byte[16], false); string selectedUrl = "";
            using (var engine = Engine(fixture))
            {
                engine.FetchOverride = delegate(Uri uri, CancellationToken token)
                {
                    if (uri.AbsolutePath.EndsWith("master.m3u8")) return Task.FromResult(new FetchResult(Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=10\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=100\nhigh.m3u8"), new Uri("https://fixture.test/redirect/master.m3u8")));
                    selectedUrl = uri.AbsoluteUri; return fixture.Fetch(uri, token);
                };
                Check(Wait(engine.ReadPlaylistAsync("https://fixture.test/master.m3u8", CancellationToken.None)).Count > 0 && selectedUrl == "https://fixture.test/redirect/high.m3u8", "Wrong variant or redirected base URL");
            }
        });
        foreach (string mode in new[] { "plain", "manual", "automatic", "sequence" })
        {
            string choice = mode;
            Run("full conversion and metadata privacy: " + choice, delegate
            {
                var fixture = new Fixture(FixtureFolder, new byte[16], choice != "plain", choice == "sequence");
                ConversionRequest request = Request(choice, choice == "manual" ? new byte[16] : null); var progress = new DirectProgress();
                using (var engine = Engine(fixture))
                {
                    ConversionResult result = Wait(engine.ConvertAsync(request, CancellationToken.None, progress));
                    Check(File.Exists(result.Path) && progress.Events.Any(x => x.Stage == "封装") && progress.Events.Any(x => x.Stage == "校验"), "Missing MP4 or progress stages");
                    string metadata = File.ReadAllText(result.Path + ".json"); Check(!metadata.Contains("fixture.test") && !metadata.Contains("AAAAAAAAAAAA"), "Receipt stores input URL or key");
                    Check(!Directory.Exists(Path.Combine(Work, "outputs", ".cache", Files.Identity(request.Url, choice == "plain" ? null : new byte[16]).Substring(0, 20))), "Successful cache retained");
                    long calls = ConversionEngine.FetchRequestCount;
                    // Known keys permit entirely offline reuse, including legacy receipt fields.
                    request.Key = choice == "plain" ? null : new byte[16];
                    Check(Wait(engine.ConvertAsync(request, CancellationToken.None, null)).Reused && ConversionEngine.FetchRequestCount == calls, "Existing video downloaded again");
                    if (choice == "manual")
                    {
                        string legacyOutput = Path.Combine(Work, "legacy"); Directory.CreateDirectory(legacyOutput);
                        string legacyPath = Path.Combine(legacyOutput, "prior-name.mp4"); File.Copy(result.Path, legacyPath); File.Copy(result.Path + ".json", legacyPath + ".json");
                        var lesson = new Lesson { Url = request.Url, Key = request.Key, Identity = Files.Identity(request.Url, request.Key), Title = "changed name", Groups = new[] { "new section" } };
                        using (var legacy = new ConversionEngine(legacyOutput, Ffmpeg) { FetchOverride = delegate { throw new Exception("Legacy reuse requested network"); } }) Check(Wait(legacy.ConvertAsync(lesson, CancellationToken.None, null)).Path == legacyPath, "Legacy identity reuse lost");
                    }
                }
            });
        }
        Run("wrong key, full-duration verification and overwrite protection preserve originals", delegate
        {
            var fixture = new Fixture(FixtureFolder, new byte[16]); var request = Request("wrong-key", Enumerable.Repeat((byte)9, 16).ToArray());
            using (var engine = Engine(fixture)) { Throws<InvalidDataException>(() => Wait(engine.ConvertAsync(request, CancellationToken.None, null)), "Wrong key accepted"); Check(!File.Exists(request.OutputPath), "Wrong key created final file"); }
            request = Request("bad-duration", new byte[16]); fixture.Manifest = fixture.Manifest.Replace("#EXTINF:1.000000", "#EXTINF:10.000000");
            using (var engine = Engine(fixture)) { Throws<InvalidDataException>(() => Wait(engine.ConvertAsync(request, CancellationToken.None, null)), "Bad duration accepted"); Check(!File.Exists(request.OutputPath), "Failed verification finalized MP4"); }
            request = Request("overwrite", new byte[16]); Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)); File.WriteAllText(request.OutputPath, "original");
            using (var engine = Engine(fixture))
            {
                long calls = ConversionEngine.FetchRequestCount; Throws<IOException>(() => Wait(engine.ConvertAsync(request, CancellationToken.None, null)), "Unconfirmed overwrite allowed"); Check(ConversionEngine.FetchRequestCount == calls, "Overwrite check requested network");
                request.Overwrite = true; Throws<InvalidDataException>(() => Wait(engine.ConvertAsync(request, CancellationToken.None, null)), "Invalid overwrite succeeded"); Check(File.ReadAllText(request.OutputPath) == "original", "Failed conversion changed original");
            }
        });
        Run("cancel, corrupt-cache refetch and resume", delegate
        {
            var fixture = new Fixture(FixtureFolder, new byte[16]) { Slow = true }; var request = Request("resume", new byte[16]);
            string cache = Path.Combine(Work, "outputs", ".cache", Files.Identity(request.Url, request.Key).Substring(0, 20));
            using (var engine = Engine(fixture))
            using (var cancellation = new CancellationTokenSource())
            {
                var progress = new DirectProgress { OnValue = value => { if (value.Stage == "下载") cancellation.Cancel(); } };
                Throws<OperationCanceledException>(() => Wait(engine.ConvertAsync(request, cancellation.Token, progress)), "Cancel ignored");
                string[] parts = Directory.GetFiles(cache, "*.ts"); Check(parts.Length > 0, "Cancellation removed verified segments");
                string bad = parts[0]; File.WriteAllText(Path.ChangeExtension(bad, ".sha256"), new string('0', 64)); fixture.Slow = false; var resumed = new DirectProgress();
                Check(File.Exists(Wait(engine.ConvertAsync(request, CancellationToken.None, resumed)).Path) && !Directory.Exists(cache), "Resume failed or cache not cleaned");
                Check(fixture.Requests.Values.Any(x => x > 1), "Corrupt cached segment reused");
            }
        });
        Run("real HTTP retry and cancellation; input redaction", delegate
        {
            using (var server = new HttpFixture())
            using (var engine = new ConversionEngine(Work, Ffmpeg))
            {
                Check(Encoding.UTF8.GetString(Wait(engine.FetchAsync(new Uri(server.BaseUri + "retry"), CancellationToken.None)).Data) == "ok" && server.RetryRequests == 3, "HTTP retries failed");
                using (var cancellation = new CancellationTokenSource(150)) Throws<OperationCanceledException>(() => Wait(engine.FetchAsync(new Uri(server.BaseUri + "slow"), cancellation.Token)), "HTTP cancellation failed");
            }
            string redacted = Files.Redact("Failed https://fixture.test/video?token=PRIVATE key=SECRET Cookie=PRIVATE"); Check(!redacted.Contains("PRIVATE") && !redacted.Contains("SECRET") && !redacted.Contains("fixture.test"), "Sensitive data not redacted");
        });
    }
    private sealed class RecordingResources : IResourceActions
    {
        public string Copied, Opened; public void Copy(string text) { Copied = text; } public void Open(string url) { Opened = url; }
    }
    private static void UiTests()
    {
        Run("demo default, offline browsing and resource copy actions", delegate
        {
            string demoRoot = Path.Combine(Work, "demo-root"); Directory.CreateDirectory(demoRoot); File.Copy(Path.Combine(Root, "demo-catalog.txt"), Path.Combine(demoRoot, "demo-catalog.txt"));
            long calls = ConversionEngine.FetchRequestCount;
            using (var form = new MainForm(demoRoot, new RecordingPlayer()))
            {
                ShowOffscreen(form); Check(form.Demo && form.AvailableCount == 3 && form.SingleVideoButton.Enabled, "Fresh demo failed");
                form.CourseTree.Nodes[0].Checked = true; StartAndPump(form); Check(!form.StartButton.Enabled, "Demo conversion enabled");
                form.SearchBox.Text = "01"; Check(form.CheckedCount > 0, "Search cleared choices");
                form.SearchBox.Clear(); form.CourseTree.SelectedNode = form.CourseTree.Nodes[0].Nodes[0]; Check(form.ResourceView.Items.Items.Count > 0 && !form.ResourceView.OpenLink.Enabled, "Demo resource opening enabled");
                Check(ConversionEngine.FetchRequestCount == calls, "Demo requested network");
            }
            var actions = new RecordingResources(); var list = Catalog.Parse("Section\n other-> https://fixture.test/a?pwd=DEMO\n Child\n  other-> https://fixture.test/b ABCD");
            using (var host = new Form()) using (var panel = new ResourcesPanel(actions))
            {
                host.Controls.Add(panel); host.Location = new Point(-20000, -20000); host.ShowInTaskbar = false; host.Show(); panel.LoadNode(list.Roots[0].Children[0]); Application.DoEvents();
                Check(panel.Items.Items.Count == 2, "Inherited resource missing"); panel.CopyCode.PerformClick(); Check(actions.Copied == "ABCD", "Copy code wrong"); panel.CopyShare.PerformClick(); Check(actions.Copied.Contains("fixture.test/b") && actions.Copied.Contains("ABCD"), "Copy share wrong"); panel.OpenLink.PerformClick(); Check(actions.Opened == "https://fixture.test/b", "Open used modified URL");
            }
        });
        foreach (string language in new[] { "zh", "en" })
        {
            string choice = language;
            Run("layout and language preference: " + choice, delegate
            {
                string uiRoot = MakeBatchRoot("language-" + choice, false); var config = AppConfig.Load(uiRoot); config.Language = choice; config.Save(uiRoot);
                using (var form = new MainForm(uiRoot, new RecordingPlayer()))
                {
                    ShowOffscreen(form); Check(form.SingleVideoButton.Text == (choice == "en" ? "Single video" : "单个视频转换"), "Language not applied");
                    Check(form.CourseTree.Nodes[0].Text.StartsWith("测试章节"), "Input titles translated");
                    foreach (int width in new[] { 980, 760 })
                    {
                        var tabs = (TabControl)form.ResourceView.Parent.Parent; tabs.SelectedTab = (TabPage)form.ResourceView.Parent;
                        form.Size = new Size(width, width == 980 ? 660 : 520); form.PerformLayout(); Application.DoEvents();
                        Check(form.SearchBox.Width > 25 && form.SingleVideoButton.Right <= form.SingleVideoButton.Parent.ClientSize.Width, "Toolbar clipping");
                        foreach (Button button in new[] { form.ResourceView.CopyUrl, form.ResourceView.CopyCode, form.ResourceView.CopyShare, form.ResourceView.OpenLink })
                            Check(button.ClientSize.Height >= TextRenderer.MeasureText(button.Text, button.Font).Height + 8, "Resource button text clipped: " + button.Text);
                        Check(form.ResourceView.CopyUrl.Bottom <= form.ResourceView.CopyShare.Top, "Resource button rows overlap");
                        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(Work, "layout-" + choice + "-" + width + ".png")); }
                    }
                    form.LanguageBox.SelectedIndex = choice == "en" ? 0 : 1;
                    Check(form.LanguageRestartRequested && AppConfig.Load(uiRoot).Language == (choice == "en" ? "zh" : "en"), "Language selection was not persisted");
                }
                L.English = choice == "en";
                using (var form = new SingleVideoForm(Root)) { form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false; form.Show(); Check(form.Start.Text == (choice == "en" ? "Start" : "开始转换"), "Single video not localized"); }
            });
        }
        L.English = false;
        Run("single-video UI conversion, validation and explicit overwrite rejection", delegate
        {
            var fixture = new Fixture(FixtureFolder, new byte[16]); string app = Path.Combine(Work, "single-app"); Directory.CreateDirectory(app);
            using (var form = new SingleVideoForm(app, (output, ffmpeg) => Engine(fixture, output)))
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false; form.Show();
                int notices = 0; form.NotificationOverride = text => notices++; form.Address.Text = "https://example.invalid/demo.m3u8"; form.Output.Text = Path.Combine(Work, "single", "示例 video.mp4");
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext()); Pump(form.StartAsync()); Check(notices == 1 && !File.Exists(form.Output.Text), "Demo accepted by single video");
                form.Address.Text = "https://fixture.test/index.m3u8"; form.Key.Text = "00000000000000000000000000000000";
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext()); Pump(form.StartAsync()); Check(File.Exists(form.Output.Text) && form.Progress.Value == 100 && !File.Exists(Path.Combine(app, "settings.json")), "Single-video UI failed or input was persisted");
                long calls = ConversionEngine.FetchRequestCount; form.OverwriteOverride = () => false;
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext()); Pump(form.StartAsync()); Check(ConversionEngine.FetchRequestCount == calls, "Rejected overwrite started download");
            }
        });
        L.English = false; BatchTests();
    }
    private static void Screenshots()
    {
        foreach (string language in new[] { "zh", "en" })
        {
            string app = Path.Combine(Work, "screenshot-" + language); Directory.CreateDirectory(app); File.Copy(Path.Combine(Root, "demo-catalog.txt"), Path.Combine(app, "demo-catalog.txt"));
            new AppConfig { Language = language }.Save(app);
            using (var form = new MainForm(app, new RecordingPlayer()))
            {
                ShowOffscreen(form); form.CourseTree.SelectedNode = form.CourseTree.Nodes[0].Nodes[0];
                var tabs = (TabControl)form.ResourceView.Parent.Parent; tabs.SelectedTab = (TabPage)form.ResourceView.Parent; Application.DoEvents();
                using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height)); image.Save(Path.Combine(Work, "demo-" + language + ".png")); }
                using (var single = new SingleVideoForm(app))
                {
                    single.StartPosition = FormStartPosition.Manual; single.Location = new Point(-20000, -20000); single.ShowInTaskbar = false; single.Show(); single.Address.Text = "https://example.invalid/video/sample.m3u8"; single.Output.Text = "video.mp4"; Application.DoEvents();
                    using (var image = new Bitmap(single.Width, single.Height)) { single.DrawToBitmap(image, new Rectangle(0, 0, single.Width, single.Height)); image.Save(Path.Combine(Work, "single-" + language + ".png")); }
                }
            }
        }
        Console.WriteLine("Screenshots: " + Work);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Files.Utf8; Root = Path.GetFullPath(args[0]);
        Work = Path.Combine(Root, ".build", "test-work", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(Work);
        Ffmpeg = Path.Combine(Root, "ffmpeg.exe"); CatalogPath = Path.Combine(Work, "generic-list.txt");
        File.WriteAllText(CatalogPath, "Section\n Video\n  key-> AAAAAAAAAAAAAAAAAAAAAA==\n  hls-> https://fixture.test/index.m3u8", Files.Utf8); Courses = Catalog.Parse(File.ReadAllText(CatalogPath));
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[1] == "--screenshots") { Screenshots(); return 0; }
            CreateFixture(); CoreTests(); UiTests();
            Files.SaveJson(Path.Combine(Work, "results.json"), new { passed = passed, failed = 0, network = "loopback and synthetic fixtures only" });
            Console.WriteLine("ALL PASSED: " + passed); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
