using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CourseStudy
{
    public sealed class MainForm : Form
    {
        private readonly string root;
        private readonly IPlayerLauncher player;
        private readonly Func<string, string, ConversionEngine> engineFactory;
        private AppConfig config;
        private Catalog catalog;
        private readonly VideoIndex index = new VideoIndex();
        private CancellationTokenSource job;
        private bool busy, closingAfterCancel, loading, updatingChecks;
        private readonly HashSet<string> checkedLessons = new HashSet<string>(StringComparer.Ordinal);
        private int visibleCount;
        private Lesson selected;
        private readonly Label summary = new Label(), courseTitle = new Label(), state = new Label(), stage = new Label();
        private readonly Label selectionSummary = new Label();
        private readonly TextBox details = new TextBox();
        private readonly Button importButton = new Button(), folderButton = new Button(), playerButton = new Button(), cancelButton = new Button();
        public readonly Button SingleVideoButton = new Button();
        public readonly ComboBox LanguageBox = new ComboBox();
        public readonly ResourcesPanel ResourceView;
        public bool LanguageRestartRequested { get; private set; }
        public bool Demo { get { return catalog != null && catalog.IsDemo; } }
        public TreeView CourseTree { get; private set; }
        public TextBox SearchBox { get; private set; }
        public CheckBox AutoPlayBox { get; private set; }
        public Button StartButton { get; private set; }
        public ProgressBar JobProgress { get; private set; }
        public Button SelectVisibleButton { get; private set; }
        public Button ClearSelectionButton { get; private set; }
        public new Button CancelButton { get { return cancelButton; } }
        public int CheckedCount { get { return checkedLessons.Count; } }
        internal Action<string, string, MessageBoxIcon> NotificationOverride { get; set; }
        public int AvailableCount { get { return catalog == null ? 0 : catalog.Lessons.Count; } }
        public string SelectedIdentity { get { return selected == null ? "" : selected.Identity; } }
        public bool Busy { get { return busy; } }

        public MainForm(string appRoot, IPlayerLauncher launcher, Func<string, string, ConversionEngine> factory = null)
        {
            root = Path.GetFullPath(appRoot); player = launcher; engineFactory = factory ?? ((output, ffmpeg) => new ConversionEngine(output, ffmpeg)); config = AppConfig.Load(root);
            L.English = config.Language == "en"; ResourceView = new ResourcesPanel();
            Text = "MT001 视频转换工具"; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9.5F);
            ClientSize = new Size(980, 660); MinimumSize = new Size(760, 520);
            BackColor = Color.FromArgb(247, 248, 250);
            BuildControls();
            loading = true; AutoPlayBox.Checked = config.AutoPlay; loading = false;
            L.Apply(this); LoadCatalog(AppConfig.Resolve(root, config.CatalogPath));
            FormClosing += OnClosing;
        }

        private void BuildControls()
        {
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            Controls.Add(outer);
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = Padding.Empty };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Controls.Add(new Label { Text = "搜索课程", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
            SearchBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 8, 6), AccessibleName = "搜索课程" };
            SearchBox.TextChanged += delegate { if (!busy) BuildTree(); }; toolbar.Controls.Add(SearchBox, 1, 0);
            SetupSmallButton(importButton, "更换清单"); SetupSmallButton(folderButton, "视频目录"); SetupSmallButton(playerButton, "播放器");
            toolbar.Controls.Add(importButton, 2, 0); toolbar.Controls.Add(folderButton, 3, 0); toolbar.Controls.Add(playerButton, 4, 0);
            SetupSmallButton(SingleVideoButton, "单个视频转换"); toolbar.Controls.Add(SingleVideoButton, 5, 0);
            LanguageBox.DropDownStyle = ComboBoxStyle.DropDownList; LanguageBox.Width = 96; LanguageBox.Anchor = AnchorStyles.Left; LanguageBox.Items.AddRange(new object[] { "中文", "English" }); LanguageBox.SelectedIndex = L.English ? 1 : 0; toolbar.Controls.Add(LanguageBox, 6, 0);
            LanguageBox.SelectedIndexChanged += delegate { if (!busy) { config.Language = LanguageBox.SelectedIndex == 1 ? "en" : "zh"; SaveConfig(); LanguageRestartRequested = true; Close(); } };
            SingleVideoButton.Click += delegate { if (!busy) using (var dialog = new SingleVideoForm(root, engineFactory)) { dialog.ShowDialog(this); index.Scan(AppConfig.Resolve(root, config.OutputRoot)); BuildTree(); } };
            importButton.Click += ImportCatalog; folderButton.Click += OpenFolder; playerButton.Click += delegate { SelectPlayer(); };
            outer.Controls.Add(toolbar, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = new Padding(0, 0, 14, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            summary.AutoSize = true; summary.Margin = new Padding(0, 2, 0, 8); summary.ForeColor = Color.DimGray; left.Controls.Add(summary, 0, 0);
            var selectionTools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            SelectVisibleButton = new Button(); ClearSelectionButton = new Button();
            SetupSmallButton(SelectVisibleButton, "全选当前结果"); SetupSmallButton(ClearSelectionButton, "清空勾选");
            SelectVisibleButton.Click += delegate { ChangeChecked(true); }; ClearSelectionButton.Click += delegate { ChangeChecked(false); };
            selectionTools.Controls.Add(SelectVisibleButton); selectionTools.Controls.Add(ClearSelectionButton); left.Controls.Add(selectionTools, 0, 1);
            CourseTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, CheckBoxes = true, BorderStyle = BorderStyle.FixedSingle, FullRowSelect = true, ItemHeight = 27, AccessibleName = "课程列表（可勾选多节或章节）" };
            CourseTree.AfterSelect += OnSelectionChanged; CourseTree.BeforeCheck += BeforeCourseCheck; CourseTree.AfterCheck += AfterCourseCheck;
            left.Controls.Add(CourseTree, 0, 2);
            selectionSummary.Dock = DockStyle.Fill; selectionSummary.AutoEllipsis = true; selectionSummary.ForeColor = Color.DimGray; selectionSummary.Margin = new Padding(0, 6, 0, 0); left.Controls.Add(selectionSummary, 0, 3); body.Controls.Add(left, 0, 0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = new Padding(0, 28, 0, 0), Padding = new Padding(8), BackColor = Color.White };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            courseTitle.Text = "请选择一节课程"; courseTitle.AutoSize = false; courseTitle.AutoEllipsis = true; courseTitle.Dock = DockStyle.Fill; courseTitle.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold); courseTitle.Margin = new Padding(0, 0, 0, 6); right.Controls.Add(courseTitle, 0, 0);
            details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; details.BorderStyle = BorderStyle.None; details.BackColor = Color.White; details.ForeColor = Color.DimGray; details.Dock = DockStyle.Fill; details.TabStop = false;
            var tabs = new TabControl { Dock = DockStyle.Fill }; var detailTab = new TabPage("详情"); var resourcesTab = new TabPage("资料资源"); detailTab.Controls.Add(details); resourcesTab.Controls.Add(ResourceView); tabs.TabPages.Add(detailTab); tabs.TabPages.Add(resourcesTab); right.Controls.Add(tabs, 0, 1);
            state.Text = "选中课程后，点击按钮才开始下载。"; state.Dock = DockStyle.Fill; state.AutoEllipsis = true; state.Margin = new Padding(0, 5, 0, 3); right.Controls.Add(state, 0, 2);
            AutoPlayBox = new CheckBox { Text = "完成后用 PotPlayer 打开", AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            AutoPlayBox.CheckedChanged += delegate { if (!loading) { config.AutoPlay = AutoPlayBox.Checked; SaveConfig(); UpdateAction(); } }; right.Controls.Add(AutoPlayBox, 0, 3);
            StartButton = new Button { Text = "转换并播放", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(32, 105, 216), ForeColor = Color.White, Margin = new Padding(0, 6, 0, 6), Enabled = false };
            StartButton.FlatAppearance.BorderSize = 0; StartButton.Click += async delegate { await StartSelectedAsync(); }; right.Controls.Add(StartButton, 0, 4);
            cancelButton.Text = "取消全部任务"; cancelButton.Dock = DockStyle.Fill; cancelButton.FlatStyle = FlatStyle.Flat; cancelButton.Enabled = false; cancelButton.Margin = new Padding(0, 2, 0, 4);
            cancelButton.Click += delegate { if (job != null) { cancelButton.Enabled = false; stage.Text = L.T("正在取消整批任务… 分片缓存会保留供续传。"); job.Cancel(); } }; right.Controls.Add(cancelButton, 0, 5);
            body.Controls.Add(right, 1, 0); outer.Controls.Add(body, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 12, 0, 0) };
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stage.Text = "就绪 · 请选择课程"; stage.Dock = DockStyle.Fill; stage.AutoEllipsis = true; footer.Controls.Add(stage, 0, 0);
            JobProgress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, AccessibleName = "当前阶段进度" }; footer.Controls.Add(JobProgress, 0, 1);
            footer.Controls.Add(new Label { Text = "勾选课程或章节后批量转换；搜索保留勾选，整批结束后可自动播放。", Dock = DockStyle.Fill, ForeColor = Color.Gray, Margin = new Padding(0, 7, 0, 0) }, 0, 2);
            outer.Controls.Add(footer, 0, 2);
        }
        private static void SetupSmallButton(Button button, string text)
        { button.Text = text; button.AutoSize = true; button.Anchor = AnchorStyles.Left; button.FlatStyle = FlatStyle.Flat; button.Margin = new Padding(4, 3, 0, 5); button.Padding = new Padding(4, 2, 4, 2); }
        private void SaveConfig()
        {
            try { config.Save(root); }
            catch (Exception ex) { stage.Text = L.T("设置暂时无法保存：" + Files.Redact(ex.Message)); }
        }
        private void LoadCatalog(string path)
        {
            try
            {
                catalog = Catalog.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));
                checkedLessons.Clear();
                index.Scan(AppConfig.Resolve(root, config.OutputRoot)); BuildTree();
                stage.Text = L.T(catalog.IsDemo ? "虚拟数据，仅用于展示" : "就绪 · 仅在点击转换后联网");
            }
            catch (Exception ex) { catalog = null; CourseTree.Nodes.Clear(); selected = null; checkedLessons.Clear(); summary.Text = L.T("暂无清单，可导入清单或转换单个视频。"); details.Text = L.T(Files.Redact(ex.Message)); state.Text = L.T("请点击“更换清单”选择课程文件。"); UpdateAction(); }
        }
        private TreeNode BuildNode(CatalogNode source, string query, string selectedId, ref TreeNode selection, ref int count)
        {
            var children = new List<TreeNode>();
            foreach (CatalogNode child in source.Children) { TreeNode node = BuildNode(child, query, selectedId, ref selection, ref count); if (node != null) children.Add(node); }
            bool match = query.Length == 0 || source.DisplayPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!match && children.Count == 0) return null;
            string text = source.Title; bool available = source.Lesson != null;
            if (available) { count++; if (index.Quick(source.Lesson.Identity) != null) text += "  ✓"; }
            else if (source.Children.Count == 0) text += L.T("（暂无视频）");
            if (source.Resources.Count > 0) text += "  [" + source.Resources.Count + "]";
            var result = new TreeNode(text) { Tag = source };
            if (!available && source.Children.Count == 0) result.ForeColor = Color.Gray;
            else if (available && index.Quick(source.Lesson.Identity) != null) result.ForeColor = Color.FromArgb(32, 120, 75);
            result.Nodes.AddRange(children.ToArray());
            if (available && source.Lesson.Identity == selectedId) selection = result;
            return result;
        }
        private void BuildTree()
        {
            if (catalog == null) return;
            string wanted = selected != null ? selected.Identity : config.LastLessonIdentity;
            if (string.IsNullOrEmpty(wanted) && catalog.Lessons.Count > 0) wanted = catalog.Lessons[0].Identity;
            TreeNode selection = null; int count = 0; string query = SearchBox.Text.Trim();
            updatingChecks = true; CourseTree.BeginUpdate(); CourseTree.Nodes.Clear();
            try
            {
                string filename = Path.GetFileNameWithoutExtension(AppConfig.Resolve(root, config.CatalogPath));
                foreach (CatalogNode source in catalog.Roots)
                {
                    if (source.Title == filename && source.Children.Count == 0 && source.Lesson == null) continue;
                    TreeNode node = BuildNode(source, query, wanted, ref selection, ref count); if (node != null) CourseTree.Nodes.Add(node);
                }
                if (query.Length > 0) CourseTree.ExpandAll();
                if (selection != null) { CourseTree.SelectedNode = selection; selection.EnsureVisible(); }
                else { selected = null; courseTitle.Text = L.T("请选择一节课程"); details.Clear(); ResourceView.LoadNode(null); state.Text = L.T("选中课程后，点击按钮才开始下载。"); if (CourseTree.Nodes.Count > 0 && catalog.Lessons.Count == 0) CourseTree.SelectedNode = CourseTree.Nodes[0]; }
                int existing = catalog.Lessons.Select(x => x.Identity).Distinct().Count(x => index.Quick(x) != null);
                visibleCount = count;
                summary.Text = L.T("显示 " + count.ToString("N0") + " / " + catalog.Lessons.Count.ToString("N0") + " 节课 · 本地已完成 " + existing + " 节") + " · " + catalog.ResourceCount + " " + L.T("资源");
                if (catalog.IsDemo) { CourseTree.ExpandAll(); summary.Text = L.T("虚拟数据，仅用于展示"); }
                RefreshChecks(CourseTree.Nodes);
                UpdateAction();
            }
            finally { CourseTree.EndUpdate(); updatingChecks = false; }
        }
        private static IEnumerable<Lesson> NodeLessons(TreeNode node)
        {
            var source = node.Tag as CatalogNode;
            if (source != null && source.Lesson != null) yield return source.Lesson;
            foreach (TreeNode child in node.Nodes) foreach (Lesson lesson in NodeLessons(child)) yield return lesson;
        }
        private void BeforeCourseCheck(object sender, TreeViewCancelEventArgs e)
        { if (!updatingChecks && (busy || !NodeLessons(e.Node).Any())) e.Cancel = true; }
        private void AfterCourseCheck(object sender, TreeViewEventArgs e)
        {
            if (updatingChecks || busy) return;
            foreach (Lesson lesson in NodeLessons(e.Node)) { if (e.Node.Checked) checkedLessons.Add(lesson.Identity); else checkedLessons.Remove(lesson.Identity); }
            SyncChecks();
        }
        private void RefreshChecks(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                Lesson[] lessons = NodeLessons(node).ToArray();
                node.Checked = lessons.Length > 0 && lessons.All(x => checkedLessons.Contains(x.Identity));
                RefreshChecks(node.Nodes);
            }
        }
        private void SyncChecks()
        {
            updatingChecks = true;
            try { RefreshChecks(CourseTree.Nodes); }
            finally { updatingChecks = false; }
            UpdateAction();
        }
        private void ChangeChecked(bool selectVisible)
        {
            if (busy) return;
            if (selectVisible) foreach (TreeNode node in CourseTree.Nodes) foreach (Lesson lesson in NodeLessons(node)) checkedLessons.Add(lesson.Identity);
            else checkedLessons.Clear();
            SyncChecks();
        }
        private Lesson[] QueueLessons()
        {
            if (catalog == null) return new Lesson[0];
            IEnumerable<Lesson> lessons = checkedLessons.Count > 0 ? catalog.Lessons.Where(x => checkedLessons.Contains(x.Identity)) : (selected == null ? Enumerable.Empty<Lesson>() : new[] { selected });
            return lessons.GroupBy(x => x.Identity).Select(x => x.First()).ToArray();
        }
        private void OnSelectionChanged(object sender, TreeViewEventArgs e)
        {
            if (busy) return; CatalogNode node = e.Node.Tag as CatalogNode;
            selected = node == null ? null : node.Lesson; courseTitle.Text = node == null ? "请选择一节课程" : node.Title;
            details.Text = node == null ? "" : node.DisplayPath;
            ResourceView.LoadNode(node);
            if (selected == null) state.Text = node != null && node.Error != null ? node.Error : "请选择带视频地址的课程。";
            else
            {
                ExistingVideo video = index.Quick(selected.Identity);
                state.Text = video == null ? "尚未转换 · 点击后才开始下载" : "已有 MP4 · 将校验后直接使用";
                if (video != null) details.Text += "\r\n\r\n" + video.Path;
            }
            JobProgress.Value = 0; UpdateAction();
        }
        private void UpdateAction()
        {
            if (StartButton == null) return;
            bool exists = selected != null && index.Quick(selected.Identity) != null;
            if (checkedLessons.Count > 0)
            {
                StartButton.Text = (AutoPlayBox.Checked ? "转换并播放 " : "仅转换 ") + checkedLessons.Count + " 节";
                if (!busy) state.Text = "已勾选 " + checkedLessons.Count + " 节 · 按清单顺序转换";
            }
            else
            {
                StartButton.Text = exists ? (AutoPlayBox.Checked ? "播放已有视频" : "已完成") : (AutoPlayBox.Checked ? "转换并播放" : "仅转换");
                if (!busy && (state.Text.StartsWith("已勾选 ") || state.Text.StartsWith(L.T("已勾选 ")))) state.Text = selected == null ? "请选择带视频地址的课程。" : (exists ? "已有 MP4 · 将校验后直接使用" : "尚未转换 · 点击后才开始下载");
            }
            StartButton.Enabled = !busy && !Demo && (checkedLessons.Count > 0 || (selected != null && (!exists || AutoPlayBox.Checked)));
            int visibleChecked = CourseTree.Nodes.Cast<TreeNode>().SelectMany(NodeLessons).Select(x => x.Identity).Distinct().Count(checkedLessons.Contains);
            selectionSummary.Text = "已勾选 " + checkedLessons.Count + " 节" + (checkedLessons.Count > visibleChecked ? "（筛选外 " + (checkedLessons.Count - visibleChecked) + " 节）" : " · 未勾选时处理高亮课程");
            SelectVisibleButton.Enabled = !busy && visibleCount > 0; ClearSelectionButton.Enabled = !busy && checkedLessons.Count > 0;
            cancelButton.Enabled = busy && job != null && !job.IsCancellationRequested;
            if (Demo) state.Text = "虚拟数据，仅用于展示";
            StartButton.Text = L.T(StartButton.Text); state.Text = L.T(state.Text); selectionSummary.Text = L.T(selectionSummary.Text);
        }
        private void SetBusy(bool value)
        {
            busy = value; CourseTree.Enabled = !value; SearchBox.Enabled = !value; importButton.Enabled = !value; playerButton.Enabled = !value; AutoPlayBox.Enabled = !value;
            SingleVideoButton.Enabled = LanguageBox.Enabled = !value;
            UpdateAction();
        }
        internal async Task StartSelectedAsync()
        {
            if (busy || Demo) return;
            Lesson[] queue = QueueLessons(); if (queue.Length == 0) return;
            bool autoPlay = AutoPlayBox.Checked;
            if (autoPlay && !File.Exists(config.PotPlayerPath) && !SelectPlayer()) return;
            job = new CancellationTokenSource(); SetBusy(true); state.Text = "正在处理当前课程…"; JobProgress.Value = 0;
            CancellationToken token = job.Token;
            var completed = new List<string>(); var failures = new List<string>(); var successful = new HashSet<string>();
            int currentItem = 0, completedItems = 0; bool acceptingProgress = false, cancelled = false; string warning = null;
            try
            {
                using (var engine = engineFactory(AppConfig.Resolve(root, config.OutputRoot), Path.Combine(root, "ffmpeg.exe")))
                for (int i = 0; i < queue.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    Lesson lesson = queue[i]; currentItem = i + 1;
                    config.LastLessonIdentity = lesson.Identity; SaveConfig();
                    courseTitle.Text = lesson.Title; details.Text = lesson.DisplayPath; state.Text = L.T("第 " + currentItem + " / " + queue.Length + " 节 · 已完成 " + completedItems + " 节"); JobProgress.Value = 0;
                    string displayedStage = ""; int item = currentItem; acceptingProgress = true;
                    var progress = new Progress<ConversionProgress>(delegate(ConversionProgress value)
                    {
                        if (IsDisposed || !busy || !acceptingProgress || token.IsCancellationRequested || currentItem != item) return;
                        if (displayedStage == value.Stage && value.Percent < JobProgress.Value) return;
                        displayedStage = value.Stage; stage.Text = "[" + item + "/" + queue.Length + "] " + lesson.Title + " · " + L.T(value.Stage + " · " + value.Message); JobProgress.Value = value.Percent;
                    });
                    try
                    {
                        ConversionResult result = await Task.Run(() => engine.ConvertAsync(lesson, token, progress));
                        completed.Add(result.Path); completedItems++; successful.Add(lesson.Identity); checkedLessons.Remove(lesson.Identity);
                        details.Text = lesson.DisplayPath + "\r\n\r\n" + result.Path;
                        if (result.Warning != null) warning = result.Warning;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        string log = SaveError(lesson, ex);
                        failures.Add(lesson.DisplayPath + "\n" + ex.Message + (log == null ? "" : "\n错误记录：" + log));
                        checkedLessons.Add(lesson.Identity);
                    }
                    finally { acceptingProgress = false; }
                }
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { failures.Add(ex.Message); }
            finally
            {
                acceptingProgress = false;
                if (cancelled) foreach (Lesson lesson in queue.Where(x => !successful.Contains(x.Identity))) checkedLessons.Add(lesson.Identity);
                job.Dispose(); job = null;
                // Ignore progress callbacks posted by a finished lesson before rebuilding the view.
                index.Scan(AppConfig.Resolve(root, config.OutputRoot)); BuildTree();
                SetBusy(false);
                if (cancelled)
                {
                    state.Text = "已取消 · 完成 " + completedItems + " 节";
                    stage.Text = "已取消整批任务 · 未完成课程保持勾选，缓存可续传";
                }
                else
                {
                    JobProgress.Value = failures.Count == 0 ? 100 : 0;
                    state.Text = "完成 " + completedItems + " / " + queue.Length + " 节" + (failures.Count == 0 ? " · 全部校验通过" : " · 失败 " + failures.Count + " 节");
                    stage.Text = warning ?? (failures.Count == 0 ? "已完成 · " + completedItems + " 节" : "部分失败 · 失败课程保持勾选，可再次重试");
                    if (completed.Count > 0) details.Text = string.Join("\r\n\r\n", completed);
                    if (autoPlay && completed.Count > 0 && !closingAfterCancel)
                    {
                        try
                        {
                            string target = completed.Count == 1 ? completed[0] : WritePlaylist(completed);
                            player.Open(config.PotPlayerPath, target);
                            stage.Text = "完成 " + completedItems + " 节" + (failures.Count > 0 ? "，失败 " + failures.Count + " 节" : "") + " · 已交给 PotPlayer 打开";
                        }
                        catch (Exception ex) { stage.Text = "视频已完成，但播放器未打开。"; Notify(ex.Message + "\n\n视频已保留在：\n" + AppConfig.Resolve(root, config.OutputRoot), "PotPlayer 启动失败", MessageBoxIcon.Information); }
                    }
                }
                if (failures.Count > 0 && !closingAfterCancel)
                    Notify(string.Join("\n\n", failures.Take(5)) + (failures.Count > 5 ? "\n\n其余失败原因见 .logs。" : "") + "\n\n失败课程及缓存已保留，可再次点击重试。", "有课程未完成", MessageBoxIcon.Warning);
                state.Text = L.T(state.Text); stage.Text = L.T(stage.Text);
                if (closingAfterCancel) { closingAfterCancel = false; Close(); }
            }
        }
        private string WritePlaylist(IEnumerable<string> videos)
        {
            string directory = Path.Combine(AppConfig.Resolve(root, config.OutputRoot), ".playlists"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "最近批次.m3u8"), temporary = path + ".part";
            File.WriteAllLines(temporary, new[] { "#EXTM3U" }.Concat(videos), Files.Utf8); Files.Replace(temporary, path); return path;
        }
        private void Notify(string message, string title, MessageBoxIcon icon)
        { message = L.T(Files.Redact(message)); title = L.T(title); if (NotificationOverride != null) NotificationOverride(message, title, icon); else MessageBox.Show(this, message, title, MessageBoxButtons.OK, icon); }
        private string SaveError(Lesson lesson, Exception ex)
        {
            try
            {
                string directory = Path.Combine(root, ".logs"); Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                File.WriteAllText(path, DateTime.UtcNow.ToString("O") + "\r\n" + Files.Redact(ex.Message), Files.Utf8);
                foreach (string old in Directory.GetFiles(directory, "*.log").OrderByDescending(x => x).Skip(5)) File.Delete(old);
                return path;
            }
            catch { return null; }
        }
        private bool SelectPlayer()
        {
            using (var dialog = new OpenFileDialog { Title = L.T("选择 PotPlayer 程序"), Filter = L.T("播放器程序 (*.exe)|*.exe"), FileName = "PotPlayerMini64.exe" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                config.PotPlayerPath = dialog.FileName; SaveConfig(); stage.Text = L.T("已选择播放器：") + Path.GetFileName(dialog.FileName); return true;
            }
        }
        private void ImportCatalog(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Title = L.T("选择课程清单"), Filter = L.T("课程清单 (*.java;*.txt)|*.java;*.txt|所有文件 (*.*)|*.*") })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { Catalog.Parse(File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8)); }
                catch (Exception ex) { Notify(ex.Message, "清单无法读取", MessageBoxIcon.Warning); return; }
                config.CatalogPath = dialog.FileName; config.LastLessonIdentity = ""; selected = null; SearchBox.Clear(); SaveConfig(); LoadCatalog(dialog.FileName);
            }
        }
        private void OpenFolder(object sender, EventArgs e)
        {
            try { string path = AppConfig.Resolve(root, config.OutputRoot); Directory.CreateDirectory(path); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
            catch (Exception ex) { Notify(ex.Message, "无法打开视频目录", MessageBoxIcon.Information); }
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!busy) return;
            e.Cancel = true;
            if (closingAfterCancel) return;
            if (MessageBox.Show(this, L.T("课程队列还在转换。要取消全部任务并退出吗？\n已完成的视频和分片缓存会保留，下次可继续。"), L.T("退出课程学习助手"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            closingAfterCancel = true; cancelButton.Enabled = false; if (job != null) job.Cancel();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string mutexName = "Local\\CourseStudy-" + Files.Hash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToLowerInvariant())).Substring(0, 20);
            bool created;
            using (var mutex = new Mutex(true, mutexName, out created))
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (!created) { MessageBox.Show(L.T("课程学习助手已经在运行。"), "MT001", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                try
                {
                    bool restart;
                    do { using (var form = new MainForm(root, new PotPlayerLauncher())) { Application.Run(form); restart = form.LanguageRestartRequested; } } while (restart);
                }
                catch (Exception ex) { MessageBox.Show(L.T(Files.Redact(ex.Message)), "MT001", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
