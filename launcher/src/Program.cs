using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Targeting 4.8 switches on modern behaviour, most importantly support for
// file paths longer than 260 characters (players unzip games in deep folders).
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyTitle("Pokémon Copper Launcher")]
[assembly: System.Reflection.AssemblyProduct("Pokémon Copper Launcher")]
[assembly: System.Reflection.AssemblyVersion(CopperLauncher.Program.Version + ".0")]
[assembly: System.Reflection.AssemblyFileVersion(CopperLauncher.Program.Version + ".0")]

namespace CopperLauncher
{
    static class Program
    {
        public const string Version = "1.0.0";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        // Normal use: just run Launcher.exe.
        // Testing options:
        //   --headless           check and update without showing a window (exit code 0 = ok)
        //   --screenshot <file>  open the window, save a picture of it after the check, close
        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            // Older .NET defaults don't include TLS 1.2, which GitHub requires.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

            Config cfg = Config.Load(AppDomain.CurrentDomain.BaseDirectory);
            Log.Init(cfg.DataDir);
            Log.Write("---- Launcher " + Version + " started in " + cfg.GameDir);

            bool headless = false;
            string screenshot = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--headless") headless = true;
                else if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[++i];
            }

            if (headless) return RunHeadless(cfg);

            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Write("Unexpected error: " + e.Exception);
                MessageBox.Show("Something went wrong:\n" + e.Exception.Message + "\n\nDetails are in launcher_data\\launcher.log",
                                "Pokémon Copper Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("Fatal error: " + e.ExceptionObject);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm form = new MainForm(cfg);
            form.ScreenshotPath = screenshot;
            Application.Run(form);
            return 0;
        }

        static int RunHeadless(Config cfg)
        {
            try
            {
                Updater u = new Updater(cfg);
                u.Report = delegate (string s, double f) { };
                u.Check(false);
                Log.Write("[headless] update available: " + u.UpdateAvailable + ", download " + Updater.Mb(u.DownloadSize));
                if (u.UpdateAvailable) u.Apply();
                Log.Write("[headless] done, installed " + u.State.InstalledVersion);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("[headless] failed: " + ex);
                return 1;
            }
        }
    }
}
