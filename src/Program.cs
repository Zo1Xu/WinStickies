using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinStickies
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.MatchShellDpi();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Ui.Initialize();
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            int dataArg = Array.IndexOf(args, "--data-dir");
            if (dataArg >= 0 && dataArg + 1 < args.Length) folder = Path.GetFullPath(args[dataArg + 1]);
            if (args.Contains("--self-test")) return SelfTests.Run(folder);
            string identity = InstanceIdentity(folder);
            uint activate = Native.RegisterWindowMessage("WinStickies.Activate." + identity);
            bool first;
            using (var mutex = new Mutex(true, "Local\\WinStickies." + identity, out first))
            {
                // Ask the running instance to acknowledge the launch through its tray icon.
                // Relaunching still never opens a menu, creates a note or loads the library.
                if (!first)
                {
                    Native.PostMessage(new IntPtr(0xffff), activate, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                try { Application.Run(new StickyContext(folder, activate)); return 0; }
                catch (Exception ex)
                {
                    try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "error.log"), ex.ToString()); } catch { }
                    MessageBox.Show("WinStickies 无法启动。\n\n" + ex.Message + "\n\n数据位置：" + folder, "WinStickies", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        internal static string InstanceIdentity(string folder)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    Path.GetFullPath(Path.Combine(folder, "notes.json")).ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
        }
    }
}
