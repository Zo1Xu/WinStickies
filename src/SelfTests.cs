using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WinStickies
{
    internal static class SelfTests
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        internal static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            var forms = new List<NoteForm>();
            StickyContext app = null;
            int exitCode = 0;
            try
            {
                string runFolder = Path.Combine(folder, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                var store = new NoteStore(runFolder); var book = store.Load();
                Check(store.IsNew && book.Notes.Count == 0, "first run starts with a new notebook");
                for (int i = 0; i < 5; i++)
                {
                    var note = new Note { X = Ui.S(70 + i * 55), Y = Ui.S(70 + i * 52), ExpandedWidth = Ui.S(280 + i * 13),
                        ExpandedHeight = Ui.S(240 + i * 17), Content = "测试 " + i + "\r\n中文、emoji 🌿 and multiline\r\n" + new string('x', 200), ColorIndex = i };
                    book.Notes.Add(note);
                }
                var original = book.Notes.Select(n => n.VisibleBounds(NoteForm.HeaderHeight)).ToArray();
                foreach (var note in book.Notes)
                {
                    var form = new NoteForm(note); string error = null; form.Error += message => error = message;
                    forms.Add(form); form.Show(); Application.DoEvents();
                    Check(error == null, "floating window initializes: " + note.Title + (error == null ? "" : " / " + error));
                    form.SetCollapsed(true); Application.DoEvents();
                    Check(Native.IsWindowVisible(form.Handle) && !Native.IsIconic(form.Handle), "collapsed title remains a visible, non-minimized HWND: " + note.Title);
                    Check(Native.Bounds(form.Handle).Height == NoteForm.HeaderHeight, "collapsed height is exactly the title bar: " + note.Title);
                }
                Check(forms.Select(f => f.Handle).Distinct().Count() == 5, "five collapsed notes retain five distinct windows");
                for (int i = 0; i < forms.Count; i++)
                {
                    var form = forms[i]; var moved = Native.Bounds(form.Handle); moved.Offset(17, 23);
                    Native.Place(form.Handle, moved, IntPtr.Zero); form.CaptureGeometry();
                    Check(form.Note.ExpandedWidth == original[i].Width && form.Note.ExpandedHeight == original[i].Height, "moving collapsed note preserves its expanded dimensions: " + i);
                }
                store.Save(book); var restored = new NoteStore(runFolder).Load();
                Check(restored.Notes.Count == 5 && restored.Notes.All(n => n.Collapsed), "restart restores all five collapsed states");
                Check(restored.Notes[0].Content == book.Notes[0].Content, "Unicode and multiline content round-trip losslessly");
                for (int i = 0; i < forms.Count; i++)
                {
                    forms[i].SetCollapsed(false); Application.DoEvents();
                    var expected = original[i]; expected.Offset(17, 23);
                    Check(Native.Bounds(forms[i].Handle) == expected, "expand restores independent position and size: " + i);
                }
                var target = forms[0]; var before = Native.Bounds(target.Handle);
                Check(target.ChangeMode(NoteMode.Topmost), "switch floating to topmost succeeds");
                Check((Native.GetStyle(target.Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0, "topmost mode sets the actual native topmost flag");
                Check(target.ChangeMode(NoteMode.Floating), "switch topmost to floating succeeds");
                Check((Native.GetStyle(target.Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0, "floating mode clears the native topmost flag");
                IntPtr desktop = Native.DesktopHost();
                results.Add("INFO: Shell HWND=" + Native.GetShellWindow() + ", desktop host=" + desktop);
                if (desktop == IntPtr.Zero)
                {
                    Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
                    {
                        var cls = new System.Text.StringBuilder(128); Native.GetClassName(hwnd, cls, cls.Capacity);
                        if (cls.ToString() == "Progman" || cls.ToString() == "WorkerW")
                        {
                            results.Add("INFO: Shell candidate " + hwnd + " " + cls);
                            Native.EnumChildWindows(hwnd, delegate(IntPtr child, IntPtr arg) { var name = new System.Text.StringBuilder(128); Native.GetClassName(child, name, name.Capacity); results.Add("INFO: child " + child + " " + name); return true; }, IntPtr.Zero);
                        }
                        return true;
                    }, IntPtr.Zero);
                }
                if (desktop != IntPtr.Zero)
                {
                Check(true, "Explorer desktop host was found");
                Check(target.ChangeMode(NoteMode.Desktop), "switch floating to desktop succeeds");
                Check(Native.GetParent(target.Handle) == desktop && (Native.GetStyle(target.Handle, Native.GWL_STYLE) & Native.WS_CHILD) != 0, "desktop mode really parents the HWND to Explorer");
                Check((Native.GetStyle(target.Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0, "desktop attachment is not topmost");
                Check(Native.Bounds(target.Handle) == before, "attaching preserves screen coordinates and size");
                target.SetCollapsed(true); Application.DoEvents();
                Check(!Native.IsIconic(target.Handle) && Native.IsWindowVisible(target.Handle) && Native.GetParent(target.Handle) == desktop, "desktop collapse retains a visible desktop child");
                var desktopMove = Native.Bounds(target.Handle); desktopMove.Offset(31, 19);
                Native.Place(target.Handle, desktopMove, desktop); target.CaptureGeometry(); target.SetCollapsed(false);
                var afterMove = before; afterMove.Offset(31, 19);
                Check(Native.Bounds(target.Handle) == afterMove, "desktop collapsed drag uses screen coordinates and restores its size");
                Check(target.ChangeMode(NoteMode.Topmost), "switch desktop directly to topmost succeeds");
                Check((Native.GetStyle(target.Handle, Native.GWL_STYLE) & Native.WS_CHILD) == 0 && Native.GetParent(target.Handle) == IntPtr.Zero, "detaching restores a true top-level window");
                Check(Native.Bounds(target.Handle) == afterMove, "detaching preserves position and size");
                }
                else results.Add("SKIP: actual desktop attachment requires an interactive Explorer desktop.");
                target.ChangeMode(NoteMode.Floating);
                var cycleBounds = Native.Bounds(target.Handle);
                for (int i = 0; i < 10; i++) { target.SetCollapsed(true); target.SetCollapsed(false); }
                Check(Native.Bounds(target.Handle) == cycleBounds, "ten collapse/expand cycles produce no geometry drift");
                store.Save(book);
                Check(File.Exists(store.FilePath + ".bak"), "atomic saving keeps a previous-version backup");
                File.WriteAllText(store.FilePath, "broken json");
                var recovery = new NoteStore(runFolder); var recovered = recovery.Load();
                Check(recovered.Notes.Count == 5 && recovery.LoadWarning != null, "damaged data recovers the backup with a warning");
                Check(Directory.GetFiles(runFolder, "*.damaged-*").Length == 1, "damaged original is preserved before recovery");
                var emptyFolder = Path.Combine(runFolder, "empty"); var emptyStore = new NoteStore(emptyFolder);
                emptyStore.Save(new Notebook());
                Check(new NoteStore(emptyFolder).Load().Notes.Count == 0, "an intentionally empty notebook does not recreate samples");
                var legacyFolder = Path.Combine(runFolder, "legacy-upgrade");
                var legacyStore = new NoteStore(legacyFolder);
                var legacyBook = new Notebook { StartupRevision = 0 };
                for (int i = 0; i < 5; i++) legacyBook.Notes.Add(new Note { Content = LegacySamples.Content[i], ColorIndex = i });
                legacyBook.Notes[2].Content += "\r\n用户自己写下的想法";
                legacyBook.Notes.Add(new Note { Content = "今天，慢慢来\r\n这是用户自己的正文。" });
                string editedId = legacyBook.Notes[2].Id;
                legacyStore.Save(legacyBook);
                var upgradeBook = legacyStore.Load();
                Check(legacyStore.MigrateTrayStartup(upgradeBook) == 4, "upgrade removes only unchanged original demo content");
                Check(upgradeBook.Notes.Count == 2 && upgradeBook.Notes.Any(n => n.Id == editedId) && upgradeBook.Notes.Any(n => n.Content.Contains("自己的正文")), "upgrade preserves edited demos and user notes with matching titles");
                Check(File.Exists(legacyStore.MigrationBackupPath), "upgrade creates a permanent backup before removing demos");
                using (var stream = File.OpenRead(legacyStore.MigrationBackupPath))
                {
                    var snapshot = (Notebook)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Notebook)).ReadObject(stream);
                    Check(snapshot.Notes.Count == 6 && snapshot.Notes.Any(n => n.Id == editedId) && snapshot.StartupRevision == 1, "upgrade snapshot contains every original note and can be restored without re-cleanup");
                }
                var afterUpgrade = new NoteStore(legacyFolder).Load();
                Check(afterUpgrade.StartupRevision == 1 && afterUpgrade.Notes.Count == 2, "upgrade completion is persisted");
                afterUpgrade.Notes.Add(new Note { Content = LegacySamples.Content[0] });
                Check(legacyStore.MigrateTrayStartup(afterUpgrade) == 0 && afterUpgrade.Notes.Count == 3, "migration is one-time and never cleans matching new user content");
                var leftMonitor = new Rectangle(-1920, 0, 1920, 1080); var primary = new Rectangle(0, 0, 1920, 1080);
                var negative = new Rectangle(-1500, 80, 310, 300);
                Check(Placement.KeepReachable(negative, new[] { leftMonitor, primary }, 38) == negative, "valid negative monitor coordinates are retained");
                Check(Placement.KeepReachable(negative, new[] { primary }, 38).X >= 0, "disconnected-monitor notes are brought back into reach");
                using (var bitmap = new Bitmap(target.Width, target.Height)) { target.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "note-preview.png")); }
                string smokeFolder = Path.Combine(runFolder, "app-smoke");
                string instanceIdentity = Program.InstanceIdentity(smokeFolder);
                app = new StickyContext(smokeFolder, Native.RegisterWindowMessage("WinStickies.Activate." + instanceIdentity));
                Application.DoEvents();
                Check(app.Book.Notes.Count == 0 && app.Library == null, "first launch creates no notes and does not instantiate a library window");
                Check(!app.IsShellMonitorRunning, "empty tray-only application does not poll the desktop");
                var notification = (NotifyIcon)typeof(StickyContext).GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(app);
                Check(notification.Visible && notification.BalloonTipTitle == "WinStickies 启动成功" && notification.BalloonTipText.Contains("双击托盘图标新建便签"),
                    "successful first launch shows a tray startup notification with usage guidance");
                int relaunchSignals = 0;
                using (var relaunchListener = new MessageSink(Native.RegisterWindowMessage("WinStickies.Activate." + instanceIdentity),
                    delegate { relaunchSignals++; }, delegate { }, delegate { }))
                using (var instanceMutex = new System.Threading.Mutex(true, "Local\\WinStickies." + instanceIdentity))
                {
                    try
                    {
                        var start = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath, "--data-dir \"" + smokeFolder + "\"")
                            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
                        using (var duplicate = System.Diagnostics.Process.Start(start))
                        {
                            bool finished = duplicate.WaitForExit(5000);
                            if (!finished) duplicate.Kill();
                            Check(finished && duplicate.ExitCode == 0, "second executable launch exits without starting another instance");
                        }
                        Application.DoEvents();
                        Check(relaunchSignals == 1 && notification.BalloonTipTitle == "WinStickies 已在运行" &&
                            !app.TrayMenu.Visible && app.Library == null && app.Book.Notes.Count == 0,
                            "second launch triggers one running-instance notification without opening a menu, library or note");
                    }
                    finally { instanceMutex.ReleaseMutex(); }
                }
                app.PrepareTrayMenu();
                Check(!app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "我的便签").Enabled, "empty tray menu disables note-specific actions");
                string[] testTitles = { "快速记录", "今天要做", "灵感草稿", "稍后处理", "随手记下" };
                for (int i = 0; i < 5; i++)
                {
                    app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "新建便签").PerformClick();
                    app.WindowFor(app.Book.Notes.Last().Id).Editor.Text = testTitles[i] + "\r\n\r\n" + (i == 2 ? "还没成形的想法。" : "这是一张用户主动创建的便签。");
                }
                Check(app.Book.Notes.Count == 5 && app.Library == null, "tray new-note action creates only the requested notes without loading the library");
                Check(app.IsShellMonitorRunning, "desktop recovery monitor starts when notes exist");
                app.PrepareTrayMenu();
                var noteEntries = app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "我的便签");
                Check(noteEntries.DropDownItems.Count == 5, "tray note list reflects all user-created notes");
                var firstEntry = (ToolStripMenuItem)noteEntries.DropDownItems[0];
                firstEntry.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "始终置顶").PerformClick();
                Check(app.Book.Notes[0].Mode == NoteMode.Topmost && app.Library == null, "tray changes individual note mode without opening a library");
                firstEntry.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == "普通悬浮").PerformClick();
                app.ShowTrayMenu(); Application.DoEvents();
                using (var bitmap = new Bitmap(app.TrayMenu.Width, app.TrayMenu.Height))
                { app.TrayMenu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "tray-menu-preview.png")); }
                app.TrayMenu.Close();
                app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "全部收起（保留标题栏）").PerformClick();
                Check(app.Book.Notes.All(n => n.Collapsed && Native.IsWindowVisible(app.WindowFor(n.Id).Handle)) && app.Library == null, "tray collapses all notes into visible independent title bars without a library");
                app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "全部展开").PerformClick();
                Check(app.Book.Notes.All(n => !n.Collapsed), "tray expands all notes");
                app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "便签总览…").PerformClick();
                Check(app.Library != null && app.Library.Visible, "library is created only when explicitly requested");
                using (var bitmap = new Bitmap(app.Library.Width, app.Library.Height))
                { app.Library.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "library-preview.png")); }
                var appBounds = app.Book.Notes.Select(n => n.VisibleBounds(NoteForm.HeaderHeight)).ToArray();
                var collapse = app.Library.Controls.OfType<ActionButton>().Single(b => b.Text == "全部收起");
                collapse.PerformClick(); Application.DoEvents();
                Check(app.Book.Notes.All(n => n.Collapsed) && app.Book.Notes.All(n => Native.IsWindowVisible(app.WindowFor(n.Id).Handle)), "library collapse-all button keeps all five notes visible");
                int stripWidth = app.Book.Notes.Max(n => n.ExpandedWidth);
                using (var bitmap = new Bitmap(stripWidth, (NoteForm.HeaderHeight + Ui.S(14)) * 5))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Ui.Background);
                    for (int i = 0; i < 5; i++)
                    {
                        var window = app.WindowFor(app.Book.Notes[i].Id);
                        using (var strip = new Bitmap(window.Width, window.Height))
                        { window.DrawToBitmap(strip, new Rectangle(Point.Empty, strip.Size)); g.DrawImageUnscaled(strip, 0, i * (NoteForm.HeaderHeight + Ui.S(14))); }
                    }
                    bitmap.Save(Path.Combine(folder, "collapsed-preview.png"));
                }
                app.Library.Controls.OfType<ActionButton>().Single(b => b.Text == "全部展开").PerformClick(); Application.DoEvents();
                Check(app.Book.Notes.Select((n, i) => Native.Bounds(app.WindowFor(n.Id).Handle) == appBounds[i]).All(b => b), "library expand-all button restores every note's own bounds");
                var search = app.Library.Controls.OfType<TextBox>().Single(); search.Text = "还没成形"; Application.DoEvents();
                Check(app.Library.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<NoteCard>().Count() == 1, "library search filters matching note content");
                search.Text = "";
                var previousLibrary = app.Library; previousLibrary.Close(); Application.DoEvents();
                Check(app.Library == null && previousLibrary.IsDisposed, "closing optional library releases its window and controls");
                app.NewNote(); Check(app.Book.Notes.Count == 6, "new-note action creates an independent sixth window");
                var added = app.Book.Notes.Last(); app.WindowFor(added.Id).Editor.Text = "持久化验证\r\n自动保存正文";
                Check(app.IsDirty && added.Content.Contains("自动保存正文"), "editing updates model and marks pending save");
                app.WindowFor(added.Id).ChangeMode(NoteMode.Topmost); app.WindowFor(added.Id).SetCollapsed(true);
                Check(app.SaveNow(), "application flush succeeds");
                var savedNote = new NoteStore(Path.Combine(runFolder, "app-smoke")).Load().Notes.Last();
                Check(savedNote.Content == added.Content && savedNote.Collapsed && savedNote.Mode == NoteMode.Topmost, "application saves edited text, collapse state and display mode to disk");
                IntPtr oldHandle = app.WindowFor(added.Id).Handle;
                var lostBounds = added.VisibleBounds(NoteForm.HeaderHeight);
                Native.DestroyWindow(oldHandle); Application.DoEvents();
                app.RepairShell();
                var recreated = app.WindowFor(added.Id);
                Check(recreated.IsHandleCreated && Native.IsWindowVisible(recreated.Handle) && !Native.IsIconic(recreated.Handle), "lost native note HWND is recreated as a visible window");
                Check(recreated.Note.Content == savedNote.Content && Native.Bounds(recreated.Handle) == lostBounds && recreated.Note.Mode == NoteMode.Topmost, "window recreation preserves content, collapsed geometry and mode");
                app.SetAllCollapsed(true); app.SaveNow();
                app.Quit(); app = null;
                app = new StickyContext(Path.Combine(runFolder, "app-smoke"), Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                Application.DoEvents();
                Check(app.Book.Notes.Count == 6 && app.Book.Notes.All(n => n.Collapsed && Native.Bounds(app.WindowFor(n.Id).Handle).Height == NoteForm.HeaderHeight) && app.Library == null, "restart restores only existing title bars without launching a library");
                notification = (NotifyIcon)typeof(StickyContext).GetField("tray", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(app);
                Check(notification.BalloonTipTitle == "WinStickies 启动成功", "startup success notification also appears when reopening an existing notebook");
                app.Quit(); app = null;
                var untouchedFolder = Path.Combine(runFolder, "untouched-demo-upgrade");
                new NoteStore(untouchedFolder).Save(new Notebook { StartupRevision = 0, Notes = LegacySamples.Content.Select(s => new Note { Content = s }).ToList() });
                app = new StickyContext(untouchedFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                Check(app.Book.Notes.Count == 0 && app.Library == null && !app.IsShellMonitorRunning, "old five-demo installation upgrades to a quiet tray-only startup");
                app.Quit(); app = null;

                string archiveFolder = Path.Combine(runFolder, "archive-lifecycle");
                app = new StickyContext(archiveFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                var archiveBounds = new Dictionary<string, Rectangle>();
                var archiveSizes = new Dictionary<string, Size>();
                var archiveStates = new Dictionary<string, bool>();
                var archiveHandles = new List<IntPtr>();
                int modeCount = desktop == IntPtr.Zero ? 2 : 3;
                for (int i = 0; i < modeCount; i++)
                {
                    app.NewNote(); var note = app.Book.Notes.Last(); var window = app.WindowFor(note.Id);
                    note.ColorIndex = i;
                    window.Editor.Text = "归档测试 " + i + "\r\n立即保存最新输入：中文、emoji 🌿 & 多行正文";
                    Check(window.ChangeMode((NoteMode)i), "archive fixture sets native display mode: " + i);
                    if (i == 1) window.SetCollapsed(true);
                    archiveBounds[note.Id] = Native.Bounds(window.Handle);
                    archiveSizes[note.Id] = new Size(note.ExpandedWidth, note.ExpandedHeight);
                    archiveStates[note.Id] = note.Collapsed;
                    archiveHandles.Add(window.Handle);
                    using (var menu = window.BuildMenu(false, false))
                    {
                        menu.Show(window, new Point(0, NoteForm.HeaderHeight));
                        menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "归档便签").PerformClick();
                        menu.Close(); Application.DoEvents();
                        Check(window.IsDisposed && !Native.IsWindow(archiveHandles.Last()) && menu.IsDisposed,
                            "archive menu closes and disposes the native note window and menu safely: " + i);
                    }
                    var diskNote = new NoteStore(archiveFolder).Load().Notes.Single(n => n.Id == note.Id);
                    Check(diskNote.Archived && diskNote.Content == note.Content && diskNote.ColorIndex == note.ColorIndex &&
                        diskNote.Mode == note.Mode && diskNote.Collapsed == archiveStates[note.Id] &&
                        diskNote.VisibleBounds(NoteForm.HeaderHeight) == archiveBounds[note.Id] &&
                        new Size(diskNote.ExpandedWidth, diskNote.ExpandedHeight) == archiveSizes[note.Id],
                        "archive immediately persists latest text, color, mode, bounds and collapse state: " + i);
                }
                Check(app.ActiveWindowCount == 0 && !app.IsShellMonitorRunning && app.Library == null,
                    "archiving every note releases all note windows and stops desktop polling without opening a library");
                bool refusedArchivedWindow = false;
                try { app.WindowFor(app.Book.Notes[0].Id); } catch (InvalidOperationException) { refusedArchivedWindow = true; }
                Check(refusedArchivedWindow && app.ActiveWindowCount == 0, "window lookup cannot accidentally reopen archived notes");
                app.SetAllCollapsed(true); app.SetAllCollapsed(false); app.RepairShell();
                Check(app.ActiveWindowCount == 0 && app.Book.Notes.All(n => n.Archived && n.Collapsed == archiveStates[n.Id]),
                    "global collapse, expand and shell recovery leave archives hidden with their original collapse states");
                app.PrepareTrayMenu();
                var archiveEntries = app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "已归档");
                Check(archiveEntries.Enabled && archiveEntries.DropDownItems.Count == modeCount &&
                    !app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "我的便签").Enabled &&
                    !app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "全部展开").Enabled,
                    "tray separates archived notes and disables active-note actions when only archives remain");
                app.ShowLibrary();
                var archiveCards = app.Library.Controls.OfType<FlowLayoutPanel>().Single();
                Check(!archiveCards.Controls.OfType<NoteCard>().Any(), "default library view excludes archived notes");
                app.Library.Controls.OfType<ActionButton>().Single(b => b.Text == "已归档").PerformClick();
                var archiveSearch = app.Library.Controls.OfType<TextBox>().Single(); archiveSearch.Text = "归档测试 1";
                Check(archiveCards.Controls.OfType<NoteCard>().Count() == 1 && app.ActiveWindowCount == 0,
                    "archive search previews saved content without creating note windows");
                var archiveCard = archiveCards.Controls.OfType<NoteCard>().Single();
                using (var menu = app.BuildArchivedMenu(app.Book.Notes[1].Id))
                {
                    menu.Show(archiveCard, new Point(0, 0)); Application.DoEvents();
                    Check(app.ActiveWindowCount == 0, "opening an archived card menu does not reopen its note");
                    menu.Close(); Application.DoEvents();
                }
                archiveSearch.Text = "";
                using (var bitmap = new Bitmap(app.Library.Width, app.Library.Height))
                { app.Library.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "archive-preview.png")); }
                archiveCards.Controls.OfType<NoteCard>().First().Controls.OfType<ActionButton>().Single(b => b.AccessibleName == "重新打开便签").PerformClick();
                Check(!app.Book.Notes[0].Archived && app.ActiveWindowCount == 1 && app.IsShellMonitorRunning &&
                    Native.Bounds(app.WindowFor(app.Book.Notes[0].Id).Handle) == archiveBounds[app.Book.Notes[0].Id],
                    "library restore recreates one note at its original position and size");
                app.SetAllCollapsed(true); app.SetAllCollapsed(false); app.RepairShell();
                Check(app.ActiveWindowCount == 1 && app.Book.Notes.Skip(1).All(n => n.Archived && n.Collapsed == archiveStates[n.Id]),
                    "global actions and shell repair affect only active notes in a mixed notebook");
                app.ArchiveNote(app.Book.Notes[0].Id);
                app.Quit(); app = null;
                app = new StickyContext(archiveFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                Application.DoEvents();
                Check(app.Book.Notes.Count == modeCount && app.Book.Notes.All(n => n.Archived) && app.ActiveWindowCount == 0 &&
                    !app.IsShellMonitorRunning && app.Library == null,
                    "restart retains every archive while creating no note or library windows");
                for (int i = 0; i < modeCount; i++)
                {
                    var note = app.Book.Notes[i];
                    app.PrepareTrayMenu();
                    archiveEntries = app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "已归档");
                    var entry = (ToolStripMenuItem)archiveEntries.DropDownItems[0];
                    entry.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "重新打开").PerformClick();
                    Application.DoEvents(); // WinForms dispatches OnShown, which reapplies the saved native mode.
                    var window = app.WindowFor(note.Id);
                    Check(!note.Archived && Native.IsWindowVisible(window.Handle) && !Native.IsIconic(window.Handle) &&
                        Native.Bounds(window.Handle) == archiveBounds[note.Id] &&
                        new Size(note.ExpandedWidth, note.ExpandedHeight) == archiveSizes[note.Id] &&
                        note.Collapsed == archiveStates[note.Id] && window.Editor.Text == note.Content,
                        "tray restore after restart retains text, individual bounds and collapse state: " + i);
                    Check(note.Mode == (NoteMode)i && (i != 1 || (Native.GetStyle(window.Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0) &&
                        (i != 2 || Native.GetParent(window.Handle) == desktop), "archive restore reapplies the actual native window mode: " + i);
                    app.RestoreNote(note.Id);
                    Check(app.ActiveWindowCount == i + 1 && app.WindowFor(note.Id) == window && app.Library == null,
                        "repeated restore creates no duplicate note or library window: " + i);
                }
                Check(new NoteStore(archiveFolder).Load().Notes.All(n => !n.Archived), "restore persists removal of archive state immediately");
                app.Quit(); app = null;

                string failureFolder = Path.Combine(runFolder, "archive-save-failure");
                app = new StickyContext(failureFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                app.NewNote(); var failureNote = app.Book.Notes.Single(); var failureWindow = app.WindowFor(failureNote.Id);
                failureWindow.Editor.Text = "保存失败时保留正文";
                Check(app.SaveNow(), "save-failure fixture is persisted before locking its data file");
                string failureFile = Path.Combine(failureFolder, "notes.json");
                using (var lockedFile = new FileStream(failureFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    app.ArchiveNote(failureNote.Id);
                    Check(!failureNote.Archived && !failureWindow.IsDisposed && Native.IsWindowVisible(failureWindow.Handle) && app.IsDirty,
                        "failed archive save retains the live note and content instead of hiding it");
                }
                Check(app.SaveNow(), "archive save can recover after the file lock is released");
                app.ArchiveNote(failureNote.Id);
                using (var lockedFile = new FileStream(failureFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    app.RestoreNote(failureNote.Id);
                    Check(failureNote.Archived && app.ActiveWindowCount == 0 && app.IsDirty,
                        "failed restore save keeps the note archived without creating a window");
                }
                app.RestoreNote(failureNote.Id);
                Check(!failureNote.Archived && app.ActiveWindowCount == 1 && app.WindowFor(failureNote.Id).Editor.Text == "保存失败时保留正文",
                    "retry restores archived content after save failure is resolved");
                app.Quit(); app = null;
                File.WriteAllText(failureFile, File.ReadAllText(failureFile).Replace("\"Archived\":false,", ""));
                var oldFormat = new NoteStore(failureFolder).Load().Notes.Single();
                Check(!oldFormat.Archived && oldFormat.Content == "保存失败时保留正文", "pre-archive JSON loads as active notes without losing content");
                RunResizeAndDeleteTests(folder, runFolder, desktop);
                RunEditorTests(folder);
                RunTitleAndFontTests(folder, runFolder, desktop);
                RunFormattingTests(folder, runFolder, desktop);
                RunRuntimeCleanupTests(runFolder);
            }
            catch (Exception ex) { results.Add(ex.ToString()); exitCode = 1; }
            finally
            {
                if (app != null) app.Quit();
                foreach (var form in forms) if (!form.IsDisposed) { form.AllowClose = true; form.Close(); form.Dispose(); }
                results.Add(exitCode == 0 ? "ALL AVAILABLE TESTS PASSED (" + results.Count(s => s.StartsWith("PASS:")) + " assertions, " + results.Count(s => s.StartsWith("SKIP:")) + " skipped groups)." : "TESTS FAILED.");
                File.WriteAllLines(Path.Combine(folder, "test-results.txt"), results);
            }
            return exitCode;
        }

        // Exercise the real form mouse handlers without moving the user's mouse pointer.
        private static void NoteMouse(NoteForm window, string method, Point screen, int clicks)
        {
            Point client = window.PointToClient(screen);
            typeof(NoteForm).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(window, new object[] { new MouseEventArgs(MouseButtons.Left, clicks, client.X, client.Y, 0) });
        }
        private static void DragNote(NoteForm window, Point client, Point delta)
        {
            Point start = window.PointToScreen(client);
            Point finish = new Point(start.X + delta.X, start.Y + delta.Y);
            NoteMouse(window, "OnMouseDown", start, 1);
            NoteMouse(window, "OnMouseMove", finish, 0);
            NoteMouse(window, "OnMouseUp", finish, 1);
        }
        private static void NoteKey(NoteForm window, Keys keys)
        {
            var message = Message.Create(window.Handle, 0x0100, IntPtr.Zero, IntPtr.Zero);
            typeof(NoteForm).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(window, new object[] { message, keys });
        }
        private static void EditorKey(NoteForm window, Keys keys)
        {
            var message = Message.Create(window.Editor.Handle, 0x0100, IntPtr.Zero, IntPtr.Zero);
            typeof(NoteEditor).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(window.Editor, new object[] { message, keys });
        }
        private static void DoubleClickNote(NoteForm window, Rectangle area)
        { NoteMouse(window, "OnMouseDown", window.PointToScreen(new Point(area.Left + area.Width / 2, area.Top + area.Height / 2)), 2); }
        private static void SaveNotePreview(NoteForm window, string path)
        {
            // RichTextBox does not support DrawToBitmap. Capture this test window's
            // actual pixels while it is briefly raised above other test windows.
            bool topmost = window.TopMost;
            try
            {
                window.TopMost = true; window.BringToFront(); window.Refresh(); Application.DoEvents();
                Rectangle bounds = Native.Bounds(window.Handle);
                using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                { graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size); bitmap.Save(path); }
            }
            finally { window.TopMost = topmost; }
        }
        private static void ChooseNoteClose(NoteForm window, string action, string previewPath = null)
        {
            bool observed = false; Exception failure = null;
            using (var timer = new Timer { Interval = 30 })
            {
                timer.Tick += delegate
                {
                    var dialog = Application.OpenForms.OfType<CloseNoteDialog>().SingleOrDefault();
                    if (dialog == null) return;
                    timer.Stop(); observed = true;
                    try
                    {
                        var buttons = dialog.Controls.OfType<Button>().ToArray();
                        Check(dialog.Modal && buttons.Select(b => b.Text).OrderBy(t => t).SequenceEqual(new[] { "删除", "归档", "取消" }.OrderBy(t => t)) &&
                            ((Button)dialog.AcceptButton).Text == "取消" && ((Button)dialog.CancelButton).Text == "取消" && !window.IsDisposed,
                            "title-bar X opens a three-choice modal with cancel as the default: " + action + ", " + window.Note.Mode);
                        if (window.Note.Mode == NoteMode.Topmost)
                            Check((Native.GetStyle(dialog.Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0, "close dialog remains above its topmost note");
                        if (window.Note.Mode == NoteMode.Desktop)
                            Check(Native.GetParent(dialog.Handle) != Native.DesktopHost() && Native.GetParent(dialog.Handle) != window.Handle,
                                "desktop note close dialog is not owned by Explorer or its child note");
                        if (previewPath != null)
                            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                            { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(previewPath); }
                        if (action == "窗口关闭") dialog.Close();
                        else if (action == "Esc" || action == "Enter")
                            typeof(Form).GetMethod("ProcessDialogKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                .Invoke(dialog, new object[] { action == "Esc" ? Keys.Escape : Keys.Enter });
                        else buttons.Single(b => b.Text == action).PerformClick();
                    }
                    catch (Exception ex) { failure = ex; dialog.Close(); }
                };
                timer.Start();
                window.Controls.OfType<ActionButton>().Single(b => b.AccessibleName == "关闭便签").PerformClick();
                timer.Stop();
            }
            if (failure != null) throw failure;
            Check(observed && !Application.OpenForms.OfType<CloseNoteDialog>().Any(), "close choice dialog is disposed after selection: " + action);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr EditorMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        private static void ScrollMouse(ThinScrollBar bar, string method, Point point)
        {
            typeof(ThinScrollBar).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(bar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0) });
        }
        private static void RunEditorTests(string outputFolder)
        {
            using (var window = new NoteForm(new Note { X = Ui.S(100), Y = Ui.S(100), ExpandedWidth = Ui.S(310), ExpandedHeight = Ui.S(300) }))
            {
                window.Show(); Application.DoEvents(); var editor = window.Editor; var bar = window.ScrollBar;
                Check(!bar.Visible && editor.MaximumLine == 0 && (Native.GetStyle(editor.Handle, Native.GWL_STYLE) & 0x200000) == 0,
                    "empty note has neither a custom nor a native vertical scrollbar");
                editor.Text = "标题\r\n短正文"; Application.DoEvents();
                Check(!bar.Visible && editor.MaximumLine == 0, "short text that fits keeps the scrollbar hidden");
                SaveNotePreview(window, Path.Combine(outputFolder, "short-note-preview.png"));
                editor.Text = new string('想', 1500); Application.DoEvents();
                Check(editor.TotalLines > 1 && bar.Visible && bar.Width == Ui.S(8), "wrapped text overflow shows the narrow custom scrollbar");
                editor.Text = string.Join("\r\n", Enumerable.Range(1, 200).Select(i => "第 " + i + " 行：中文 🌿，记录想法。"));
                Application.DoEvents(); editor.Select(5, 4); string selected = editor.SelectedText;
                editor.ScrollToLine(editor.MaximumLine);
                Check(editor.FirstLine == editor.MaximumLine && editor.FirstLine > 0 && editor.SelectedText == selected,
                    "scrolling to the bottom retains text selection and reaches the final page");
                editor.ScrollToLine(0);
                EditorMessage(editor.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero); Application.DoEvents();
                Check(SystemInformation.MouseWheelScrollLines == 0 || editor.FirstLine > 0, "mouse wheel scrolls the native text even without a native scrollbar");
                editor.ScrollToLine(0);
                int grab = bar.ThumbBounds.Height / 2;
                ScrollMouse(bar, "OnMouseDown", new Point(2, grab));
                ScrollMouse(bar, "OnMouseMove", new Point(2, bar.Height - bar.ThumbBounds.Height + grab));
                ScrollMouse(bar, "OnMouseUp", new Point(2, bar.Height - 1));
                Check(editor.FirstLine == editor.MaximumLine && editor.SelectedText == selected, "dragging the thin thumb reaches the bottom without changing selection");
                ScrollMouse(bar, "OnMouseDown", new Point(2, 0)); ScrollMouse(bar, "OnMouseUp", new Point(2, 0));
                Check(editor.FirstLine < editor.MaximumLine, "clicking the scrollbar track scrolls a page");
                editor.ScrollToLine(0); editor.SelectionStart = editor.TextLength; editor.ScrollToCaret(); Application.DoEvents();
                Check(editor.FirstLine > 0 && editor.FirstLine == (int)EditorMessage(editor.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero) &&
                    bar.ThumbBounds.Top > 0 && editor.GetPositionFromCharIndex(editor.TextLength).Y < editor.ClientSize.Height,
                    "caret scrolling keeps the scrollbar synchronized with the native viewport and brings the caret into view");
                SaveNotePreview(window, Path.Combine(outputFolder, "long-note-preview.png"));
                editor.Text = string.Join("\r\n", Enumerable.Range(1, 8).Select(i => "第 " + i + " 行"));
                Native.Place(window.Handle, new Rectangle(window.Left, window.Top, Ui.S(310), Ui.S(180)), IntPtr.Zero); Application.DoEvents();
                Check(bar.Visible, "shrinking the note displays its scrollbar when text no longer fits");
                editor.ScrollToLine(editor.MaximumLine);
                Native.Place(window.Handle, new Rectangle(window.Left, window.Top, Ui.S(310), Ui.S(480)), IntPtr.Zero); Application.DoEvents();
                Check(!bar.Visible && editor.FirstLine == 0, "growing the note hides the scrollbar and returns fitting text to the top");
                Native.Place(window.Handle, new Rectangle(window.Left, window.Top, Ui.S(310), Ui.S(180)), IntPtr.Zero);
                Application.DoEvents(); string original = editor.Text; editor.SelectAll();
                EditorMessage(editor.Handle, 0x0102, new IntPtr('短'), IntPtr.Zero); Application.DoEvents(); // Native typing retains undo.
                Check(!bar.Visible && editor.MaximumLine == 0, "deleting overflow text hides the scrollbar immediately");
                editor.Undo(); Application.DoEvents();
                Check(editor.Text == original && bar.Visible, "native undo restores the content and overflow scrollbar");
                window.SetCollapsed(true); Application.DoEvents(); Check(!bar.Visible, "collapsed notes hide the body scrollbar");
                var bodySize = editor.Size;
                DragNote(window, new Point(window.Width - 1, window.Height - 1), new Point(Ui.S(60), Ui.S(20)));
                Check(editor.Size == bodySize, "resizing a collapsed strip avoids reflowing its hidden body");
                window.SetCollapsed(false); Application.DoEvents(); Check(bar.Visible, "expanding restores the correct body scrollbar state");

                int changes = 0, sizes = 0;
                window.Changed += delegate { changes++; }; window.SizeChanged += delegate { sizes++; };
                Rectangle before = Native.Bounds(window.Handle); Point start = window.PointToScreen(new Point(window.Width - 1, window.Height - 1));
                NoteMouse(window, "OnMouseDown", start, 1);
                for (int i = 1; i <= 100; i++) NoteMouse(window, "OnMouseMove", new Point(start.X + i, start.Y + i), 0);
                Check(sizes == 0 && changes == 0 && window.IsInteracting, "a burst of 100 resize inputs is coalesced without repeated layout or save notifications");
                System.Threading.Thread.Sleep(25); Application.DoEvents();
                Check(sizes == 1 && changes == 0, "resize timer applies one visible update while saving stays deferred");
                var finish = new Point(start.X + 123, start.Y + 111);
                NoteMouse(window, "OnMouseMove", finish, 0); NoteMouse(window, "OnMouseUp", finish, 1);
                Check(Native.Bounds(window.Handle).Size == new Size(before.Width + 123, before.Height + 111) && changes == 1 && !window.IsInteracting,
                    "mouse release flushes the final size exactly and requests one save");
                start = window.PointToScreen(new Point(window.Width - 1, window.Height - 1)); before = Native.Bounds(window.Handle);
                NoteMouse(window, "OnMouseDown", start, 1); NoteMouse(window, "OnMouseMove", new Point(start.X + 17, start.Y + 19), 0);
                window.Capture = false;
                Check(!window.IsInteracting && Native.Bounds(window.Handle).Size == new Size(before.Width + 17, before.Height + 19) && changes == 2,
                    "losing mouse capture flushes pending geometry and ends the resize cleanly");
                window.AllowClose = true; window.Close();
            }
        }
        private static void RunTitleAndFontTests(string outputFolder, string runFolder, IntPtr desktop)
        {
            string dataFolder = Path.Combine(runFolder, "title-and-font");
            StickyContext app = null;
            try
            {
                app = new StickyContext(dataFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                int modeCount = desktop == IntPtr.Zero ? 2 : 3;
                for (int mode = 0; mode < modeCount; mode++)
                {
                    app.NewNote(); var note = app.Book.Notes.Last(); var window = app.WindowFor(note.Id);
                    window.ChangeMode((NoteMode)mode);
                    string body = "正文首行 " + mode + "\r\n第二行正文保持不变";
                    window.Editor.Text = body;
                    body = window.Editor.Text;
                    Rectangle bounds = Native.Bounds(window.Handle);
                    NoteMouse(window, "OnMouseDown", window.PointToScreen(new Point(Ui.S(50), NoteForm.HeaderHeight / 2)), 2);
                    Check(window.TitleEditor.Visible && window.TitleEditor.Focused && !note.Collapsed && Native.Bounds(window.Handle) == bounds,
                        "double-click opens the inline title editor without collapsing or moving: " + mode);
                    NoteKey(window, Keys.Enter);
                    Check(string.IsNullOrEmpty(note.CustomTitle) && note.Title == "正文首行 " + mode,
                        "accepting an unchanged automatic title keeps first-line title behavior: " + mode);
                    NoteKey(window, Keys.F2);
                    window.TitleEditor.Text = "  独立标题 " + mode + " 🌿 & 提醒  ";
                    NoteKey(window, Keys.Control | Keys.A);
                    Check(window.TitleEditor.SelectionLength == window.TitleEditor.TextLength && window.Editor.Text == body,
                        "Ctrl+A selects the title rather than changing the body: " + mode);
                    NoteKey(window, Keys.Enter);
                    string title = "独立标题 " + mode + " 🌿 & 提醒";
                    Check(!window.TitleEditor.Visible && note.Title == title && note.Content == body && window.Text.StartsWith(title) && app.IsDirty,
                        "Enter finishes a trimmed Unicode title independently of body text: " + mode);
                    window.Editor.Text += "\r\n继续输入正文"; body = note.Content;
                    Check(note.Title == title, "editing the body preserves the custom title: " + mode);
                    NoteKey(window, Keys.F2); window.TitleEditor.Text = "应当撤销的标题";
                    NoteKey(window, Keys.Escape);
                    Check(note.Title == title && !window.TitleEditor.Visible && note.Content == body,
                        "Escape restores the previous title without affecting the body: " + mode);
                    NoteKey(window, Keys.F2); window.TitleEditor.Text = "失去焦点保存";
                    window.Editor.Focus();
                    Check(note.Title == "失去焦点保存" && !window.TitleEditor.Visible,
                        "leaving the inline field finishes the title edit: " + mode);
                    NoteKey(window, Keys.F2); window.TitleEditor.Text = "   "; NoteKey(window, Keys.Enter);
                    Check(note.CustomTitle == "" && note.Title == "正文首行 " + mode && note.Content == body,
                        "clearing the title restores the first nonempty body line: " + mode);
                    NoteKey(window, Keys.F2); window.TitleEditor.Text = title; NoteKey(window, Keys.Enter);
                    window.Editor.Select(1, 3); string selected = window.Editor.SelectedText;
                    using (var menu = window.BuildMenu(false, false))
                    {
                        var sizes = menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "正文字号");
                        Check(sizes.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Checked).Text.Contains("默认"),
                            "new notes mark the original body size as the default: " + mode);
                        sizes.DropDownItems.OfType<ToolStripMenuItem>().Single(i => i.Text == (18 + mode * 2) + " 磅" || mode == 2 && i.Text == "24 磅").PerformClick();
                    }
                    float expectedSize = mode == 2 ? 24 : 18 + mode * 2;
                    Application.DoEvents();
                    Check(note.FontSize == expectedSize && Math.Abs(window.Editor.Font.SizeInPoints * window.Editor.ZoomFactor - expectedSize) < 0.1f &&
                        window.Editor.SelectedText == selected && note.Content == body && Native.Bounds(window.Handle) == bounds,
                        "font menu changes this note's body size while preserving text, selection and geometry: " + mode);
                    using (var menu = window.BuildMenu(false, false))
                        Check(menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "正文字号").DropDownItems.OfType<ToolStripMenuItem>()
                            .Single(i => i.Checked).Text == expectedSize + " 磅", "font menu reflects the saved choice: " + mode);
                    window.SetCollapsed(true); bounds = Native.Bounds(window.Handle);
                    NoteMouse(window, "OnMouseDown", window.PointToScreen(new Point(Ui.S(50), window.Height / 2)), 2);
                    Check(window.TitleEditor.Visible && !window.Editor.Visible && note.Collapsed && Native.Bounds(window.Handle) == bounds,
                        "collapsed strip allows title editing without expanding: " + mode);
                    window.TitleEditor.Text = title + " · 收起"; NoteKey(window, Keys.Enter);
                    app.ArchiveNote(note.Id); app.RestoreNote(note.Id); window = app.WindowFor(note.Id);
                    Check(note.Title == title + " · 收起" && note.Content == body && note.FontSize == expectedSize &&
                        Math.Abs(window.Editor.Font.SizeInPoints * window.Editor.ZoomFactor - expectedSize) < 0.1f && note.Collapsed && Native.Bounds(window.Handle) == bounds && note.Mode == (NoteMode)mode,
                        "archive and restore preserve custom title, font size, body, geometry and mode: " + mode);
                }
                app.PrepareTrayMenu();
                Check(app.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "我的便签").DropDownItems
                    .OfType<ToolStripMenuItem>().All(i => i.Text.StartsWith("独立标题") && i.Text.Contains("&&")),
                    "tray uses custom titles and escapes ampersands");
                app.ShowLibrary(); var search = app.Library.Controls.OfType<TextBox>().Single();
                search.Text = "独立标题 0"; Application.DoEvents();
                Check(app.Library.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<NoteCard>().Count() == 1,
                    "library can search by a custom title absent from the body");
                app.Library.Close();
                app.SaveNow(); app.Quit(); app = null;
                app = new StickyContext(dataFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                Check(app.Book.Notes.Select((n, i) => n.Title == "独立标题 " + i + " 🌿 & 提醒 · 收起" && n.Collapsed &&
                    n.FontSize == (i == 2 ? 24 : 18 + i * 2) && Math.Abs(app.WindowFor(n.Id).Editor.Font.SizeInPoints * app.WindowFor(n.Id).Editor.ZoomFactor - n.FontSize) < 0.1f).All(v => v),
                    "restart restores every independent title and body font size");
                var first = app.Book.Notes[0]; var target = app.WindowFor(first.Id); target.SetCollapsed(false);
                target.SetFontSize(9); target.Editor.Text = string.Join("\r\n", Enumerable.Range(1, 5).Select(i => "第 " + i + " 行正文"));
                Application.DoEvents(); Check(!target.ScrollBar.Visible, "small-font short body fits without a scrollbar");
                target.SetFontSize(36); Application.DoEvents();
                Check(target.ScrollBar.Visible && target.Editor.MaximumLine > 0, "increasing font size updates wrapped lines and overflow scrollbar");
                SaveNotePreview(target, Path.Combine(outputFolder, "font-size-preview.png"));
                target.SetFontSize(9); Application.DoEvents();
                Check(!target.ScrollBar.Visible && target.Editor.FirstLine == 0, "reducing font size hides the scrollbar when the body fits");
                target.Editor.SelectAll(); string undoBody = target.Editor.Text;
                EditorMessage(target.Editor.Handle, 0x0102, new IntPtr('短'), IntPtr.Zero);
                target.SetFontSize(16); target.Editor.Undo(); Application.DoEvents();
                Check(target.Editor.Text == undoBody, "changing font size preserves native text undo history");
                target.SetCollapsed(true);
                DragNote(target, new Point(target.Width - 1, target.Height - 1), new Point(-Ui.S(1000), -Ui.S(1000)));
                NoteKey(target, Keys.F2);
                Check(target.TitleEditor.Bounds.Left >= target.Controls.OfType<ActionButton>().Single(b => b.AccessibleName == "展开便签").Right &&
                    target.TitleEditor.Right < target.Controls.OfType<ActionButton>().Single(b => b.AccessibleName == "显示模式").Left &&
                    target.TitleEditor.Top >= 0 && target.TitleEditor.Bottom <= target.Height,
                    "inline title stays between buttons inside the smallest collapsed strip");
                using (var bitmap = new Bitmap(target.Width, target.Height))
                { target.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(outputFolder, "title-edit-preview.png")); }
                target.TitleEditor.Text = "退出前最后输入";
                app.Quit(); app = null;
                Check(new NoteStore(dataFolder).Load().Notes[0].Title == "退出前最后输入", "quitting during title editing saves the latest input");

                var legacyStore = new NoteStore(Path.Combine(runFolder, "pre-title-font"));
                legacyStore.Save(new Notebook { Notes = new List<Note> { new Note { Content = "旧正文\r\n仍然保留" } } });
                string json = File.ReadAllText(legacyStore.FilePath).Replace("\"CustomTitle\":\"\",", "").Replace("\"FontSize\":11.25,", "");
                Check(!json.Contains("CustomTitle") && !json.Contains("FontSize"), "legacy fixture omits both new fields");
                File.WriteAllText(legacyStore.FilePath, json); var legacy = legacyStore.Load().Notes.Single();
                Check(legacy.FontSize == Note.DefaultFontSize && legacy.Title == "旧正文" && legacy.Content == "旧正文\r\n仍然保留",
                    "old notebooks retain the original font size and automatic title without losing text");
                foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                {
                    legacy.FontSize = invalid; legacy.Normalize();
                    Check(legacy.FontSize == Note.DefaultFontSize, "invalid stored font size falls back safely: " + invalid);
                }
                legacy.FontSize = 1000; legacy.Normalize(); Check(legacy.FontSize == 36, "oversized stored font is bounded");
            }
            finally { if (app != null) app.Quit(); }
        }
        private static void RunFormattingTests(string outputFolder, string runFolder, IntPtr desktop)
        {
            string folder = Path.Combine(runFolder, "formatting"); StickyContext app = null;
            try
            {
                app = new StickyContext(folder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                int modeCount = desktop == IntPtr.Zero ? 2 : 3;
                for (int mode = 0; mode < modeCount; mode++)
                {
                    app.NewNote(); var note = app.Book.Notes.Last(); var window = app.WindowFor(note.Id); var editor = window.Editor;
                    window.ChangeMode((NoteMode)mode);
                    Application.DoEvents();
                    editor.Text = "普通文字 加粗文字\n中文与 emoji 🌿 均保留";
                    editor.Select(0, 4);
                    Check(editor.SelectionFont != null && editor.SelectionFont.Name == Ui.Font(NoteEditor.BaseFontSize).Name,
                        "rich editing retains the note's Chinese UI font: " + mode);
                    string originalBody = editor.Text;
                    NoteKey(window, Keys.F2); window.TitleEditor.Text = "短标题"; NoteKey(window, Keys.Enter);
                    Rectangle bounds = Native.Bounds(window.Handle);
                    Check(window.TitleBlankBounds.Width >= Ui.S(18) && !window.TitleTextBounds.IntersectsWith(window.TitleBlankBounds),
                        "title text and an explicit blank collapse target never overlap: " + mode);
                    DoubleClickNote(window, window.TitleBlankBounds);
                    Check(note.Collapsed && !window.TitleEditor.Visible && Native.Bounds(window.Handle).Location == bounds.Location,
                        "double-clicking space beside a short title collapses without editing or moving: " + mode);
                    DoubleClickNote(window, window.TitleBlankBounds);
                    Check(!note.Collapsed && Native.Bounds(window.Handle) == bounds, "blank-area double-click expands to the original bounds: " + mode);
                    DoubleClickNote(window, window.TitleTextBounds);
                    Check(window.TitleEditor.Visible && !note.Collapsed, "only the rendered title text opens inline editing: " + mode);
                    window.TitleEditor.Text = new string('长', 80); NoteKey(window, Keys.Enter);
                    Check(window.TitleBlankBounds.Width >= Ui.S(18), "an ellipsized long title still leaves an empty double-click target: " + mode);
                    DoubleClickNote(window, window.TitleBlankBounds); DoubleClickNote(window, window.TitleTextBounds);
                    Check(note.Collapsed && window.TitleEditor.Visible, "long collapsed titles remain editable independently from the blank area: " + mode);
                    NoteKey(window, Keys.Escape); DoubleClickNote(window, window.TitleBlankBounds);
                    DragNote(window, new Point(window.TitleBlankBounds.Left + 3, NoteForm.HeaderHeight / 2), new Point(13, 17));
                    Check(new Point(note.X, note.Y) == new Point(bounds.X + 13, bounds.Y + 17) && !window.TitleEditor.Visible,
                        "blank title space continues to support dragging: " + mode);
                    window.SetCollapsed(true);
                    DragNote(window, new Point(window.Width - 1, window.Height - 1), new Point(-Ui.S(1000), -Ui.S(1000)));
                    Check(window.TitleBlankBounds.Width >= Ui.S(18) && window.TitleTextBounds.Width > 0 &&
                        window.TitleBlankBounds.Right < window.Controls.OfType<ActionButton>().Single(b => b.AccessibleName == "显示模式").Left,
                        "smallest strip reserves room for title, blank collapse area and buttons: " + mode);
                    DoubleClickNote(window, window.TitleBlankBounds);

                    editor.Focus(); editor.Select(5, 4); app.SaveNow();
                    EditorKey(window, Keys.Control | Keys.B);
                    Check(editor.SelectionFont != null && editor.SelectionFont.Bold && editor.SelectedText == "加粗文字" && note.Content == originalBody && app.IsDirty,
                        "Ctrl+B from the native editor bolds exactly the selected text and schedules saving: " + mode);
                    editor.Select(0, 4);
                    Check(editor.SelectionFont != null && !editor.SelectionFont.Bold, "unselected text stays regular: " + mode);
                    editor.Select(5, 4); EditorKey(window, Keys.Control | Keys.B);
                    Check(!editor.SelectionFont.Bold, "Ctrl+B toggles an entirely bold selection back to regular: " + mode);
                    EditorKey(window, Keys.Control | Keys.Z);
                    Check(editor.SelectionFont.Bold && note.ContentRtf == editor.Rtf, "undo restores bold formatting and updates saved rich content: " + mode);
                    EditorKey(window, Keys.Control | Keys.Y);
                    Check(!editor.SelectionFont.Bold && note.ContentRtf == editor.Rtf, "redo restores regular formatting and updates saved rich content: " + mode);
                    EditorKey(window, Keys.Control | Keys.B); editor.Select(0, 9);
                    EditorKey(window, Keys.Control | Keys.B);
                    Check(editor.SelectionFont != null && editor.SelectionFont.Bold, "Ctrl+B makes a mixed regular/bold selection uniformly bold: " + mode);
                    editor.Select(0, 4); EditorKey(window, Keys.Control | Keys.B);
                    editor.Select(5, 4); string selected = editor.SelectedText;
                    EditorKey(window, Keys.Control | Keys.Oemplus);
                    Check(note.FontSize == 12 && editor.SelectedText == selected && editor.SelectionFont.Bold,
                        "Ctrl+plus increases body size while retaining selection and bold: " + mode);
                    EditorKey(window, Keys.Control | Keys.Shift | Keys.Oemplus);
                    Check(note.FontSize == 14, "Ctrl+Shift+equals supports the main keyboard plus sign: " + mode);
                    EditorKey(window, Keys.Control | Keys.Add);
                    Check(note.FontSize == 16, "Ctrl+numpad plus increases body size: " + mode);
                    EditorKey(window, Keys.Control | Keys.OemMinus); EditorKey(window, Keys.Control | Keys.Subtract);
                    Check(note.FontSize == 12 && editor.SelectionFont.Bold && editor.SelectedText == selected,
                        "main keyboard and numpad minus reduce size without discarding formatting: " + mode);
                    window.SetFontSize(36); EditorKey(window, Keys.Control | Keys.Add);
                    Check(note.FontSize == 36, "font increase stops at the maximum");
                    window.SetFontSize(9); EditorKey(window, Keys.Control | Keys.Subtract);
                    Check(note.FontSize == 9, "font decrease stops at the minimum");
                    window.SetFontSize(16);
                    NoteKey(window, Keys.F2); window.TitleEditor.Text = "格式测试 " + mode;
                    NoteKey(window, Keys.Control | Keys.B); NoteKey(window, Keys.Control | Keys.Oemplus);
                    Check(note.FontSize == 16 && editor.SelectedText == selected, "body shortcuts do not change the body while editing a title: " + mode);
                    NoteKey(window, Keys.Enter); editor.Focus();
                    editor.Select(editor.TextLength, 0); EditorKey(window, Keys.Control | Keys.B);
                    bool typingBold = editor.SelectionFont.Bold;
                    EditorMessage(editor.Handle, 0x0102, new IntPtr('新'), IntPtr.Zero);
                    editor.Select(editor.TextLength - 1, 1);
                    Check(editor.SelectionFont.Bold == typingBold && note.Content.EndsWith("新"),
                        "Ctrl+B without a selection controls the next typed character: " + mode);
                    editor.Select(5, 4);
                    if (mode == 0)
                        SaveNotePreview(window, Path.Combine(outputFolder, "bold-text-preview.png"));
                    app.ArchiveNote(note.Id); app.RestoreNote(note.Id); window = app.WindowFor(note.Id); editor = window.Editor;
                    editor.Select(5, 4);
                    Check(editor.SelectionFont.Bold && Math.Abs(editor.SelectionFont.SizeInPoints * editor.ZoomFactor - 16) < 0.1f &&
                        note.FontSize == 16 && note.ContentRtf == new NoteStore(folder).Load().Notes.Last().ContentRtf,
                        "archive and restore keep rich text formatting and the independent body size: " + mode);
                    editor.Select(0, 4); Check(!editor.SelectionFont.Bold, "archive restore does not bold the rest of the note: " + mode);
                }
                app.SaveNow(); app.Quit(); app = null;
                app = new StickyContext(folder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                foreach (var note in app.Book.Notes)
                {
                    var editor = app.WindowFor(note.Id).Editor; editor.Select(5, 4);
                    Check(editor.SelectionFont.Bold && editor.Text.Contains("🌿") && note.FontSize == 16,
                        "restart preserves bold runs, Unicode content and font size: " + note.Mode);
                }
                using (var window = new NoteForm(new Note { Content = "纯文本旧便签\r\n保留换行", FontSize = 20 }))
                {
                    window.Show(); Application.DoEvents();
                    Check(window.Editor.Text.Replace("\r", "") == "纯文本旧便签\n保留换行" && !window.Editor.SelectionFont.Bold,
                        "legacy plain text loads normally without rich text data");
                    window.AllowClose = true; window.Close();
                }
                using (var window = new NoteForm(new Note { Content = "保留正文", ContentRtf = "invalid rtf" }))
                {
                    window.Show(); Check(window.Editor.Text == "保留正文", "invalid rich text falls back to the surviving plain content");
                    window.AllowClose = true; window.Close();
                }
                using (var window = new NoteForm(new Note { Content = "被旧版修改的正文", ContentRtf = app.Book.Notes[0].ContentRtf }))
                {
                    window.Show(); Check(window.Editor.Text == "被旧版修改的正文", "stale rich text never overwrites newer plain text from an older app version");
                    window.AllowClose = true; window.Close();
                }
            }
            finally { if (app != null) app.Quit(); }
        }
        private static void RunRuntimeCleanupTests(string runFolder)
        {
            string folder = Path.Combine(runFolder, "runtime-cleanup"); var store = new NoteStore(folder);
            var book = new Notebook { Notes = new List<Note> { new Note { Content = "用户正文必须保留" } } };
            for (int i = 0; i < 20; i++) store.Save(book);
            Check(!File.Exists(store.FilePath + ".tmp") && Directory.GetFiles(folder).Length == 2,
                "repeated runtime saves leave only the notebook and one rotating backup");
            File.Copy(store.FilePath, store.FilePath + ".tmp"); store.CleanupRuntimeFiles();
            Check(!File.Exists(store.FilePath + ".tmp"), "completed duplicate temporary save is cleaned automatically");
            File.WriteAllText(store.FilePath + ".tmp", "{partial"); store.CleanupRuntimeFiles();
            Check(!File.Exists(store.FilePath + ".tmp") && store.Load().Notes[0].Content == book.Notes[0].Content,
                "partial runtime temporary file is removed when a valid notebook survives");
            string pendingFolder = Path.Combine(runFolder, "pending-source"); var pendingStore = new NoteStore(pendingFolder);
            var pendingBook = new Notebook { Notes = new List<Note> { new Note { Content = "可能尚未保存的独有内容" } } }; pendingStore.Save(pendingBook);
            File.Copy(pendingStore.FilePath, store.FilePath + ".tmp"); store.CleanupRuntimeFiles();
            Check(File.Exists(store.FilePath + ".tmp") && store.Load().Notes[0].Content == book.Notes[0].Content,
                "a distinct valid pending save is retained without silently replacing the current notebook");
            using (var locked = new FileStream(store.FilePath + ".tmp", FileMode.Open, FileAccess.Read, FileShare.None)) store.CleanupRuntimeFiles();
            Check(File.Exists(store.FilePath + ".tmp"), "cleanup skips an in-use temporary file");
            store.Save(book); Check(!File.Exists(store.FilePath + ".tmp"), "successful retry consumes the one pending save file");
            File.Move(pendingStore.FilePath, pendingStore.FilePath + ".tmp");
            Check(pendingStore.Load().Notes[0].Content == pendingBook.Notes[0].Content && !File.Exists(pendingStore.FilePath + ".tmp") && File.Exists(pendingStore.FilePath),
                "interrupted first save recovers its valid temporary notebook before cleanup");
            string loneFolder = Path.Combine(runFolder, "partial-only"); Directory.CreateDirectory(loneFolder);
            var loneStore = new NoteStore(loneFolder); File.WriteAllText(loneStore.FilePath + ".tmp", "{only surviving partial bytes");
            loneStore.CleanupRuntimeFiles(); Check(File.Exists(loneStore.FilePath + ".tmp"), "cleanup keeps even partial data when no readable notebook or backup survives");
            bool partialRejected = false;
            try { loneStore.Load(); } catch (System.Runtime.Serialization.SerializationException) { partialRejected = true; }
            Check(partialRejected && File.Exists(loneStore.FilePath + ".tmp") && !File.Exists(loneStore.FilePath),
                "a sole partial save is not silently replaced by an empty notebook");
            string replacementFolder = Path.Combine(runFolder, "pending-replacement"); Directory.CreateDirectory(replacementFolder);
            var replacementStore = new NoteStore(replacementFolder);
            File.WriteAllText(replacementStore.FilePath, "broken main"); File.WriteAllText(replacementStore.FilePath + ".bak", "broken backup");
            File.Copy(pendingStore.FilePath, replacementStore.FilePath + ".tmp");
            Check(replacementStore.Load().Notes[0].Content == pendingBook.Notes[0].Content && !File.Exists(replacementStore.FilePath + ".tmp") &&
                Directory.GetFiles(replacementFolder, "notes.json.damaged-*").Length == 1,
                "valid pending save recovers when both committed copies are damaged and preserves the damaged original");
            File.WriteAllText(store.FilePath, "broken json"); store.Load(); store.Load();
            Check(Directory.GetFiles(folder, "notes.json.damaged-*").Length == 1, "repeated recovery does not create duplicate damaged-file snapshots");
            File.WriteAllText(Path.Combine(folder, "notes.json.damaged-duplicate"), "broken json");
            File.WriteAllText(Path.Combine(folder, "notes.json.damaged-distinct"), "different recoverable bytes");
            File.WriteAllText(Path.Combine(folder, "notes.before-tray-update-preserve.json"), "migration backup");
            File.WriteAllText(Path.Combine(folder, "personal.tmp"), "not an app runtime file");
            store.CleanupRuntimeFiles();
            Check(Directory.GetFiles(folder, "notes.json.damaged-*").Length == 2 &&
                File.Exists(Path.Combine(folder, "notes.before-tray-update-preserve.json")) && File.Exists(Path.Combine(folder, "personal.tmp")) && File.Exists(store.FilePath + ".bak"),
                "cleanup deduplicates snapshots while preserving distinct recovery data, migration backups and unrelated files");
            string log = Path.Combine(folder, "error.log"); File.WriteAllText(log, new string('x', 90000) + "latest error"); store.CleanupRuntimeFiles();
            Check(new FileInfo(log).Length == 65536 && File.ReadAllText(log).EndsWith("latest error"), "recent runtime error log is bounded to its last 64 KiB");
            File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-8)); store.CleanupRuntimeFiles();
            Check(!File.Exists(log), "runtime error logs older than seven days are cleaned automatically");
        }
        private static void RunResizeAndDeleteTests(string outputFolder, string runFolder, IntPtr desktop)
        {
            string dataFolder = Path.Combine(runFolder, "resize-and-delete");
            StickyContext app = null;
            var strips = new Dictionary<string, Rectangle>();
            var expandedSizes = new Dictionary<string, Size>();
            try
            {
                app = new StickyContext(dataFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                int modeCount = desktop == IntPtr.Zero ? 2 : 3;
                for (int mode = 0; mode < modeCount; mode++)
                {
                    app.NewNote(); var note = app.Book.Notes.Last(); var window = app.WindowFor(note.Id);
                    window.Editor.Text = Ui.ModeName((NoteMode)mode) + " · 自由调整\r\n展开与收起，各自记住大小。";
                    Application.DoEvents();
                    Check(window.ChangeMode((NoteMode)mode), "resize fixture sets native display mode: " + mode);
                    var initial = new Rectangle(Ui.S(130 + mode * 30), Ui.S(120 + mode * 25), Ui.S(340 + mode * 20), Ui.S(260 + mode * 20));
                    Native.Place(window.Handle, initial, window.DesktopParent); window.CaptureGeometry();
                    foreach (bool collapsed in new[] { false, true })
                    {
                        window.SetCollapsed(collapsed);
                        var baseline = Native.Bounds(window.Handle);
                        Size otherSize = collapsed ? new Size(note.ExpandedWidth, note.ExpandedHeight) : new Size(note.CollapsedWidth, note.CollapsedHeight);
                        int[] edges = { 1, 2, 4, 8, 5, 6, 9, 10 };
                        foreach (int edge in edges)
                        {
                            Native.Place(window.Handle, baseline, window.DesktopParent); window.CaptureGeometry();
                            Point start = new Point((edge & 1) != 0 ? 1 : (edge & 2) != 0 ? window.Width - 1 : window.Width / 2,
                                (edge & 4) != 0 ? 1 : (edge & 8) != 0 ? window.Height - 1 : window.Height / 2);
                            int dx = (edge & 1) != 0 ? -Ui.S(20) : (edge & 2) != 0 ? Ui.S(20) : 0;
                            int dy = (edge & 4) != 0 ? -Ui.S(14) : (edge & 8) != 0 ? Ui.S(14) : 0;
                            var expected = baseline;
                            if (dx < 0) expected.X += dx; if (dy < 0) expected.Y += dy;
                            expected.Width += Math.Abs(dx); expected.Height += Math.Abs(dy);
                            DragNote(window, start, new Point(dx, dy));
                            Check(Native.Bounds(window.Handle) == expected && !Native.IsIconic(window.Handle) &&
                                (collapsed ? new Size(note.ExpandedWidth, note.ExpandedHeight) : new Size(note.CollapsedWidth, note.CollapsedHeight)) == otherSize,
                                "mouse resize preserves the opposite edge and other state's dimensions: mode=" + mode + ", collapsed=" + collapsed + ", edge=" + edge);
                        }
                        DragNote(window, new Point(window.Width - 1, window.Height - 1), new Point(-10000, -10000));
                        Check(window.Width == (collapsed ? NoteForm.MinCollapsedWidth : NoteForm.MinNoteWidth) &&
                            window.Height == (collapsed ? NoteForm.MinCollapsedHeight : NoteForm.MinNoteHeight),
                            "resize stops at usable minimum dimensions: mode=" + mode + ", collapsed=" + collapsed);
                        var controls = window.Controls.OfType<ActionButton>().ToArray();
                        Check(controls.Length == 4 && controls.All(b => window.ClientRectangle.Contains(b.Bounds)) &&
                            controls.SelectMany((a, i) => controls.Skip(i + 1).Select(b => Rectangle.Intersect(a.Bounds, b.Bounds).IsEmpty)).All(b => b),
                            "title controls remain inside the smallest note without overlapping: mode=" + mode + ", collapsed=" + collapsed);
                        if (mode == 0)
                        {
                            using (var bitmap = new Bitmap(window.Width, window.Height))
                            { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(outputFolder, collapsed ? "small-strip-preview.png" : "small-note-preview.png")); }
                        }
                        Native.Place(window.Handle, baseline, window.DesktopParent); window.CaptureGeometry();
                    }
                    var customSize = new Size(Ui.S(410 + mode * 30), Ui.S(58 + mode * 12));
                    DragNote(window, new Point(window.Width - 1, window.Height - 1), new Point(customSize.Width - window.Width, customSize.Height - window.Height));
                    var beforeMove = Native.Bounds(window.Handle);
                    DragNote(window, new Point(Ui.S(50), window.Height / 2), new Point(Ui.S(17), Ui.S(23)));
                    var moved = beforeMove; moved.Offset(Ui.S(17), Ui.S(23));
                    Check(Native.Bounds(window.Handle) == moved && !window.Editor.Visible, "thicker collapsed strip remains draggable with hidden body: " + mode);
                    NoteMouse(window, "OnMouseDown", window.PointToScreen(new Point(Ui.S(50), window.Height / 2)), 2);
                    Check(note.Collapsed && window.TitleEditor.Visible && Native.Bounds(window.Handle) == moved,
                        "double-clicking a resized strip edits the title without changing geometry: " + mode);
                    NoteKey(window, Keys.Escape);
                    window.Controls.OfType<ActionButton>().Single(b => b.AccessibleName == "展开便签").PerformClick();
                    Check(!note.Collapsed && window.Editor.Visible && Native.Bounds(window.Handle).Size == initial.Size &&
                        Native.Bounds(window.Handle).Location == moved.Location, "fold button restores expanded size at its moved position: " + mode);
                    DragNote(window, new Point(window.Width - 1, window.Height - 1), new Point(Ui.S(31), Ui.S(27)));
                    expandedSizes[note.Id] = Native.Bounds(window.Handle).Size;
                    for (int cycle = 0; cycle < 5; cycle++) { window.SetCollapsed(true); window.SetCollapsed(false); }
                    window.SetCollapsed(true);
                    Check(Native.Bounds(window.Handle) == moved && new Size(note.ExpandedWidth, note.ExpandedHeight) == expandedSizes[note.Id],
                        "expanded and collapsed resize settings remain independent over repeated toggles: " + mode);
                    strips[note.Id] = moved;
                    app.ArchiveNote(note.Id); app.RestoreNote(note.Id); Application.DoEvents();
                    window = app.WindowFor(note.Id);
                    Check(Native.Bounds(window.Handle) == moved && new Size(note.ExpandedWidth, note.ExpandedHeight) == expandedSizes[note.Id] &&
                        (mode != 1 || (Native.GetStyle(window.Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0) &&
                        (mode != 2 || Native.GetParent(window.Handle) == desktop), "archive restore preserves both custom sizes and the actual native mode: " + mode);
                }
                using (var bitmap = new Bitmap(strips.Values.Max(r => r.Width), strips.Values.Sum(r => r.Height + Ui.S(14))))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Ui.Background); int y = 0;
                    foreach (var note in app.Book.Notes)
                    {
                        var window = app.WindowFor(note.Id);
                        using (var strip = new Bitmap(window.Width, window.Height))
                        { window.DrawToBitmap(strip, new Rectangle(Point.Empty, strip.Size)); graphics.DrawImageUnscaled(strip, 0, y); }
                        y += window.Height + Ui.S(14);
                    }
                    bitmap.Save(Path.Combine(outputFolder, "resized-strips-preview.png"));
                }
                app.SaveNow(); app.Quit(); app = null;
                app = new StickyContext(dataFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                Application.DoEvents();
                Check(app.Book.Notes.All(n => n.Collapsed && Native.Bounds(app.WindowFor(n.Id).Handle) == strips[n.Id] &&
                    new Size(n.ExpandedWidth, n.ExpandedHeight) == expandedSizes[n.Id]), "restart restores every note's independent expanded and collapsed sizes");
                app.RepairShell();
                Check(app.Book.Notes.All(n => Native.Bounds(app.WindowFor(n.Id).Handle) == strips[n.Id]), "desktop recovery preserves custom collapsed dimensions");
                foreach (var note in app.Book.Notes.ToArray())
                {
                    var window = app.WindowFor(note.Id); window.SetCollapsed(false);
                    var expandedBounds = Native.Bounds(window.Handle); string content = note.Content;
                    ChooseNoteClose(window, "取消", note.Mode == NoteMode.Floating ? Path.Combine(outputFolder, "close-dialog-preview.png") : null);
                    ChooseNoteClose(window, "窗口关闭");
                    Check(!window.IsDisposed && !note.Archived && !note.Collapsed && window.Enabled &&
                        Native.Bounds(window.Handle) == expandedBounds && note.Content == content,
                        "cancel button and dialog X leave the expanded note unchanged: " + note.Mode);
                    window.SetCollapsed(true);
                    ChooseNoteClose(window, "Esc"); ChooseNoteClose(window, "Enter");
                    Check(!window.IsDisposed && !note.Archived && note.Collapsed && window.Enabled &&
                        Native.Bounds(window.Handle) == strips[note.Id] && note.Content == content,
                        "Escape and default Enter cancel without changing the collapsed note: " + note.Mode);
                    ChooseNoteClose(window, "归档");
                    Check(window.IsDisposed && note.Archived && note.Content == content &&
                        new NoteStore(dataFolder).Load().Notes.Single(n => n.Id == note.Id).Archived,
                        "archive choice hides the note and persists its content and archive state: " + note.Mode);
                    app.RestoreNote(note.Id); Application.DoEvents();
                    Check(Native.Bounds(app.WindowFor(note.Id).Handle) == strips[note.Id] &&
                        new Size(note.ExpandedWidth, note.ExpandedHeight) == expandedSizes[note.Id],
                        "note archived through close dialog reopens with both independent sizes preserved: " + note.Mode);
                }
                var first = app.Book.Notes[0]; var firstWindow = app.WindowFor(first.Id);
                using (var locked = new FileStream(Path.Combine(dataFolder, "notes.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ChooseNoteClose(firstWindow, "删除");
                    Check(app.Book.Notes[0] == first && !firstWindow.IsDisposed && app.IsDirty, "failed confirmed deletion keeps the window, content and original list position");
                    ChooseNoteClose(firstWindow, "归档");
                    Check(!first.Archived && !firstWindow.IsDisposed && firstWindow.Enabled && app.IsDirty,
                        "failed archive from close dialog retains the active note");
                }
                app.SaveNow(); app.ShowLibrary();
                int removed = 0;
                foreach (var note in app.Book.Notes.ToArray())
                {
                    var window = app.WindowFor(note.Id); if (removed == 0) window.SetCollapsed(false);
                    IntPtr handle = window.Handle;
                    ChooseNoteClose(window, "删除");
                    app.RepairShell(); Application.DoEvents(); removed++;
                    Check(window.IsDisposed && !Native.IsWindow(handle) && !app.Book.Notes.Any(n => n.Id == note.Id) &&
                        !new NoteStore(dataFolder).Load().Notes.Any(n => n.Id == note.Id) && app.ActiveWindowCount == modeCount - removed &&
                        app.Library.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<NoteCard>().Count() == modeCount - removed,
                        "delete choice removes only the selected note and persists removal without a second dialog: " + note.Mode);
                }
                Check(!app.IsShellMonitorRunning, "confirming deletion of the last note stops desktop polling");
                app.Quit(); app = null;
                app = new StickyContext(dataFolder, Native.RegisterWindowMessage("WinStickies.Test." + Guid.NewGuid().ToString("N")));
                Check(app.Book.Notes.Count == 0 && app.ActiveWindowCount == 0, "confirmed deletions stay deleted after restart");
                var legacy = new Notebook { Notes = new List<Note> { new Note { Content = "旧版收起便签", Collapsed = true, ExpandedWidth = Ui.S(340), ExpandedHeight = Ui.S(270) } } };
                string legacyFolder = Path.Combine(runFolder, "pre-resize"); var legacyStore = new NoteStore(legacyFolder); legacyStore.Save(legacy);
                string json = File.ReadAllText(legacyStore.FilePath).Replace("\"CollapsedWidth\":0,", "").Replace("\"CollapsedHeight\":0,", "");
                File.WriteAllText(legacyStore.FilePath, json);
                var legacyNote = legacyStore.Load().Notes.Single();
                Check(legacyNote.VisibleBounds(NoteForm.HeaderHeight).Size == new Size(Ui.S(340), NoteForm.HeaderHeight),
                    "old notebooks without collapsed dimensions keep the original width and default title height");
                var thin = new Rectangle(60, 1050, 220, 30);
                Check(Placement.KeepReachable(thin, new[] { new Rectangle(0, 0, 1920, 1080) }, 38) == thin,
                    "short collapsed strip at the screen bottom does not jump during reachability checks");
            }
            finally { if (app != null) app.Quit(); }
        }
    }
}
