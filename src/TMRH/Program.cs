using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Tmrh.Core;

namespace Tmrh
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--render") { Render(args[1]); return; }   // used by the build to screenshot the windows
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            // One copy at a time, taken before anything reads or writes the saved settings or the registry.
            using (var only = InstanceLock.TryAcquire(InstanceLock.DefaultName))
            {
                if (!only.Acquired) { ShowOtherCopy(); return; }
                // An older test build's saved copy is moved in when first needed; if that fails the window says so and nothing changes.
                Func<string> sid = () => { try { return BackupStore.WindowsSid(); } catch { return null; } };
                var store = new BackupStore(BackupStore.DefaultDirectory, BackupStore.DefaultLegacyFile, sid);
                var engine = new Engine(new WindowsRegistryStore(), store, new WinInetNotifier(), WindowsBuild);
                Application.Run(new UI.MainForm(engine));
            }
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>Another copy is already open: bring its window to the front, or say so if it can't be found.</summary>
        static void ShowOtherCopy()
        {
            try
            {
                var me = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
                {
                    if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(p.MainWindowHandle, 9);   // SW_RESTORE
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
            catch { }
            MessageBox.Show("TrackMania Registration Helper is already open. Use the window that's already open.", "TrackMania Registration Helper", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Saves pictures of the windows at normal and large text sizes, so layout can be checked by the build.</summary>
        static void Render(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var engine = new Engine(new WindowsRegistryStore(), new BackupStore(System.IO.Path.Combine(dir, "nobackup")), new NoNotify(), WindowsBuild);
            foreach (var scale in new[] { 1F, 1.5F })
            {
                UI.Theme.FontScale = scale;
                foreach (var mode in new[] { Mode.Off, Mode.On, Mode.Interrupted, Mode.BadBackup, Mode.LegacyStuck })
                {
                    using (var f = new UI.MainForm(engine) { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0), ShowInTaskbar = false })
                    {
                        f.Show(); Application.DoEvents(); f.Preview(mode); Application.DoEvents();
                        Shot(f, System.IO.Path.Combine(dir, string.Format("main-{0}-{1:0.0}x.png", mode, scale)));
                    }
                }
                using (var c = new UI.CheckForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(0, 0), ShowInTaskbar = false })
                {
                    c.Show(); Application.DoEvents();
                    c.PreviewText("Windows 26100\r\nHKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings\\CertificateRevocation\r\n   before: 0x00000001 (DWORD)\r\n   after:  0x00000000 (DWORD)\r\n   changed bits: 0x00000001");
                    Application.DoEvents();
                    Shot(c, System.IO.Path.Combine(dir, string.Format("check-{0:0.0}x.png", scale)));
                }
            }
        }

        static void Shot(Form f, string path)
        {
            using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        static void Crash(Exception ex)
        {
            MessageBox.Show("Something went wrong:\n\n" + (ex == null ? "unknown error" : ex.Message) +
                "\n\nIf Registration Mode was being changed, open this tool again: it will offer to put your original settings back.",
                "TrackMania Registration Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>The real Windows build number (Environment.OSVersion can be wrong for older .NET apps).</summary>
        public static int WindowsBuild()
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int b;
                    return k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b) ? b : Environment.OSVersion.Version.Build;
                }
            }
            catch { return Environment.OSVersion.Version.Build; }
        }
    }

    /// <summary>Tells Windows' internet component (WinINet) to reload Internet Options straight away.</summary>
    sealed class WinInetNotifier : ISettingsNotifier
    {
        const int INTERNET_OPTION_REFRESH = 37, INTERNET_OPTION_SETTINGS_CHANGED = 39;

        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        public void SettingsChanged()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }
}
