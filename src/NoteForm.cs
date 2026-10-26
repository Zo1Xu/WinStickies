using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace WinStickies
{
    internal sealed class NoteForm : Form
    {
        internal readonly Note Note;
        internal readonly NoteEditor Editor;
        internal readonly TextBox TitleEditor;
        internal readonly ThinScrollBar ScrollBar;
        internal event Action<NoteForm> Changed, NewRequested, LibraryRequested, ArchiveRequested, DeleteRequested;
        internal event Action<NoteForm> CloseRequested;
        internal event Action<string> Error;
        private readonly ActionButton foldButton, modeButton, menuButton, closeButton;
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer resizeTimer = new Timer { Interval = 16 };
        private bool updating, ready, dragging, resizing;
        private bool hasPendingBounds, interactionChanged;
        private bool editingTitle;
        private string originalTitle;
        private Rectangle pendingBounds;
        private Point dragCursor;
        private Rectangle dragBounds;
        private int resizeEdge;
        private string saveStatus = "已保存到本机";
        internal IntPtr DesktopParent { get; private set; }
        internal static int HeaderHeight { get { return Ui.S(38); } }
        internal static int MinNoteWidth { get { return Ui.S(200); } }
        internal static int MinNoteHeight { get { return Ui.S(140); } }
        internal static int MinCollapsedWidth { get { return Ui.S(180); } }
        internal static int MinCollapsedHeight { get { return Ui.S(30); } }
        private int TitleHeight { get { return Note.Collapsed ? Height : HeaderHeight; } }
        private int ResizeBorder { get { return Ui.S(Note.Collapsed ? 6 : 8); } }
        private int TitleAvailableWidth { get { return Math.Max(1, Width - Ui.S(142)); } }
        private int TitleTextMaxWidth { get { return Math.Max(1, TitleAvailableWidth - Math.Min(Ui.S(28), Math.Max(Ui.S(18), TitleAvailableWidth / 4))); } }
        internal Rectangle TitleTextBounds
        {
            get
            {
                Size size = TextRenderer.MeasureText(Note.Title, Ui.Font(9, true), Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                return new Rectangle(Ui.S(41), (TitleHeight - size.Height) / 2, Math.Min(TitleTextMaxWidth, size.Width), size.Height);
            }
        }
        internal Rectangle TitleBlankBounds
        {
            get { var text = TitleTextBounds; return new Rectangle(text.Right, ResizeBorder,
                Math.Max(1, Ui.S(41) + TitleAvailableWidth - text.Right), Math.Max(1, TitleHeight - 2 * ResizeBorder)); }
        }
        internal bool AllowClose;
        internal bool IsInteracting { get { return dragging || resizing; } }

        internal NoteForm(Note note)
        {
            Note = note; Text = note.Title + " · WinStickies";
            Note.FontSize = Note.NormalizeFontSize(Note.FontSize);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            // WS_EX_TOOLWINDOW is set in CreateParams; avoid WinForms' hidden owner,
            // which otherwise interferes with switching between child and top-level HWNDs.
            ShowInTaskbar = true; StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true; KeyPreview = true; Icon = Ui.CreateIcon();
            SetStyle(ControlStyles.ResizeRedraw, true);
            Note.ExpandedWidth = Math.Max(MinNoteWidth, Note.ExpandedWidth);
            Note.ExpandedHeight = Math.Max(MinNoteHeight, Note.ExpandedHeight);
            if (Note.CollapsedWidth > 0) Note.CollapsedWidth = Math.Max(MinCollapsedWidth, Note.CollapsedWidth);
            if (Note.CollapsedHeight > 0) Note.CollapsedHeight = Math.Max(MinCollapsedHeight, Note.CollapsedHeight);
            Rectangle visible = Placement.KeepReachable(note.VisibleBounds(HeaderHeight), Screen.AllScreens.Select(s => s.WorkingArea), HeaderHeight);
            Note.X = visible.X; Note.Y = visible.Y;
            Bounds = visible;
            Editor = new NoteEditor { BorderStyle = BorderStyle.None,
                AcceptsTab = false, Font = Ui.Font(NoteEditor.BaseFontSize),
                ForeColor = Ui.Ink, AccessibleName = "便签内容", MaxLength = 2000000 };
            Editor.LoadContent(note);
            Editor.TextChanged += delegate { CaptureContent(); };
            Editor.FormattingChanged += delegate { CaptureContent(); };
            Controls.Add(Editor);
            TitleEditor = new TextBox { BorderStyle = BorderStyle.FixedSingle, Font = Ui.Font(9, true),
                ForeColor = Ui.Ink, AccessibleName = "便签标题", MaxLength = 200, Visible = false, TabStop = false };
            TitleEditor.TextChanged += delegate
            {
                if (editingTitle) UpdateTitle(Note.CleanTitle(TitleEditor.Text));
            };
            TitleEditor.Leave += delegate { EndTitleEdit(true); };
            // Desktop-attached forms can deactivate while their child editor gains focus.
            // Finish on the editor's own focus loss instead of the form's activation state.
            TitleEditor.LostFocus += delegate { EndTitleEdit(true); };
            Controls.Add(TitleEditor);
            ScrollBar = new ThinScrollBar(Editor) { Visible = false };
            Controls.Add(ScrollBar);
            resizeTimer.Tick += delegate { resizeTimer.Stop(); ApplyPendingBounds(); };
            foldButton = new ActionButton("", "up", delegate { SetCollapsed(!Note.Collapsed); });
            modeButton = new ActionButton("", Ui.ModeIcon(Note.Mode), delegate { ShowModeMenu(); });
            menuButton = new ActionButton("", "dots", delegate { ShowNoteMenu(); });
            closeButton = new ActionButton("", "close", delegate { if (CloseRequested != null) CloseRequested(this); });
            Controls.AddRange(new Control[] { foldButton, modeButton, menuButton, closeButton });
            modeButton.AccessibleName = "显示模式"; menuButton.AccessibleName = "便签菜单";
            closeButton.AccessibleName = "关闭便签";
            tips.SetToolTip(closeButton, "关闭便签：选择删除或归档");
            tips.SetToolTip(menuButton, "字号、颜色与更多操作");
            tips.SetToolTip(this, "双击标题文字修改；双击标题旁空白展开或收起；拖动标题栏移动，拖动边缘调整大小");
            tips.SetToolTip(TitleEditor, "Enter 完成，Esc 取消；清空标题后自动使用正文首行");
            ApplyAppearance();
        }

        private void CaptureContent()
        {
            string content = Editor.Text, rtf = Editor.Rtf;
            if (Note.Content == content && Note.ContentRtf == rtf) return;
            Note.Content = content; Note.ContentRtf = rtf; Note.ModifiedUtc = DateTime.UtcNow;
            Text = Note.Title + " · WinStickies"; SignalChange(); Invalidate();
        }

        internal void BeginTitleEdit()
        {
            if (editingTitle) { TitleEditor.Focus(); return; }
            FinishInteraction();
            originalTitle = Note.CustomTitle ?? "";
            TitleEditor.Text = Note.Content.Length == 0 && originalTitle.Length == 0 ? "" : Note.Title;
            editingTitle = true; TitleEditor.Visible = true; LayoutChildren();
            TitleEditor.BringToFront(); TitleEditor.Focus(); TitleEditor.SelectAll(); Invalidate();
        }
        private void UpdateTitle(string title)
        {
            if ((Note.CustomTitle ?? "") == title) return;
            Note.CustomTitle = title; Note.ModifiedUtc = DateTime.UtcNow;
            Text = Note.Title + " · WinStickies"; SignalChange(); Invalidate();
        }
        private void EndTitleEdit(bool commit)
        {
            if (!editingTitle) return;
            editingTitle = false;
            if (!commit) UpdateTitle(originalTitle);
            TitleEditor.Visible = false; Invalidate();
        }
        internal void SetFontSize(float size)
        {
            size = Note.NormalizeFontSize(size);
            if (Note.FontSize == size) return;
            Note.FontSize = size; Note.ModifiedUtc = DateTime.UtcNow;
            Editor.SetBodyFontSize(size); SignalChange();
        }
        private void StepFontSize(bool larger)
        {
            float next = larger ? FontSizes.FirstOrDefault(s => s > Note.FontSize) : FontSizes.LastOrDefault(s => s < Note.FontSize);
            if (next > 0) SetFontSize(next);
        }
        private static readonly float[] FontSizes = { 9f, 10f, Note.DefaultFontSize, 12f, 14f, 16f, 18f, 20f, 24f, 28f, 32f, 36f };

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; cp.ExStyle &= ~0x40000; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e); Native.RoundCorners(Handle); ready = true;
            ApplySavedMode();
        }
        protected override void OnHandleDestroyed(EventArgs e)
        { ready = false; resizeTimer.Stop(); hasPendingBounds = dragging = resizing = false; base.OnHandleDestroyed(e); }
        internal void ApplySavedMode()
        {
            updating = true;
            try
            {
                Native.SetMode(Handle, Note.Mode, Note.VisibleBounds(HeaderHeight));
                DesktopParent = Note.Mode == NoteMode.Desktop ? Native.GetParent(Handle) : IntPtr.Zero;
            }
            catch (Exception ex)
            {
                // Preserve the requested mode; the shell monitor can retry after Explorer returns.
                DesktopParent = IntPtr.Zero;
                if (Error != null) Error(ex.Message);
            }
            finally { updating = false; }
            ApplyAppearance();
        }
        internal bool ChangeMode(NoteMode mode)
        {
            EndTitleEdit(true);
            if (mode == Note.Mode && (mode != NoteMode.Desktop || DesktopParent != IntPtr.Zero)) return true;
            CaptureGeometry(); updating = true;
            try
            {
                Native.SetMode(Handle, mode, Note.VisibleBounds(HeaderHeight));
                DesktopParent = mode == NoteMode.Desktop ? Native.GetParent(Handle) : IntPtr.Zero;
                Note.Mode = mode;
            }
            catch (Exception ex) { if (Error != null) Error(ex.Message); return false; }
            finally { updating = false; ApplyAppearance(); }
            SignalChange(); return true;
        }

        internal void CaptureGeometry()
        {
            if (!ready || updating || !IsHandleCreated || !Native.IsWindow(Handle) || Native.IsIconic(Handle)) return;
            if (DesktopParent != IntPtr.Zero && !Native.IsWindow(DesktopParent)) return;
            // GetWindowRect is screen-relative even when attached to a desktop parent.
            Note.CaptureBounds(Native.Bounds(Handle));
        }
        internal void SetCollapsed(bool collapsed)
        {
            EndTitleEdit(true);
            if (Note.Collapsed == collapsed) return;
            CaptureGeometry(); updating = true;
            try
            {
                Note.Collapsed = collapsed;
                if (collapsed)
                {
                    if (Note.CollapsedWidth <= 0) Note.CollapsedWidth = Note.ExpandedWidth;
                    if (Note.CollapsedHeight <= 0) Note.CollapsedHeight = HeaderHeight;
                }
                Native.Place(Handle, Note.VisibleBounds(HeaderHeight), DesktopParent);
                ApplyAppearance();
            }
            finally { updating = false; }
            SignalChange();
        }
        internal void Reveal()
        {
            if (Note.Collapsed) SetCollapsed(false);
            if (Note.Mode == NoteMode.Desktop)
            {
                // Never raise the desktop host above application windows.
                if (Error != null) Error("这张便签位于桌面层。按 Win+D 查看桌面，或在卡片菜单切换为普通悬浮。");
            }
            else { Show(); Native.SetForegroundWindow(Handle); Editor.Focus(); }
        }
        internal void SetSaveStatus(bool success)
        { saveStatus = success ? "已保存到本机" : "保存失败 · 请查看总览"; Invalidate(); }
        private void SignalChange() { saveStatus = "正在保存…"; if (Changed != null) Changed(this); }
        private void ApplyAppearance()
        {
            BackColor = Ui.Papers[Note.ColorIndex]; Editor.BackColor = BackColor; Editor.Visible = !Note.Collapsed;
            TitleEditor.BackColor = BackColor;
            ScrollBar.BackColor = BackColor; ScrollBar.Visible = !Note.Collapsed && Editor.MaximumLine > 0;
            foreach (var b in new[] { foldButton, modeButton, menuButton, closeButton }) { b.BackColor = BackColor; b.Quiet = true; }
            foldButton.Glyph = Note.Collapsed ? "down" : "up";
            foldButton.AccessibleName = Note.Collapsed ? "展开便签" : "收起便签";
            tips.SetToolTip(foldButton, (Note.Collapsed ? "展开便签" : "收起为标题栏") + "  Ctrl+Shift+C");
            modeButton.Glyph = Ui.ModeIcon(Note.Mode);
            tips.SetToolTip(modeButton, Ui.ModeName(Note.Mode) + "：" + Ui.ModeDescription(Note.Mode));
            LayoutChildren(); Invalidate(true);
        }
        private void LayoutChildren()
        {
            if (Editor == null || closeButton == null) return;
            SuspendLayout();
            try
            {
                int buttonHeight = Math.Max(1, Math.Min(Ui.S(26), TitleHeight - 2 * ResizeBorder));
                int y = (TitleHeight - buttonHeight) / 2;
                foldButton.SetBounds(Ui.S(9), y, Ui.S(26), buttonHeight);
                modeButton.SetBounds(Width - Ui.S(95), y, Ui.S(26), buttonHeight);
                menuButton.SetBounds(Width - Ui.S(66), y, Ui.S(26), buttonHeight);
                closeButton.SetBounds(Width - Ui.S(37), y, Ui.S(26), buttonHeight);
                TitleEditor.SetBounds(Ui.S(41), (TitleHeight - TitleEditor.PreferredHeight) / 2,
                    TitleTextMaxWidth, TitleEditor.PreferredHeight);
                if (!Note.Collapsed)
                {
                    int editorHeight = Math.Max(1, Height - HeaderHeight - Ui.S(54));
                    Editor.SetBounds(Ui.S(19), HeaderHeight + Ui.S(17), Math.Max(1, Width - Ui.S(41)), editorHeight);
                    ScrollBar.SetBounds(Width - Ui.S(17), Editor.Top, Ui.S(8), editorHeight);
                }
            }
            finally { ResumeLayout(false); }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutChildren(); if (ready && !updating) { CaptureGeometry(); if (!IsInteracting) SignalChange(); } }
        protected override void OnMove(EventArgs e) { base.OnMove(e); if (ready && !updating) { CaptureGeometry(); if (!IsInteracting) SignalChange(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Ui.Darken(BackColor, 23))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (!Note.Collapsed) using (var p = new Pen(Ui.Darken(BackColor, 13))) g.DrawLine(p, 1, HeaderHeight - 1, Width - 2, HeaderHeight - 1);
            if (!editingTitle) Ui.Text(g, Note.Title, Ui.Font(9, true), Ui.Ink, TitleTextBounds, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!Note.Collapsed)
            {
                Ui.Icon(g, Ui.ModeIcon(Note.Mode), Ui.Ink, new Rectangle(Ui.S(18), Height - Ui.S(25), Ui.S(13), Ui.S(13)));
                bool wide = Width >= Ui.S(280);
                if (wide) Ui.Text(g, Ui.ModeName(Note.Mode), Ui.Font(7.75f), Ui.Ink, new Rectangle(Ui.S(37), Height - Ui.S(27), Ui.S(75), Ui.S(18)), TextFormatFlags.VerticalCenter);
                int statusX = wide ? Width - Ui.S(142) : Ui.S(38);
                Ui.Text(g, saveStatus, Ui.Font(7.5f), Ui.Ink, new Rectangle(statusX, Height - Ui.S(27), Math.Max(1, Width - statusX - Ui.S(24)), Ui.S(18)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            using (var p = new Pen(Ui.Darken(BackColor, 80)))
                for (int i = 0; i < (Note.Collapsed ? 2 : 3); i++) g.DrawLine(p, Width - Ui.S(4 + i * 3), Height - Ui.S(4), Width - Ui.S(4), Height - Ui.S(4 + i * 3));
        }

        private int Edge(Point p)
        {
            int b = ResizeBorder, result = 0;
            if (p.X <= b) result |= 1; if (p.X >= Width - b) result |= 2;
            if (p.Y <= b) result |= 4; if (p.Y >= Height - b) result |= 8;
            if (!Note.Collapsed && p.X >= Width - Ui.S(18) && p.Y >= Height - Ui.S(18)) result = 10;
            return result;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { ShowNoteMenu(); return; }
            if (e.Button != MouseButtons.Left) return;
            resizeEdge = Edge(e.Location); resizing = resizeEdge != 0;
            if (!resizing && e.Clicks == 2 && e.Y < TitleHeight)
            {
                FinishInteraction();
                if (TitleTextBounds.Contains(e.Location)) BeginTitleEdit();
                else SetCollapsed(!Note.Collapsed);
                return;
            }
            EndTitleEdit(true);
            dragging = !resizing && e.Y < TitleHeight;
            if (IsInteracting)
            { resizeTimer.Stop(); hasPendingBounds = interactionChanged = false; dragCursor = PointToScreen(e.Location); dragBounds = Native.Bounds(Handle); Capture = true; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging || resizing)
            {
                var pointer = PointToScreen(e.Location);
                int dx = pointer.X - dragCursor.X, dy = pointer.Y - dragCursor.Y;
                var r = dragBounds;
                if (dragging) { r.X += dx; r.Y += dy; }
                else
                {
                    int minWidth = Note.Collapsed ? MinCollapsedWidth : MinNoteWidth;
                    int minHeight = Note.Collapsed ? MinCollapsedHeight : MinNoteHeight;
                    if ((resizeEdge & 1) != 0) { int width = Math.Max(minWidth, dragBounds.Width - dx); r.X = dragBounds.Right - width; r.Width = width; }
                    if ((resizeEdge & 2) != 0) r.Width = Math.Max(minWidth, dragBounds.Width + dx);
                    if ((resizeEdge & 4) != 0) { int height = Math.Max(minHeight, dragBounds.Height - dy); r.Y = dragBounds.Bottom - height; r.Height = height; }
                    if ((resizeEdge & 8) != 0) r.Height = Math.Max(minHeight, dragBounds.Height + dy);
                }
                pendingBounds = r; hasPendingBounds = true;
                if (!resizeTimer.Enabled) resizeTimer.Start();
            }
            else
            {
                int edge = Edge(e.Location);
                Cursor = edge == 5 || edge == 10 ? Cursors.SizeNWSE : edge == 6 || edge == 9 ? Cursors.SizeNESW :
                    (edge & 3) != 0 ? Cursors.SizeWE : (edge & 12) != 0 ? Cursors.SizeNS :
                    TitleTextBounds.Contains(e.Location) ? Cursors.IBeam : Cursors.Default;
            }
        }
        private void ApplyPendingBounds()
        {
            if (!hasPendingBounds || !ready || !IsHandleCreated) return;
            hasPendingBounds = false;
            if (Native.Bounds(Handle) == pendingBounds) return;
            updating = true;
            try { Native.Place(Handle, pendingBounds, DesktopParent); }
            finally { updating = false; }
            CaptureGeometry(); interactionChanged = true;
        }
        private void FinishInteraction()
        {
            if (!IsInteracting) return;
            resizeTimer.Stop(); ApplyPendingBounds();
            dragging = resizing = false; Capture = false;
            if (interactionChanged) { interactionChanged = false; SignalChange(); }
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); FinishInteraction(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) FinishInteraction(); base.OnMouseCaptureChanged(e); }

        internal ContextMenuStrip BuildMenu(bool modesOnly, bool includeModes = true)
        {
            var menu = new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
            if (includeModes)
            {
                foreach (NoteMode value in Enum.GetValues(typeof(NoteMode)))
                {
                    NoteMode mode = value;
                    var item = new ToolStripMenuItem(Ui.ModeName(mode)) { Checked = Note.Mode == mode, ToolTipText = Ui.ModeDescription(mode) };
                    item.Click += delegate { ChangeMode(mode); }; menu.Items.Add(item);
                }
            }
            if (!modesOnly)
            {
                if (includeModes) menu.Items.Add(new ToolStripSeparator());
                var colors = new ToolStripMenuItem("便签颜色");
                for (int i = 0; i < Ui.Papers.Length; i++)
                {
                    int index = i; var item = new ToolStripMenuItem(Ui.ColorNames[i]) { Checked = Note.ColorIndex == i };
                    item.Click += delegate { Note.ColorIndex = index; ApplyAppearance(); SignalChange(); }; colors.DropDownItems.Add(item);
                }
                menu.Items.Add(colors);
                var sizes = new ToolStripMenuItem("正文字号");
                foreach (float value in FontSizes)
                {
                    float size = value;
                    var item = new ToolStripMenuItem(size.ToString("0.##") + " 磅" + (size == Note.DefaultFontSize ? "（默认）" : ""))
                        { Checked = Note.FontSize == size };
                    item.Click += delegate { SetFontSize(size); }; sizes.DropDownItems.Add(item);
                }
                menu.Items.Add(sizes);
                menu.Items.Add("修改标题    F2", null, delegate { BeginTitleEdit(); });
                menu.Items.Add(Note.Collapsed ? "展开便签" : "收起便签", null, delegate { SetCollapsed(!Note.Collapsed); });
                menu.Items.Add("归档便签", null, delegate { if (ArchiveRequested != null) ArchiveRequested(this); });
                menu.Items.Add("新建便签    Ctrl+N", null, delegate { if (NewRequested != null) NewRequested(this); });
                menu.Items.Add("便签总览    Ctrl+Shift+L", null, delegate { if (LibraryRequested != null) LibraryRequested(this); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("删除这张便签…", null, delegate { if (DeleteRequested != null) DeleteRequested(this); });
            }
            // The note itself may be disposed by the archive/delete action.
            menu.Closed += delegate { menu.BeginInvoke((Action)delegate { menu.Dispose(); }); };
            return menu;
        }
        private void ShowModeMenu() { BuildMenu(true).Show(modeButton, new Point(0, modeButton.Height)); }
        private void ShowNoteMenu() { BuildMenu(false, false).Show(menuButton, new Point(menuButton.Width, menuButton.Height), ToolStripDropDownDirection.BelowLeft); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (editingTitle && (keyData == Keys.Enter || keyData == Keys.Escape))
            { EndTitleEdit(keyData == Keys.Enter); if (!Note.Collapsed) Editor.Focus(); else foldButton.Focus(); return true; }
            if (editingTitle && keyData == (Keys.Control | Keys.A)) { TitleEditor.SelectAll(); return true; }
            if (keyData == Keys.F2) { BeginTitleEdit(); return true; }
            Keys key = keyData & Keys.KeyCode, modifiers = keyData & Keys.Modifiers;
            if (!editingTitle && (modifiers == Keys.Control || modifiers == (Keys.Control | Keys.Shift)))
            {
                if (key == Keys.Oemplus || key == Keys.Add) { StepFontSize(true); return true; }
                if (key == Keys.OemMinus || key == Keys.Subtract) { StepFontSize(false); return true; }
            }
            if (keyData == (Keys.Control | Keys.B))
            { if (!editingTitle && Editor.Focused) Editor.ToggleBold(); return true; }
            if (!editingTitle && Editor.Focused && keyData == (Keys.Control | Keys.Z)) { Editor.Undo(); return true; }
            if (!editingTitle && Editor.Focused && (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)))
            { Editor.Redo(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.C)) { SetCollapsed(!Note.Collapsed); return true; }
            if (keyData == (Keys.Control | Keys.N)) { if (NewRequested != null) NewRequested(this); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.L)) { if (LibraryRequested != null) LibraryRequested(this); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SignalChange(); return true; }
            if (keyData == (Keys.Alt | Keys.F4)) { SetCollapsed(true); return true; }
            if (keyData == (Keys.Control | Keys.A) && Editor.Focused) { Editor.SelectAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            EndTitleEdit(true);
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; SetCollapsed(true); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { resizeTimer.Stop(); resizeTimer.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
    }
}
