using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinStickies
{
    internal static class Ui
    {
        internal static float Scale = 1;
        internal static readonly Color Ink = Color.FromArgb(45, 48, 43), Muted = Color.FromArgb(109, 112, 103),
            Background = Color.FromArgb(250, 250, 247), Sidebar = Color.FromArgb(243, 244, 238),
            Line = Color.FromArgb(226, 228, 219), Accent = Color.FromArgb(71, 94, 67);
        internal static readonly Color[] Papers = { Color.FromArgb(255, 240, 177), Color.FromArgb(222, 237, 210),
            Color.FromArgb(218, 234, 244), Color.FromArgb(242, 218, 222), Color.FromArgb(231, 223, 243), Color.FromArgb(239, 235, 225) };
        internal static readonly string[] ColorNames = { "奶油黄", "鼠尾草绿", "雾蓝", "浅桃粉", "丁香紫", "暖灰" };
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        internal static void Initialize() { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; }
        internal static int S(float value) { return (int)Math.Round(value * Scale); }
        internal static Font Font(float size, bool bold = false)
        {
            string key = size + ":" + bold;
            if (!Fonts.ContainsKey(key)) Fonts[key] = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            return Fonts[key];
        }
        internal static Color Darken(Color c, int amount) { return Color.FromArgb(Math.Max(0, c.R - amount), Math.Max(0, c.G - amount), Math.Max(0, c.B - amount)); }
        internal static string ModeName(NoteMode mode) { return mode == NoteMode.Topmost ? "始终置顶" : mode == NoteMode.Desktop ? "桌面附着" : "普通悬浮"; }
        internal static string ModeIcon(NoteMode mode) { return mode == NoteMode.Topmost ? "pin" : mode == NoteMode.Desktop ? "desktop" : "float"; }
        internal static string ModeDescription(NoteMode mode)
        {
            return mode == NoteMode.Topmost ? "一直显示在其他应用窗口上方" : mode == NoteMode.Desktop ? "留在桌面层，其他应用可以覆盖" : "自由移动，会被其他应用窗口遮挡";
        }
        internal static GraphicsPath Round(Rectangle rect, int radius)
        {
            var path = new GraphicsPath(); int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d <= 0) { path.AddRectangle(rect); return path; }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void FillRound(Graphics g, Color color, Rectangle rect, int radius)
        { using (var path = Round(rect, radius)) using (var brush = new SolidBrush(color)) g.FillPath(brush, path); }
        internal static void Text(Graphics g, string text, Font font, Color color, Rectangle rect, TextFormatFlags flags = TextFormatFlags.Left)
        { TextRenderer.DrawText(g, text, font, rect, color, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix); }
        internal static void Icon(Graphics g, string name, Color color, Rectangle bounds)
        {
            var saved = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            using (var p = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (name)
                {
                    case "plus": g.DrawLine(p, 12, 5, 12, 19); g.DrawLine(p, 5, 12, 19, 12); break;
                    case "up": g.DrawLines(p, new[] { new PointF(6, 14), new PointF(12, 8), new PointF(18, 14) }); break;
                    case "down": g.DrawLines(p, new[] { new PointF(6, 10), new PointF(12, 16), new PointF(18, 10) }); break;
                    case "close": g.DrawLine(p, 6, 6, 18, 18); g.DrawLine(p, 18, 6, 6, 18); break;
                    case "min": g.DrawLine(p, 5, 12, 19, 12); break;
                    case "dots": using (var b = new SolidBrush(color)) for (int i = 0; i < 3; i++) g.FillEllipse(b, 5 + i * 6, 11, 2, 2); break;
                    case "pin": g.DrawPolygon(p, new[] { new PointF(8, 4), new PointF(16, 4), new PointF(15, 11), new PointF(18, 15), new PointF(6, 15), new PointF(9, 11) }); g.DrawLine(p, 12, 15, 12, 21); break;
                    case "desktop": g.DrawRectangle(p, 3, 4, 18, 13); g.DrawLine(p, 12, 17, 12, 21); g.DrawLine(p, 8, 21, 16, 21); break;
                    case "float": g.DrawRectangle(p, 7, 4, 14, 13); g.DrawLines(p, new[] { new PointF(17, 20), new PointF(3, 20), new PointF(3, 8) }); break;
                    case "grid": g.DrawRectangle(p, 4, 4, 6, 6); g.DrawRectangle(p, 14, 4, 6, 6); g.DrawRectangle(p, 4, 14, 6, 6); g.DrawRectangle(p, 14, 14, 6, 6); break;
                    case "archive": g.DrawRectangle(p, 3, 4, 18, 5); g.DrawRectangle(p, 5, 9, 14, 12); g.DrawLine(p, 9, 13, 15, 13); break;
                    case "search": g.DrawEllipse(p, 4, 3, 12, 12); g.DrawLine(p, 14, 14, 20, 20); break;
                    case "check": g.DrawLines(p, new[] { new PointF(5, 12), new PointF(10, 17), new PointF(20, 7) }); break;
                    case "note": g.DrawLines(p, new[] { new PointF(4, 3), new PointF(20, 3), new PointF(20, 15), new PointF(14, 21), new PointF(4, 21), new PointF(4, 3) }); g.DrawLines(p, new[] { new PointF(14, 21), new PointF(14, 15), new PointF(20, 15) }); g.DrawLine(p, 8, 8, 16, 8); break;
                    case "arrow": g.DrawLine(p, 5, 12, 19, 12); g.DrawLines(p, new[] { new PointF(14, 7), new PointF(19, 12), new PointF(14, 17) }); break;
                }
            }
            g.Restore(saved);
        }
        internal static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                FillRound(g, Papers[0], new Rectangle(2, 2, 28, 28), 7);
                Icon(g, "note", Accent, new Rectangle(5, 5, 22, 22));
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }

    internal sealed class ActionButton : Button
    {
        internal string Glyph;
        internal bool Primary, Selected, Quiet;
        internal bool AlignLeft;
        internal string TrailingText;
        private bool hover, pressed;
        internal ActionButton(string text, string glyph, EventHandler click)
        {
            Text = text; Glyph = glyph; Font = Ui.Font(9.5f); Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            if (click != null) Click += click;
            AccessibleName = text; Size = new Size(Ui.S(100), Ui.S(36)); TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary ? Ui.Accent : Selected ? Color.FromArgb(225, 231, 218) : Quiet ? BackColor : Color.White;
            if (hover || pressed) fill = Ui.Darken(fill, pressed ? 17 : 9);
            Ui.FillRound(e.Graphics, fill, new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(7));
            Color ink = Primary ? Color.White : Ui.Ink;
            if (!Enabled) ink = Ui.Muted;
            int icon = Ui.S(17), gap = Ui.S(8);
            int textWidth = TextRenderer.MeasureText(Text, Font).Width;
            int all = string.IsNullOrEmpty(Glyph) ? textWidth : string.IsNullOrEmpty(Text) ? icon : textWidth + icon + gap;
            int x = AlignLeft ? Ui.S(12) : Math.Max(Ui.S(4), (Width - all) / 2);
            if (!string.IsNullOrEmpty(Glyph)) { Ui.Icon(e.Graphics, Glyph, ink, new Rectangle(x, (Height - icon) / 2, icon, icon)); x += icon + gap; }
            if (!string.IsNullOrEmpty(Text)) Ui.Text(e.Graphics, Text, Font, ink, new Rectangle(x, 0, Width - x - Ui.S(5), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (!string.IsNullOrEmpty(TrailingText)) Ui.Text(e.Graphics, TrailingText, Ui.Font(9), Ui.Muted, new Rectangle(Width - Ui.S(36), 0, Ui.S(24), Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues) { using (var p = new Pen(Ui.Accent, Ui.S(1))) using (var path = Ui.Round(new Rectangle(2, 2, Width - 5, Height - 5), Ui.S(6))) e.Graphics.DrawPath(p, path); }
        }
    }
}
