using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace WinStickies
{
    internal sealed class LibraryForm : Form
    {
        private readonly StickyContext app;
        private readonly FlowLayoutPanel cards;
        private readonly TextBox search;
        private readonly ActionButton[] filters;
        private readonly ActionButton create, collapseAll, expandAll, close, minimize;
        private readonly Label status;
        private int filter;
        private bool dragging;
        private Point dragOrigin, windowOrigin;
        internal LibraryForm(StickyContext context)
        {
            app = context; Text = "WinStickies · 便签总览"; Icon = Ui.CreateIcon();
            FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Ui.Background; DoubleBuffered = true;
            Size = new Size(Ui.S(1040), Ui.S(720)); MinimumSize = new Size(Ui.S(780), Ui.S(560));
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - Ui.S(40)), Math.Min(Height, area.Height - Ui.S(40)));
            KeyPreview = true; SetStyle(ControlStyles.ResizeRedraw, true);
            create = new ActionButton("新建便签", "plus", delegate { app.NewNote(); }) { Primary = true };
            collapseAll = new ActionButton("全部收起", "up", delegate { app.SetAllCollapsed(true); });
            expandAll = new ActionButton("全部展开", "down", delegate { app.SetAllCollapsed(false); });
            close = new ActionButton("", "close", delegate { Close(); }) { Quiet = true, AccessibleName = "关闭总览，便签继续运行" };
            minimize = new ActionButton("", "min", delegate { WindowState = FormWindowState.Minimized; }) { Quiet = true, AccessibleName = "最小化总览" };
            Controls.AddRange(new Control[] { create, collapseAll, expandAll, close, minimize });
            filters = new ActionButton[5];
            string[] names = { "桌面便签", "普通悬浮", "始终置顶", "桌面附着", "已归档" };
            string[] glyphs = { "grid", "float", "pin", "desktop", "archive" };
            for (int i = 0; i < filters.Length; i++)
            {
                int selected = i;
                filters[i] = new ActionButton(names[i], glyphs[i], delegate { filter = selected; RefreshNotes(); })
                    { AlignLeft = true, Quiet = true, BackColor = Ui.Sidebar };
                Controls.Add(filters[i]);
            }
            search = new TextBox { BorderStyle = BorderStyle.None, Font = Ui.Font(10), BackColor = Color.White,
                ForeColor = Ui.Ink, AccessibleName = "搜索便签", TabIndex = 0 };
            search.HandleCreated += delegate { SetCueBanner(search.Handle, 0x1501, new IntPtr(1), "搜索便签…"); };
            search.TextChanged += delegate { RefreshNotes(); }; Controls.Add(search);
            cards = new FlowLayoutPanel { AutoScroll = true, BackColor = Ui.Background, WrapContents = true,
                Padding = new Padding(0), FlowDirection = FlowDirection.LeftToRight, AccessibleName = "便签列表" };
            Controls.Add(cards);
            status = new Label { Font = Ui.Font(8.25f), ForeColor = Ui.Muted, BackColor = Ui.Background,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(status);
            LayoutChildren(); RefreshNotes();
        }

        internal void RefreshNotes()
        {
            if (cards == null || IsDisposed) return;
            foreach (var pair in filters.Select((button, index) => new { button, index }))
            {
                pair.button.Selected = pair.index == filter;
                pair.button.TrailingText = app.Book.Notes.Count(n => MatchesFilter(n, pair.index)).ToString();
                pair.button.Invalidate();
            }
            var scroll = cards.AutoScrollPosition;
            cards.SuspendLayout();
            foreach (Control c in cards.Controls.Cast<Control>().ToArray()) c.Dispose();
            var selected = app.Book.Notes.Where(n => MatchesFilter(n, filter) &&
                (string.IsNullOrWhiteSpace(search.Text) || n.Title.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.Content.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            int usable = cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            int columns = Math.Max(1, usable / Ui.S(258));
            int width = Math.Max(Ui.S(220), usable / columns - Ui.S(16));
            foreach (var note in selected)
            {
                var card = new NoteCard(note, app) { Width = width, Height = Ui.S(204), Margin = new Padding(0, 0, Ui.S(16), Ui.S(16)) };
                cards.Controls.Add(card);
            }
            if (selected.Length == 0)
                cards.Controls.Add(new Label { Text = string.IsNullOrEmpty(search.Text) ? (filter == 4 ? "还没有归档的便签\n在便签的“…”菜单中选择“归档便签”。" : "这里还没有便签\n点击“新建便签”，记下第一个想法。") : "没有找到匹配的便签\n试试其他关键词。",
                    Size = new Size(Math.Max(1, usable), Ui.S(200)), Font = Ui.Font(12), ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleCenter });
            cards.ResumeLayout(); cards.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            collapseAll.Enabled = expandAll.Enabled = filter != 4 && app.Book.Notes.Any(n => !n.Archived);
            UpdateStatus(); Invalidate();
        }
        private static bool MatchesFilter(Note note, int selected)
        { return selected == 4 ? note.Archived : !note.Archived && (selected == 0 || (int)note.Mode == selected - 1); }
        internal void UpdateStatus()
        {
            if (status == null) return;
            status.Text = string.IsNullOrEmpty(app.LastError) ? (app.IsDirty ? "正在保存…" : "●  所有更改已保存到本机") + "     ·     " + app.Book.Notes.Count(n => !n.Archived) + " 张桌面便签 · " + app.Book.Notes.Count(n => n.Archived) + " 张已归档" : app.LastError;
            status.ForeColor = string.IsNullOrEmpty(app.LastError) ? Ui.Muted : Color.FromArgb(161, 65, 49);
        }
        private void LayoutChildren()
        {
            if (cards == null) return;
            int left = Ui.S(218), right = Ui.S(28);
            close.SetBounds(Width - Ui.S(45), Ui.S(10), Ui.S(32), Ui.S(30));
            minimize.SetBounds(Width - Ui.S(81), Ui.S(10), Ui.S(32), Ui.S(30));
            create.SetBounds(Width - right - Ui.S(122), Ui.S(80), Ui.S(122), Ui.S(39));
            for (int i = 0; i < filters.Length; i++) filters[i].SetBounds(Ui.S(16), Ui.S(110 + i * 48), Ui.S(158), Ui.S(40));
            int searchWidth = Math.Max(Ui.S(135), Width - left - right - Ui.S(244));
            search.SetBounds(left + Ui.S(38), Ui.S(161), searchWidth - Ui.S(48), Ui.S(24));
            collapseAll.SetBounds(Width - right - Ui.S(226), Ui.S(152), Ui.S(106), Ui.S(39));
            expandAll.SetBounds(Width - right - Ui.S(110), Ui.S(152), Ui.S(110), Ui.S(39));
            cards.SetBounds(left, Ui.S(217), Width - left - Ui.S(14), Height - Ui.S(270));
            status.SetBounds(left, Height - Ui.S(42), Width - left - right, Ui.S(26));
            foreach (var b in new[] { create, collapseAll, expandAll, close, minimize }) b.BackColor = Ui.Background;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutChildren(); RefreshNotes(); }
        protected override void OnShown(EventArgs e) { Native.RoundCorners(Handle); base.OnShown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Ui.Sidebar)) g.FillRectangle(b, 0, 0, Ui.S(190), Height);
            using (var pen = new Pen(Ui.Line)) { g.DrawLine(pen, Ui.S(190), 0, Ui.S(190), Height); g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
            Ui.FillRound(g, Ui.Papers[0], new Rectangle(Ui.S(23), Ui.S(25), Ui.S(32), Ui.S(32)), Ui.S(8));
            Ui.Icon(g, "note", Ui.Accent, new Rectangle(Ui.S(29), Ui.S(31), Ui.S(20), Ui.S(20)));
            Ui.Text(g, "WinStickies", Ui.Font(12, true), Ui.Ink, new Rectangle(Ui.S(64), Ui.S(28), Ui.S(117), Ui.S(28)));
            Ui.Text(g, "留一点空间给想法。", Ui.Font(8.5f), Ui.Muted, new Rectangle(Ui.S(24), Ui.S(72), Ui.S(150), Ui.S(22)));
            Ui.Text(g, filter == 4 ? "已归档" : filter == 0 ? "我的便签" : Ui.ModeName((NoteMode)(filter - 1)), Ui.Font(23, true), Ui.Ink, new Rectangle(Ui.S(216), Ui.S(69), Ui.S(300), Ui.S(48)));
            Ui.Text(g, filter == 4 ? "内容一直保留，点击便签即可重新打开。" : "随手记下，留在眼前。", Ui.Font(9.5f), Ui.Muted, new Rectangle(Ui.S(219), Ui.S(118), Ui.S(400), Ui.S(25)));
            int searchWidth = Math.Max(Ui.S(135), Width - Ui.S(218 + 28 + 244));
            Ui.FillRound(g, Color.White, new Rectangle(Ui.S(218), Ui.S(152), searchWidth, Ui.S(39)), Ui.S(7));
            Ui.Icon(g, "search", Ui.Muted, new Rectangle(Ui.S(230), Ui.S(163), Ui.S(17), Ui.S(17)));
            int y = Math.Max(Ui.S(330), Height - Ui.S(213));
            Ui.FillRound(g, Color.FromArgb(232, 235, 225), new Rectangle(Ui.S(17), y, Ui.S(156), Ui.S(140)), Ui.S(10));
            Ui.Icon(g, "up", Ui.Accent, new Rectangle(Ui.S(30), y + Ui.S(15), Ui.S(21), Ui.S(21)));
            Ui.Text(g, "把想法收好", Ui.Font(10, true), Ui.Ink, new Rectangle(Ui.S(30), y + Ui.S(45), Ui.S(125), Ui.S(24)));
            Ui.Text(g, "双击文字修改标题。\n双击旁边空白收起或展开。\nCtrl + / − 调整正文字号。", Ui.Font(8.25f), Ui.Muted,
                new Rectangle(Ui.S(30), y + Ui.S(76), Ui.S(135), Ui.S(62)), TextFormatFlags.WordBreak);
            Ui.Text(g, "本地保存  /  无需登录", Ui.Font(8), Ui.Muted, new Rectangle(Ui.S(26), Height - Ui.S(47), Ui.S(150), Ui.S(24)));
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr SetCueBanner(IntPtr hwnd, int message, IntPtr wParam, string text);
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && e.Y < Ui.S(60)) { dragging = true; dragOrigin = Cursor.Position; windowOrigin = Location; Capture = true; } }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); if (dragging) Location = new Point(windowOrigin.X + Cursor.Position.X - dragOrigin.X, windowOrigin.Y + Cursor.Position.Y - dragOrigin.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { app.NewNote(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal sealed class NoteCard : Panel
    {
        private readonly Note note;
        private readonly StickyContext app;
        private readonly ActionButton more, fold;
        private readonly ToolTip tips = new ToolTip();
        private bool hover;
        internal NoteCard(Note item, StickyContext context)
        {
            note = item; app = context; BackColor = Ui.Background; DoubleBuffered = true; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true); TabStop = true; AccessibleRole = AccessibleRole.PushButton; AccessibleName = note.Title + "，" + (note.Archived ? "已归档，点击重新打开" : Ui.ModeName(note.Mode));
            more = new ActionButton("", "dots", delegate { (note.Archived ? app.BuildArchivedMenu(note.Id) : app.WindowFor(note.Id).BuildMenu(false)).Show(more, new Point(0, more.Height)); }) { Quiet = true, AccessibleName = "便签菜单", BackColor = Ui.Papers[note.ColorIndex] };
            fold = new ActionButton("", note.Archived ? "arrow" : note.Collapsed ? "down" : "up", delegate { if (note.Archived) app.RestoreNote(note.Id); else app.WindowFor(note.Id).SetCollapsed(!note.Collapsed); }) { Quiet = true, AccessibleName = note.Archived ? "重新打开便签" : note.Collapsed ? "展开便签" : "收起便签", BackColor = Ui.Papers[note.ColorIndex] };
            tips.SetToolTip(fold, note.Archived ? "重新打开便签（保留原状态）" : fold.AccessibleName);
            Controls.AddRange(new Control[] { more, fold });
            Click += delegate { OpenNote(); };
        }
        private void OpenNote() { if (note.Archived) app.RestoreNote(note.Id); else app.WindowFor(note.Id).Reveal(); }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
        protected override void OnResize(EventArgs e)
        { base.OnResize(e); if (more != null) { more.SetBounds(Width - Ui.S(42), Ui.S(10), Ui.S(28), Ui.S(28)); fold.SetBounds(Width - Ui.S(76), Ui.S(10), Ui.S(28), Ui.S(28)); } }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OpenNote(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 2, Height - 2);
            Ui.FillRound(g, Ui.Papers[note.ColorIndex], bounds, Ui.S(10));
            using (var path = Ui.Round(bounds, Ui.S(10))) using (var pen = new Pen(hover || Focused ? Ui.Accent : Ui.Darken(Ui.Papers[note.ColorIndex], 16))) g.DrawPath(pen, path);
            Ui.Text(g, note.Title, Ui.Font(11, true), Ui.Ink, new Rectangle(Ui.S(18), Ui.S(17), Width - Ui.S(97), Ui.S(26)), TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            string[] lines = note.Content.Replace("\r", "").Split('\n');
            string preview = string.Join("\n", lines.SkipWhile(s => string.IsNullOrWhiteSpace(s)).Skip(string.IsNullOrWhiteSpace(note.CustomTitle) ? 1 : 0)).Trim();
            if (preview.Length == 0) preview = note.Archived ? "点击重新打开便签" : note.Content.Length == 0 ? "点击开始记录…" : "点击回到便签";
            Ui.Text(g, preview, Ui.Font(9.25f), Ui.Ink, new Rectangle(Ui.S(18), Ui.S(57), Width - Ui.S(37), Ui.S(90)), TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.TextBoxControl);
            using (var pen = new Pen(Ui.Darken(Ui.Papers[note.ColorIndex], 16))) g.DrawLine(pen, Ui.S(18), Height - Ui.S(44), Width - Ui.S(18), Height - Ui.S(44));
            Ui.Icon(g, Ui.ModeIcon(note.Mode), Ui.Ink, new Rectangle(Ui.S(18), Height - Ui.S(31), Ui.S(14), Ui.S(14)));
            Ui.Text(g, Ui.ModeName(note.Mode), Ui.Font(8), Ui.Ink, new Rectangle(Ui.S(38), Height - Ui.S(33), Ui.S(96), Ui.S(20)), TextFormatFlags.VerticalCenter);
            Ui.Text(g, note.Archived ? "已归档" : note.Collapsed ? "已收起" : "已展开", Ui.Font(8), Ui.Ink, new Rectangle(Width - Ui.S(71), Height - Ui.S(33), Ui.S(51), Ui.S(20)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
