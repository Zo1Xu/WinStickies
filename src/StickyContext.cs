using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WinStickies
{
    internal sealed class StickyContext : ApplicationContext
    {
        internal readonly Notebook Book;
        private readonly NoteStore store;
        private readonly Dictionary<string, NoteForm> windows = new Dictionary<string, NoteForm>();
        private readonly Timer saveTimer = new Timer { Interval = 500 };
        private readonly Timer shellTimer = new Timer { Interval = 2000 };
        private readonly Timer cleanupTimer = new Timer { Interval = 30 * 60 * 1000 };
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip trayMenu;
        private readonly Timer trayClickTimer = new Timer { Interval = SystemInformation.DoubleClickTime + 20 };
        private readonly MessageSink sink;
        private LibraryForm library;
        private bool exiting, dirty;
        internal string LastError { get; private set; }
        internal bool IsDirty { get { return dirty; } }
        internal LibraryForm Library { get { return library; } }
        internal ContextMenuStrip TrayMenu { get { return trayMenu; } }
        internal bool IsShellMonitorRunning { get { return shellTimer.Enabled; } }
        internal int ActiveWindowCount { get { return windows.Values.Count(w => !w.IsDisposed && w.IsHandleCreated); } }

        internal StickyContext(string folder, uint activateMessage)
        {
            store = new NoteStore(folder); Book = store.Load();
            int removedSamples = store.MigrateTrayStartup(Book);
            tray = new NotifyIcon { Icon = Ui.AppIcon, Text = Strings.TrayTitle, Visible = true };
            trayMenu = new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
            trayMenu.Opening += delegate { PrepareTrayMenu(); };
            trayMenu.Closed += delegate
            {
                if (exiting || trayMenu.IsDisposed || !trayMenu.IsHandleCreated) return;
                // A menu click is dispatched after Closed. Release note-specific items
                // on the next UI turn, after the clicked action has finished.
                trayMenu.BeginInvoke((Action)delegate
                {
                    if (!exiting && !trayMenu.IsDisposed && !trayMenu.Visible) PrepareTrayMenu(false);
                });
            };
            tray.ContextMenuStrip = trayMenu;
            trayClickTimer.Tick += delegate { trayClickTimer.Stop(); ShowTrayMenu(); };
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                trayClickTimer.Stop();
                if (e.Button == MouseButtons.Left) trayClickTimer.Start();
            };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
            {
                trayClickTimer.Stop();
                if (e.Button == MouseButtons.Left) { trayMenu.Close(); NewNote(); }
            };
            // A repeated executable launch shows a notification, not the tray menu.
            sink = new MessageSink(activateMessage, delegate { ShowStartupNotice(true); }, RepairShell, delegate { SaveNow(); });
            foreach (var note in Book.Notes.Where(n => !n.Archived)) CreateWindow(note);
            saveTimer.Tick += delegate
            {
                if (windows.Values.Any(w => !w.IsDisposed && w.IsInteracting)) return;
                saveTimer.Stop(); SaveNow(); if (library != null && library.Visible) library.RefreshNotes();
            };
            shellTimer.Tick += delegate { RepairShell(); }; UpdatePresence(); PrepareTrayMenu(false);
            cleanupTimer.Tick += delegate { if (!dirty && !windows.Values.Any(w => !w.IsDisposed && w.IsInteracting)) store.CleanupRuntimeFiles(); };
            cleanupTimer.Start();
            if (!string.IsNullOrEmpty(store.LoadWarning)) Report(store.LoadWarning);
            if (store.IsNew) { dirty = true; SaveNow(); }
            if (string.IsNullOrEmpty(LastError))
                ShowStartupNotice(false, removedSamples > 0 ? "\n" + removedSamples + Strings.SamplesCleanedSuffix : "");
        }

        private void UpdatePresence()
        {
            int active = Book.Notes.Count(n => !n.Archived);
            string tooltip = "WinStickies · " + active + Strings.NoteCountSeparator + (Book.Notes.Count - active) + Strings.TrayArchivedSuffix;
            // .NET Framework NotifyIcon limits tooltip text to 63 characters.
            tray.Text = tooltip.Length <= 63 ? tooltip : tooltip.Substring(0, 62) + "…";
            shellTimer.Enabled = active > 0;
        }
        private void Notify(string message, string title = "WinStickies", ToolTipIcon icon = ToolTipIcon.None)
        {
            tray.BalloonTipTitle = title; tray.BalloonTipText = message; tray.BalloonTipIcon = icon; tray.ShowBalloonTip(3500);
        }
        internal void ShowStartupNotice(bool alreadyRunning, string extra = "")
        {
            if (exiting) return;
            // Keep recovery/save problems visible instead of replacing them with success.
            if (!string.IsNullOrEmpty(LastError)) { Notify(LastError); return; }
            Notify(Strings.StartupBody + extra,
                alreadyRunning ? Strings.AlreadyRunning : Strings.StartupSuccess, ToolTipIcon.Info);
        }
        internal void PrepareTrayMenu(bool includeNotes = true)
        {
            foreach (ToolStripItem item in trayMenu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
            trayMenu.Items.Clear();
            int active = Book.Notes.Count(n => !n.Archived);
            int archivedCount = Book.Notes.Count - active;
            trayMenu.Items.Add(new ToolStripMenuItem("WinStickies · " + active + Strings.NoteCountSeparator + archivedCount + Strings.ArchivedCountSuffix) { Enabled = false });
            trayMenu.Items.Add(Strings.NewNote, null, delegate { NewNote(); });
            var notes = new ToolStripMenuItem(Strings.MyNotes) { Enabled = active > 0 };
            if (includeNotes) foreach (var note in Book.OrderedNotes.Where(n => !n.Archived))
            {
                string id = note.Id;
                string title = note.Title.Length > 26 ? note.Title.Substring(0, 26) + "…" : note.Title;
                var entry = new ToolStripMenuItem(title.Replace("&", "&&") + "  · P" + note.Priority + (note.Collapsed ? Strings.CollapsedSuffix : ""));
                entry.DropDownItems.Add(PriorityMenu.Create(note, value => SetNotePriority(id, value)));
                entry.DropDownItems.Add(Strings.ViewNote, null, delegate { WindowFor(id).Reveal(); });
                entry.DropDownItems.Add(note.Collapsed ? Strings.ExpandNote : Strings.CollapseNote, null,
                    delegate { var form = WindowFor(id); form.SetCollapsed(!form.Note.Collapsed); });
                entry.DropDownItems.Add(new ToolStripSeparator());
                foreach (NoteMode value in Enum.GetValues(typeof(NoteMode)))
                {
                    NoteMode mode = value;
                    var option = new ToolStripMenuItem(Ui.ModeName(mode)) { Checked = note.Mode == mode };
                    option.Click += delegate { WindowFor(id).ChangeMode(mode); }; entry.DropDownItems.Add(option);
                }
                entry.DropDownItems.Add(new ToolStripSeparator());
                entry.DropDownItems.Add(Strings.ArchiveNote, null, delegate { ArchiveNote(id); });
                entry.DropDownItems.Add(Strings.DeleteNoteMenu, null, delegate { DeleteNote(id); });
                notes.DropDownItems.Add(entry);
            }
            trayMenu.Items.Add(notes);
            var archived = new ToolStripMenuItem(Strings.Archived) { Enabled = archivedCount > 0 };
            if (includeNotes) foreach (var note in Book.OrderedNotes.Where(n => n.Archived))
            {
                string id = note.Id;
                string title = note.Title.Length > 26 ? note.Title.Substring(0, 26) + "…" : note.Title;
                var entry = new ToolStripMenuItem(title.Replace("&", "&&") + "  · P" + note.Priority);
                entry.DropDownItems.Add(PriorityMenu.Create(note, value => SetNotePriority(id, value)));
                entry.DropDownItems.Add(Strings.Reopen, null, delegate { RestoreNote(id); });
                entry.DropDownItems.Add(new ToolStripSeparator());
                entry.DropDownItems.Add(Strings.DeleteNoteMenu, null, delegate { DeleteNote(id); });
                archived.DropDownItems.Add(entry);
            }
            trayMenu.Items.Add(archived);
            // Do not retain note/sort submenus while the tray is closed.
            if (includeNotes) trayMenu.Items.Add(BuildSortMenu());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem(Strings.CollapseAllTray, null, delegate { SetAllCollapsed(true); }) { Enabled = active > 0 });
            trayMenu.Items.Add(new ToolStripMenuItem(Strings.ExpandAll, null, delegate { SetAllCollapsed(false); }) { Enabled = active > 0 });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(Strings.OverviewMenu, null, delegate { ShowLibrary(); });
            trayMenu.Items.Add(Strings.Exit, null, delegate { Quit(); });
        }
        internal void ShowTrayMenu()
        {
            if (exiting) return;
            trayClickTimer.Stop();
            trayMenu.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
        }

        private NoteForm CreateWindow(Note note)
        {
            if (note.Archived) throw new InvalidOperationException(Strings.ReopenFirst);
            var form = new NoteForm(note);
            form.Changed += delegate { ScheduleSave(); };
            form.NewRequested += delegate { NewNote(); };
            form.LibraryRequested += delegate { ShowLibrary(); };
            form.ArchiveRequested += delegate { ArchiveNote(note.Id); };
            form.DeleteRequested += delegate { DeleteNote(note.Id); };
            form.CloseRequested += ChooseCloseNote;
            form.Error += Report;
            windows[note.Id] = form;
            form.Show(); return form;
        }
        internal NoteForm WindowFor(string id)
        {
            var note = Book.Notes.First(n => n.Id == id);
            if (note.Archived) throw new InvalidOperationException(Strings.ReopenFirst);
            NoteForm form;
            if (windows.TryGetValue(id, out form) && !form.IsDisposed && form.IsHandleCreated) return form;
            return CreateWindow(note);
        }
        internal void NewNote()
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int offset = (Book.Notes.Count % 7) * Ui.S(26);
            var note = new Note { ColorIndex = Book.Notes.Count % Ui.Papers.Length,
                ExpandedWidth = Ui.S(310), ExpandedHeight = Ui.S(300),
                X = area.X + (area.Width - Ui.S(310)) / 2 + offset,
                Y = area.Y + (area.Height - Ui.S(300)) / 2 + offset };
            Book.Notes.Add(note); var form = CreateWindow(note); form.Reveal(); ScheduleSave();
            UpdatePresence();
            if (library != null) library.RefreshNotes();
        }
        internal void SetAllCollapsed(bool collapsed)
        {
            foreach (var note in Book.Notes.Where(n => !n.Archived).ToArray()) WindowFor(note.Id).SetCollapsed(collapsed);
            SaveNow(); if (library != null) library.RefreshNotes();
        }
        internal void SetNotePriority(string id, int value)
        {
            var note = Book.Notes.FirstOrDefault(n => n.Id == id);
            if (note == null) return;
            value = Note.NormalizePriority(value);
            if (note.Priority == value) return;
            NoteForm form;
            if (windows.TryGetValue(id, out form) && !form.IsDisposed) form.SetPriority(value);
            else { note.Priority = value; note.ModifiedUtc = DateTime.UtcNow; ScheduleSave(); }
        }
        internal void SetPrioritySort(bool enabled)
        {
            if (Book.SortByPriority == enabled) return;
            Book.SortByPriority = enabled; ScheduleSave();
            if (library != null) library.RefreshNotes();
        }
        internal ToolStripMenuItem BuildSortMenu()
        {
            var menu = new ToolStripMenuItem(Strings.SortNotes);
            menu.DropDownItems.Add(new ToolStripMenuItem(Strings.DefaultOrder, null, delegate { SetPrioritySort(false); }) { Checked = !Book.SortByPriority });
            menu.DropDownItems.Add(new ToolStripMenuItem(Strings.PriorityOrder, null, delegate { SetPrioritySort(true); }) { Checked = Book.SortByPriority });
            return menu;
        }
        internal void ArchiveNote(string id)
        {
            var note = Book.Notes.FirstOrDefault(n => n.Id == id);
            if (note == null || note.Archived) return;
            NoteForm form;
            if (windows.TryGetValue(id, out form) && !form.IsDisposed) form.CaptureGeometry();
            note.Archived = true; ScheduleSave();
            // Commit the current text and geometry before removing its window.
            if (!SaveNow())
            {
                note.Archived = false;
                Report(Strings.ArchiveFailed);
                return;
            }
            CloseNoteWindow(id);
            UpdatePresence(); if (library != null) library.RefreshNotes();
        }
        internal void RestoreNote(string id)
        {
            var note = Book.Notes.FirstOrDefault(n => n.Id == id);
            if (note == null || !note.Archived) return;
            note.Archived = false; ScheduleSave();
            if (!SaveNow())
            {
                note.Archived = true;
                Report(Strings.RestoreFailed);
                return;
            }
            var form = CreateWindow(note);
            if (note.Mode == NoteMode.Desktop) Notify(Strings.RestoredToDesktop);
            else { Native.SetForegroundWindow(form.Handle); if (!note.Collapsed) form.Editor.Focus(); }
            UpdatePresence(); if (library != null) library.RefreshNotes();
        }
        private void CloseNoteWindow(string id)
        {
            NoteForm form;
            if (!windows.TryGetValue(id, out form)) return;
            windows.Remove(id);
            if (!form.IsDisposed) { form.AllowClose = true; form.Close(); form.Dispose(); }
        }
        internal ContextMenuStrip BuildArchivedMenu(string id)
        {
            var menu = new ContextMenuStrip { Font = Ui.Font(9.5f), ShowImageMargin = false };
            var note = Book.Notes.First(n => n.Id == id);
            menu.Items.Add(PriorityMenu.Create(note, value => SetNotePriority(id, value)));
            menu.Items.Add(Strings.Reopen, null, delegate { RestoreNote(id); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.DeleteThisNote, null, delegate { DeleteNote(id); });
            menu.Closed += delegate { menu.BeginInvoke((Action)delegate { menu.Dispose(); }); };
            return menu;
        }
        private void ChooseCloseNote(NoteForm form)
        {
            CloseNoteChoice choice;
            using (var dialog = new CloseNoteDialog(form.Note.Title, Native.Bounds(form.Handle), form.Note.Mode == NoteMode.Topmost))
            {
                // A desktop-attached note is an Explorer child. Use our own hidden
                // top-level window as the modal owner, never the Explorer desktop.
                dialog.ShowDialog(form.Note.Mode == NoteMode.Desktop ? (IWin32Window)sink : form);
                choice = dialog.Choice;
            }
            if (choice == CloseNoteChoice.Delete) DeleteNote(form.Note.Id, false);
            else if (choice == CloseNoteChoice.Archive) ArchiveNote(form.Note.Id);
        }
        private void DeleteNote(string id, bool confirm = true)
        {
            var note = Book.Notes.FirstOrDefault(n => n.Id == id); if (note == null) return;
            string title = note.Title; if (title.Length > 40) title = title.Substring(0, 40) + "…";
            if (confirm)
                using (var dialog = new CloseNoteDialog(title, note.VisibleBounds(NoteForm.HeaderHeight), note.Mode == NoteMode.Topmost, true))
                {
                    // Own the translated buttons instead of inheriting the OS language.
                    if (dialog.ShowDialog(sink) != DialogResult.OK || dialog.Choice != CloseNoteChoice.Delete) return;
                }
            int index = Book.Notes.IndexOf(note);
            Book.Notes.RemoveAt(index); ScheduleSave();
            if (!SaveNow())
            {
                Book.Notes.Insert(index, note);
                Report(Strings.DeleteFailed);
                return;
            }
            CloseNoteWindow(id);
            UpdatePresence(); if (library != null) library.RefreshNotes();
        }
        internal void ShowLibrary()
        {
            if (library == null || library.IsDisposed)
            {
                library = new LibraryForm(this);
                library.FormClosed += delegate { library = null; };
            }
            library.RefreshNotes(); library.Show(); library.WindowState = FormWindowState.Normal;
            library.Activate(); Native.SetForegroundWindow(library.Handle);
        }
        private void ScheduleSave()
        {
            dirty = true; saveTimer.Stop(); saveTimer.Interval = 500; saveTimer.Start();
            if (library != null) library.UpdateStatus();
        }
        internal bool SaveNow()
        {
            if (!dirty) return true;
            try
            {
                store.Save(Book); dirty = false; LastError = null;
                foreach (var window in windows.Values.Where(w => !w.IsDisposed)) window.SetSaveStatus(true);
                if (library != null) library.UpdateStatus(); return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                LastError = Strings.SaveFailed;
                foreach (var window in windows.Values.Where(w => !w.IsDisposed)) window.SetSaveStatus(false);
                if (library != null) library.UpdateStatus();
                // Retry without throwing away edits or reporting them as saved.
                saveTimer.Interval = 2000; saveTimer.Start(); return false;
            }
        }
        internal void Report(string message)
        {
            if (LastError == message) return;
            LastError = message;
            if (library != null) library.UpdateStatus();
            Notify(message);
        }
        internal void RepairShell()
        {
            if (exiting || !Book.Notes.Any(n => !n.Archived)) return;
            if (windows.Values.Any(w => !w.IsDisposed && w.IsInteracting)) return;
            IntPtr desktop = Native.DesktopHost();
            foreach (var note in Book.Notes.Where(n => !n.Archived).ToArray())
            {
                NoteForm form;
                if (!windows.TryGetValue(note.Id, out form) || form.IsDisposed || !form.IsHandleCreated)
                {
                    if (desktop == IntPtr.Zero && note.Mode == NoteMode.Desktop) continue;
                    if (form != null && !form.IsDisposed) form.Dispose();
                    Native.MatchShellDpi(); CreateWindow(note); continue;
                }
                if (note.Mode == NoteMode.Desktop && desktop != IntPtr.Zero && (form.DesktopParent != desktop || Native.GetParent(form.Handle) != desktop))
                    form.ApplySavedMode();
                var reachable = Placement.KeepReachable(note.VisibleBounds(NoteForm.HeaderHeight), Screen.AllScreens.Select(s => s.WorkingArea), NoteForm.HeaderHeight);
                if (reachable.Location != new Point(note.X, note.Y))
                { Native.Place(form.Handle, reachable, form.DesktopParent); form.CaptureGeometry(); ScheduleSave(); }
            }
        }
        internal void Quit()
        {
            if (!SaveNow()) { Report(Strings.QuitFailed); return; }
            exiting = true; shellTimer.Stop(); saveTimer.Stop(); cleanupTimer.Stop();
            foreach (var form in windows.Values.ToArray()) if (!form.IsDisposed) { form.AllowClose = true; form.Close(); form.Dispose(); }
            if (library != null) { var window = library; window.Close(); window.Dispose(); }
            trayClickTimer.Stop(); trayClickTimer.Dispose(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose();
            store.CleanupRuntimeFiles();
            sink.Dispose(); shellTimer.Dispose(); saveTimer.Dispose(); cleanupTimer.Dispose(); ExitThread();
        }
        protected override void ExitThreadCore() { if (!exiting) { dirty = true; SaveNow(); tray.Visible = false; tray.Dispose(); } base.ExitThreadCore(); }
    }

    internal sealed class MessageSink : NativeWindow, IDisposable
    {
        private readonly uint activation, taskbar;
        private readonly Action onActivate, onShell, onShutdown;
        internal MessageSink(uint message, Action activate, Action shell, Action shutdown)
        {
            activation = message; taskbar = Native.RegisterWindowMessage("TaskbarCreated");
            onActivate = activate; onShell = shell; onShutdown = shutdown;
            CreateHandle(new CreateParams { Caption = "WinStickies.MessageSink", X = -32000, Y = -32000, Width = 1, Height = 1, ExStyle = 0x80 });
        }
        protected override void WndProc(ref Message m)
        {
            if ((uint)m.Msg == activation) onActivate();
            else if ((uint)m.Msg == taskbar || m.Msg == 0x007E) onShell();
            else if (m.Msg == 0x0011 || m.Msg == 0x0016) onShutdown();
            base.WndProc(ref m);
        }
        public void Dispose() { DestroyHandle(); }
    }
}
