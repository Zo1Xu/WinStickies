using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinStickies
{
    internal static class PriorityMenu
    {
        internal static void AddItems(ToolStripItemCollection items, Note note, Action<int> change)
        {
            for (int i = 0; i <= 3; i++)
            {
                int value = i;
                var item = new ToolStripMenuItem("P" + value) { Checked = note.Priority == value };
                item.Click += delegate { change(value); };
                items.Add(item);
            }
        }

        internal static ToolStripMenuItem Create(Note note, Action<int> change)
        {
            var menu = new ToolStripMenuItem(Strings.Priority);
            AddItems(menu.DropDownItems, note, change);
            return menu;
        }
    }

    internal sealed class PriorityButton : Button
    {
        private readonly Note note;
        private readonly Action<int> change;
        private bool hover;
        private static readonly Color[] inks = {
            Color.FromArgb(157, 44, 44), Color.FromArgb(140, 75, 17),
            Color.FromArgb(40, 87, 136), Color.FromArgb(86, 91, 88)
        };
        private static readonly Color[] fills = {
            Color.FromArgb(252, 220, 217), Color.FromArgb(252, 233, 203),
            Color.FromArgb(221, 234, 247), Color.FromArgb(230, 233, 228)
        };

        internal PriorityButton(Note item, Action<int> onChange)
        {
            note = item; change = onChange;
            Font = Ui.Font(8, true); Cursor = Cursors.Hand; TabStop = true;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleDescription = Strings.PriorityTooltip;
            RefreshPriority();
        }

        internal void RefreshPriority()
        {
            Text = "P" + note.Priority;
            AccessibleName = Strings.Priority + ": " + Text;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var menu = new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
            PriorityMenu.AddItems(menu.Items, note, change);
            menu.Closed += delegate { menu.BeginInvoke((Action)delegate { menu.Dispose(); }); };
            menu.Show(this, new Point(0, Height));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int priority = Note.NormalizePriority(note.Priority);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Ui.FillRound(e.Graphics, hover ? Ui.Darken(fills[priority], 10) : fills[priority], bounds, Ui.S(5));
            Ui.Text(e.Graphics, Text, Font, inks[priority], bounds,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(2, 2, Width - 5, Height - 5));
        }
    }
}
