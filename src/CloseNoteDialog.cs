using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinStickies
{
    internal enum CloseNoteChoice { Cancel, Delete, Archive }

    internal sealed class CloseNoteDialog : Form
    {
        internal CloseNoteChoice Choice { get; private set; }
        private readonly Button cancel;

        internal CloseNoteDialog(string noteTitle, Rectangle noteBounds, bool topmost)
        {
            Text = "关闭便签"; ShowIcon = false; Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog; AutoScaleMode = AutoScaleMode.None;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText; TopMost = topmost;
            ClientSize = new Size(Ui.S(282), Ui.S(128));
            var title = new Label { Text = "关闭“" + noteTitle + "”？",
                AutoSize = false, AutoEllipsis = true, UseMnemonic = false,
                Bounds = new Rectangle(Ui.S(64), Ui.S(17), Ui.S(202), Ui.S(21)) };
            var description = new Label { Text = "删除将移除内容；\r\n归档保留内容，可随时打开。",
                AutoSize = false, Bounds = new Rectangle(Ui.S(64), Ui.S(38), Ui.S(202), Ui.S(36)) };
            var delete = new Button { Text = "删除", FlatStyle = FlatStyle.System, UseVisualStyleBackColor = true,
                Bounds = new Rectangle(Ui.S(20), Ui.S(94), Ui.S(75), Ui.S(26)), TabIndex = 1 };
            var archive = new Button { Text = "归档", FlatStyle = FlatStyle.System, UseVisualStyleBackColor = true,
                Bounds = new Rectangle(Ui.S(103), Ui.S(94), Ui.S(75), Ui.S(26)), TabIndex = 2 };
            cancel = new Button { Text = "取消", FlatStyle = FlatStyle.System, UseVisualStyleBackColor = true,
                Bounds = new Rectangle(Ui.S(186), Ui.S(94), Ui.S(75), Ui.S(26)), TabIndex = 0 };
            delete.Click += delegate { Choose(CloseNoteChoice.Delete); };
            archive.Click += delegate { Choose(CloseNoteChoice.Archive); };
            cancel.Click += delegate { Choose(CloseNoteChoice.Cancel); };
            Controls.AddRange(new Control[] { title, description, delete, archive, cancel });
            AcceptButton = cancel; CancelButton = cancel;
            var area = Screen.FromRectangle(noteBounds).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(noteBounds.X + (noteBounds.Width - Width) / 2, area.Right - Width)),
                Math.Max(area.Top, Math.Min(noteBounds.Y + (noteBounds.Height - Height) / 2, area.Bottom - Height)));
        }

        private void Choose(CloseNoteChoice choice)
        { Choice = choice; DialogResult = choice == CloseNoteChoice.Cancel ? DialogResult.Cancel : DialogResult.OK; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.FillRectangle(SystemBrushes.Control, 0, Ui.S(82), ClientSize.Width, ClientSize.Height - Ui.S(82));
            e.Graphics.DrawIcon(SystemIcons.Question, new Rectangle(Ui.S(18), Ui.S(27), Ui.S(32), Ui.S(32)));
        }

        protected override void OnShown(EventArgs e)
        { base.OnShown(e); cancel.Select(); }
    }
}
