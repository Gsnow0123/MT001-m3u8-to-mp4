using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CourseStudy
{
    public interface IResourceActions { void Copy(string text); void Open(string url); }
    public sealed class ResourceActions : IResourceActions
    {
        public void Copy(string text) { Clipboard.SetText(text); }
        public void Open(string url) { Catalog.ValidateUrl(url); if (Files.Placeholder(new Uri(url))) throw new InvalidOperationException(L.T("虚拟地址仅用于展示。")); Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    }
    public sealed class ResourcesPanel : UserControl
    {
        private sealed class Row
        {
            public ResourceLink Link; public string Owner;
            public override string ToString() { return Link + " · " + Owner; }
        }
        private readonly IResourceActions actions;
        private readonly TableLayoutPanel table, buttons;
        public readonly ListBox Items = new ListBox();
        public readonly TextBox Preview = new TextBox();
        public readonly Button CopyUrl = new Button(), CopyCode = new Button(), CopyShare = new Button(), OpenLink = new Button();
        public ResourcesPanel(IResourceActions resourceActions = null)
        {
            actions = resourceActions ?? new ResourceActions(); Dock = DockStyle.Fill; AutoScroll = true;
            table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Padding = new Padding(2), Height = 260 };
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            Items.Dock = DockStyle.Fill; Items.HorizontalScrollbar = true; Items.IntegralHeight = false; table.Controls.Add(Items, 0, 0);
            Preview.Dock = DockStyle.Fill; Preview.Multiline = true; Preview.ReadOnly = true; Preview.ScrollBars = ScrollBars.Both; Preview.WordWrap = false; table.Controls.Add(Preview, 0, 1);
            buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            int n = 0; foreach (var pair in new[] { Tuple.Create(CopyUrl, "复制地址"), Tuple.Create(CopyCode, "复制提取码"), Tuple.Create(CopyShare, "复制分享信息"), Tuple.Create(OpenLink, "浏览器打开") })
            { pair.Item1.Text = L.T(pair.Item2); pair.Item1.Dock = DockStyle.Fill; pair.Item1.FlatStyle = FlatStyle.Flat; pair.Item1.MinimumSize = new Size(0, 32); buttons.Controls.Add(pair.Item1, n % 2, n / 2); n++; }
            table.Controls.Add(buttons, 0, 2); Controls.Add(table);
            Items.SelectedIndexChanged += delegate { RefreshItem(); };
            SizeChanged += delegate { SizeTable(); }; FontChanged += delegate { SizeTable(); };
            CopyUrl.Click += delegate { Act(false, r => r.Url); }; CopyCode.Click += delegate { Act(false, r => r.Code); }; CopyShare.Click += delegate { Act(false, r => r.ShareText); }; OpenLink.Click += delegate { Act(true, r => r.Url); };
            RefreshItem(); SizeTable();
        }
        private void SizeTable()
        {
            int rowHeight = buttons.Controls.Cast<Control>().Max(button => TextRenderer.MeasureText(button.Text, button.Font).Height + button.Margin.Vertical + 16);
            table.RowStyles[2].Height = rowHeight * 2;
            int minimum = rowHeight * 2 + Math.Max(120, Font.Height * 7);
            int height = Math.Max(minimum, ClientSize.Height - Padding.Vertical);
            if (table.Height != height) table.Height = height;
        }
        public void LoadNode(CatalogNode node)
        {
            Items.Items.Clear();
            for (CatalogNode owner = node; owner != null; owner = owner.Parent)
                foreach (ResourceLink link in owner.Resources) Items.Items.Add(new Row { Link = link, Owner = owner.Title });
            if (Items.Items.Count > 0) Items.SelectedIndex = 0; else RefreshItem();
        }
        private void RefreshItem()
        {
            var row = Items.SelectedItem as Row;
            Preview.Text = row == null ? L.T("此条目及其上级暂无资源。") : row.Link.ShareText + (row.Link.Error == null ? "" : "\r\n" + L.T(row.Link.Error));
            bool enabled = row != null && row.Link.Error == null;
            CopyUrl.Enabled = CopyShare.Enabled = enabled; CopyCode.Enabled = enabled && !string.IsNullOrEmpty(row.Link.Code); OpenLink.Enabled = enabled && row.Link.CanOpen;
        }
        private void Act(bool open, Func<ResourceLink, string> value)
        {
            var row = Items.SelectedItem as Row; if (row == null || row.Link.Error != null) return;
            try { if (open) { if (row.Link.CanOpen) actions.Open(row.Link.Url); } else if (!string.IsNullOrEmpty(value(row.Link))) actions.Copy(value(row.Link)); }
            catch (Exception ex) { MessageBox.Show(this, L.T(Files.Redact(ex.Message)), L.T("资源操作失败"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
}
