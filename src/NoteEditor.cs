using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinStickies
{
    // Keep native rich editing for IME, selection, bold formatting and undo. Only its scrollbar
    // is replaced; scroll positions come from the edit control's wrapped lines.
    internal sealed class NoteEditor : RichTextBox
    {
        // Use an exact half-point RTF base size so saving never rounds a fractional font size.
        internal const float BaseFontSize = 12f;
        internal event EventHandler ScrollMetricsChanged;
        internal event EventHandler FormattingChanged;
        internal int TotalLines { get; private set; }
        internal int PageLines { get; private set; }
        internal int FirstLine { get; private set; }
        internal int MaximumLine { get { return Math.Max(0, TotalLines - PageLines); } }
        private bool refreshQueued, refreshing;
        private int wheelRemainder;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);
        private static readonly IntPtr RichEditLibrary = LoadLibrary(System.IO.Path.Combine(Environment.SystemDirectory, "msftedit.dll"));
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; if (RichEditLibrary != IntPtr.Zero) cp.ClassName = "RICHEDIT50W"; return cp; }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr GetFormatRectangle(IntPtr hwnd, int message, IntPtr wParam, out Native.RECT rect);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CharFormat
        {
            public uint Size, Mask, Effects;
            public int Height, Offset, TextColor;
            public byte CharacterSet, Pitch;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Face;
        }
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern IntPtr FormatMessage(IntPtr hwnd, int message, IntPtr wParam, ref CharFormat format);

        internal NoteEditor()
        {
            Multiline = true; ScrollBars = RichTextBoxScrollBars.None; WordWrap = true;
            DetectUrls = false; AutoWordSelection = false; HideSelection = false;
            LanguageOption = RichTextBoxLanguageOptions.UIFonts;
        }
        internal void LoadContent(Note note)
        {
            // Plain content remains authoritative if an older version edited the notebook.
            bool restored = false;
            if (!string.IsNullOrEmpty(note.ContentRtf))
            {
                try { Rtf = note.ContentRtf; restored = Text.Replace("\r", "") == note.Content.Replace("\r", ""); }
                catch (ArgumentException) { }
            }
            if (!restored) { Text = note.Content; SelectAll(); SelectionFont = Ui.Font(BaseFontSize); note.ContentRtf = null; }
            SetBodyFontSize(note.FontSize); Select(0, 0); ClearUndo();
        }
        internal void SetBodyFontSize(float size)
        {
            // Scaling the document keeps mixed bold runs, selection and native undo intact.
            ZoomFactor = Note.NormalizeFontSize(size) / BaseFontSize;
            RefreshScrollMetrics();
        }
        internal bool SelectionIsBold
        {
            get
            {
                var format = new CharFormat { Size = (uint)Marshal.SizeOf(typeof(CharFormat)) };
                FormatMessage(Handle, 0x043A, new IntPtr(1), ref format); // EM_GETCHARFORMAT, SCF_SELECTION
                return (format.Mask & 1) != 0 && (format.Effects & 1) != 0;
            }
        }
        internal void ToggleBold()
        {
            var format = new CharFormat { Size = (uint)Marshal.SizeOf(typeof(CharFormat)), Mask = 1,
                Effects = SelectionIsBold ? 0u : 1u };
            FormatMessage(Handle, 0x0444, new IntPtr(1), ref format); // EM_SETCHARFORMAT, SCF_SELECTION, CFM_BOLD
            NotifyFormattingChanged();
        }
        private void NotifyFormattingChanged()
        { QueueMetrics(); if (FormattingChanged != null) FormattingChanged(this, EventArgs.Empty); }

        private void QueueMetrics()
        {
            if (refreshQueued || refreshing || !IsHandleCreated || IsDisposed) return;
            refreshQueued = true;
            BeginInvoke((Action)delegate { refreshQueued = false; if (!IsDisposed && IsHandleCreated) RefreshScrollMetrics(); });
        }
        internal void RefreshScrollMetrics()
        {
            if (!IsHandleCreated || refreshing || IsDisposed) return;
            refreshing = true;
            try
            {
                int total = Math.Max(1, (int)SendMessage(Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero)); // EM_GETLINECOUNT
                int first = (int)SendMessage(Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero); // EM_GETFIRSTVISIBLELINE
                int lineHeight = Math.Max(1, (int)Math.Ceiling(Font.Height * ZoomFactor));
                if (total > 1)
                {
                    int visible = Math.Min(Math.Max(0, first), total - 2);
                    int start = GetFirstCharIndexFromLine(visible), second = GetFirstCharIndexFromLine(visible + 1);
                    if (start >= 0 && second >= 0) lineHeight = Math.Max(1, GetPositionFromCharIndex(second).Y - GetPositionFromCharIndex(start).Y);
                }
                Native.RECT rect; GetFormatRectangle(Handle, 0x00B2, IntPtr.Zero, out rect); // EM_GETRECT
                int page = Math.Max(1, (rect.Bottom - rect.Top) / Math.Max(1, lineHeight));
                int maximum = Math.Max(0, total - page);
                if (first > maximum)
                {
                    SendMessage(Handle, 0x00B6, IntPtr.Zero, new IntPtr(maximum - first)); // EM_LINESCROLL
                    first = (int)SendMessage(Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
                }
                if (total == TotalLines && page == PageLines && first == FirstLine) return;
                TotalLines = total; PageLines = page; FirstLine = first;
                if (ScrollMetricsChanged != null) ScrollMetricsChanged(this, EventArgs.Empty);
            }
            finally { refreshing = false; }
        }
        internal void ScrollToLine(int line)
        {
            RefreshScrollMetrics();
            int next = Math.Max(0, Math.Min(MaximumLine, line));
            SendMessage(Handle, 0x00B6, IntPtr.Zero, new IntPtr(next - FirstLine));
            RefreshScrollMetrics();
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); QueueMetrics(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); QueueMetrics(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); QueueMetrics(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); QueueMetrics(); }
        protected override void OnHandleDestroyed(EventArgs e) { refreshQueued = false; base.OnHandleDestroyed(e); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A) // WM_MOUSEWHEEL: keep scrolling under our control, including Rich Edit's default Ctrl+wheel zoom.
            {
                wheelRemainder += unchecked((short)((m.WParam.ToInt64() >> 16) & 0xffff));
                int steps = wheelRemainder / 120; wheelRemainder %= 120;
                RefreshScrollMetrics();
                int lines = SystemInformation.MouseWheelScrollLines;
                if (steps != 0 && lines != 0) ScrollToLine(FirstLine - steps * (lines < 0 ? PageLines : lines));
                m.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref m);
            if (m.Msg == 0x0304 || m.Msg == 0x00C7 || m.Msg == 0x0454 ||
                (m.Msg == 0x0100 && (ModifierKeys & Keys.Control) != 0 &&
                    ((Keys)m.WParam.ToInt32() == Keys.Z || (Keys)m.WParam.ToInt32() == Keys.Y))) NotifyFormattingChanged();
            // Covers typing, keyboard/caret scrolling, selection dragging and native scrolling.
            if (m.Msg == 0x000F || m.Msg == 0x0115 || m.Msg == 0x00B6 || m.Msg == 0x00B7 ||
                m.Msg == 0x00B1 || m.Msg == 0x0100 || m.Msg == 0x0101 || m.Msg == 0x0200) QueueMetrics();
        }
    }

    internal sealed class ThinScrollBar : Control
    {
        private readonly NoteEditor editor;
        private bool hover, dragging;
        private int grabOffset;
        internal ThinScrollBar(NoteEditor target)
        {
            editor = target; TabStop = false; Cursor = Cursors.Default;
            AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "正文滚动条";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            editor.ScrollMetricsChanged += Sync;
        }
        private void Sync(object sender, EventArgs e) { Visible = editor.Visible && editor.MaximumLine > 0; Invalidate(); }
        internal Rectangle ThumbBounds
        {
            get
            {
                int height = Math.Min(Height, Math.Max(Ui.S(18), (int)((long)Height * editor.PageLines / Math.Max(1, editor.TotalLines))));
                int top = editor.MaximumLine == 0 ? 0 : (int)((long)(Height - height) * editor.FirstLine / editor.MaximumLine);
                return new Rectangle(0, top, Width, height);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); var thumb = ThumbBounds;
            int width = Math.Min(Width, Ui.S(hover || dragging ? 6 : 4));
            thumb.X = (Width - width) / 2; thumb.Width = width;
            if (thumb.Height > 0 && width > 0) Ui.FillRound(e.Graphics, Ui.Darken(BackColor, hover || dragging ? 85 : 58), thumb, Math.Max(1, width / 2));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            var thumb = ThumbBounds;
            if (thumb.Contains(e.Location)) { dragging = true; grabOffset = e.Y - thumb.Top; Capture = true; }
            else editor.ScrollToLine(editor.FirstLine + (e.Y < thumb.Top ? -editor.PageLines : editor.PageLines));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!dragging) return;
            int travel = Height - ThumbBounds.Height;
            if (travel > 0) editor.ScrollToLine((int)Math.Round((double)(e.Y - grabOffset) * editor.MaximumLine / travel));
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) editor.ScrollMetricsChanged -= Sync; base.Dispose(disposing); }
    }
}
