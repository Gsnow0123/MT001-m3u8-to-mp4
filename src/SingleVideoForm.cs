using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CourseStudy
{
    public sealed class SingleVideoForm : Form
    {
        private readonly string root;
        private readonly Func<string, string, ConversionEngine> factory;
        private CancellationTokenSource cancellation;
        private bool closingAfterCancel;
        private string completedPath;
        private readonly Label status = new Label();
        private readonly Button browse = new Button(), open = new Button(), folder = new Button();
        public readonly TextBox Address = new TextBox(), Key = new TextBox(), Output = new TextBox();
        public readonly Button Start = new Button(), CancelJob = new Button();
        public readonly ProgressBar Progress = new ProgressBar();
        public bool Busy { get { return cancellation != null; } }
        internal Action<string> NotificationOverride { get; set; }
        internal Func<bool> OverwriteOverride { get; set; }
        public SingleVideoForm(string appRoot, Func<string, string, ConversionEngine> engineFactory = null)
        {
            root = appRoot; factory = engineFactory ?? ((output, ffmpeg) => new ConversionEngine(output, ffmpeg));
            Text = L.T("单个视频转换"); Font = new Font("Microsoft YaHei UI", 9.5F); AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(690, 365); MinimumSize = new Size(590, 400);
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 7 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            for (int i = 0; i < 4; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 3 ? 48 : 40));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            string[] labels = { "m3u8 地址", "密钥（可选）", "保存 MP4" }; TextBox[] boxes = { Address, Key, Output };
            for (int i = 0; i < 3; i++) { table.Controls.Add(new Label { Text = L.T(labels[i]), AutoSize = true, Anchor = AnchorStyles.Left }, 0, i); boxes[i].Dock = DockStyle.Fill; boxes[i].Margin = new Padding(3, 7, 3, 6); table.Controls.Add(boxes[i], 1, i); }
            Key.UseSystemPasswordChar = true; table.SetColumnSpan(Address, 2); table.SetColumnSpan(Key, 2);
            browse.Text = L.T("选择文件"); browse.Dock = DockStyle.Fill; browse.Click += Browse; table.Controls.Add(browse, 2, 2);
            var hint = new Label { Text = L.T("密钥可填 Base64 或 32 位十六进制；留空时自动读取。仅支持 MPEG-TS 点播 HLS。"), Dock = DockStyle.Fill }; table.Controls.Add(hint, 0, 3); table.SetColumnSpan(hint, 3);
            Progress.Dock = DockStyle.Fill; table.Controls.Add(Progress, 0, 4); table.SetColumnSpan(Progress, 3);
            status.Text = L.T("输入地址和保存位置后点击开始；不会自动播放。"); status.Dock = DockStyle.Fill; status.AutoEllipsis = true; table.Controls.Add(status, 0, 5); table.SetColumnSpan(status, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            foreach (var pair in new[] { Tuple.Create(Start, "开始转换"), Tuple.Create(CancelJob, "取消转换"), Tuple.Create(open, "打开视频"), Tuple.Create(folder, "打开目录") })
            { pair.Item1.Text = L.T(pair.Item2); pair.Item1.AutoSize = true; pair.Item1.Height = 32; buttons.Controls.Add(pair.Item1); }
            table.Controls.Add(buttons, 0, 6); table.SetColumnSpan(buttons, 3); Controls.Add(table);
            CancelJob.Enabled = open.Enabled = folder.Enabled = false;
            Start.Click += async delegate { await StartAsync(); }; CancelJob.Click += delegate { if (cancellation != null) { CancelJob.Enabled = false; cancellation.Cancel(); status.Text = L.T("正在取消…"); } };
            open.Click += delegate { OpenPath(completedPath); }; folder.Click += delegate { OpenPath(Path.GetDirectoryName(completedPath)); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!Busy) return; e.Cancel = true;
                if (!closingAfterCancel && MessageBox.Show(this, L.T("当前视频还在转换。取消并关闭吗？缓存会保留。"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { closingAfterCancel = true; cancellation.Cancel(); }
            };
            FormClosed += delegate { Key.Clear(); Address.Clear(); };
        }
        private void Browse(object sender, EventArgs e)
        { using (var dialog = new SaveFileDialog { Filter = "MP4 (*.mp4)|*.mp4", FileName = "video.mp4", OverwritePrompt = false }) if (dialog.ShowDialog(this) == DialogResult.OK) Output.Text = dialog.FileName; }
        private void OpenPath(string path)
        { try { if (!string.IsNullOrEmpty(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Notice(Files.Redact(ex.Message)); } }
        private void Notice(string text)
        { if (NotificationOverride != null) NotificationOverride(L.T(text)); else MessageBox.Show(this, L.T(text), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        internal async Task StartAsync()
        {
            if (Busy) return; ConversionRequest request;
            try
            {
                Catalog.ValidateUrl(Address.Text.Trim());
                if (Files.Placeholder(new Uri(Address.Text.Trim()))) throw new FormatException("虚拟地址仅用于展示。");
                if (string.IsNullOrWhiteSpace(Output.Text) || !Path.IsPathRooted(Output.Text.Trim())) throw new FormatException("请选择完整的 MP4 保存路径。");
                request = new ConversionRequest { Url = Address.Text.Trim(), Key = Files.ParseKey(Key.Text), OutputPath = Path.GetFullPath(Output.Text.Trim()) };
                if (!request.OutputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) throw new FormatException("输出文件必须使用 .mp4 扩展名。");
                if (File.Exists(request.OutputPath))
                {
                    bool allow = OverwriteOverride != null ? OverwriteOverride() : MessageBox.Show(this, L.T("输出文件已存在，是否允许覆盖？校验通过前会保留原文件。"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                    if (!allow) return; request.Overwrite = true;
                }
            }
            catch (Exception ex) { Notice(Files.Redact(ex.Message)); return; }
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            Start.Enabled = Address.Enabled = Key.Enabled = Output.Enabled = browse.Enabled = open.Enabled = folder.Enabled = false; CancelJob.Enabled = true; Progress.Value = 0;
            string displayedStage = ""; bool receiving = true;
            var progress = new Progress<ConversionProgress>(value =>
            {
                if (IsDisposed || !receiving || token.IsCancellationRequested) return;
                if (displayedStage == value.Stage && value.Percent < Progress.Value) return;
                displayedStage = value.Stage; Progress.Value = value.Percent; status.Text = L.T(value.Stage + " · " + value.Message);
            });
            try
            {
                using (var engine = factory(Path.GetDirectoryName(request.OutputPath), Path.Combine(root, "ffmpeg.exe")))
                {
                    ConversionResult result = await Task.Run(() => engine.ConvertAsync(request, token, progress));
                    completedPath = result.Path; Progress.Value = 100; status.Text = L.T(result.Warning ?? "已完成 · 全片校验通过"); open.Enabled = folder.Enabled = true;
                }
            }
            catch (OperationCanceledException) { status.Text = L.T("已取消 · 缓存保留，再次开始可续传"); }
            catch (Exception ex) { status.Text = L.T("转换失败，可重试"); Notice(Files.Redact(ex.Message)); }
            finally
            {
                receiving = false; cancellation.Dispose(); cancellation = null;
                Start.Enabled = Address.Enabled = Key.Enabled = Output.Enabled = browse.Enabled = true; CancelJob.Enabled = false;
                if (closingAfterCancel) Close();
            }
        }
    }
}
