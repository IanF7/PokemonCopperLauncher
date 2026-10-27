using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CopperLauncher
{
    class MainForm : Form
    {
        // Colours, picked to match the title screen sunset.
        static readonly Color Bg = Color.FromArgb(26, 19, 16);
        static readonly Color PanelBg = Color.FromArgb(38, 28, 23);
        static readonly Color Copper = Color.FromArgb(214, 122, 58);
        static readonly Color CopperHot = Color.FromArgb(236, 146, 78);
        static readonly Color Ink = Color.FromArgb(242, 230, 218);
        static readonly Color Muted = Color.FromArgb(170, 150, 135);
        static readonly Color Gold = Color.FromArgb(246, 200, 90);

        enum Mode { Busy, Play, Update, Retry }

        readonly Config cfg;
        readonly Updater updater;
        Mode mode = Mode.Busy;
        bool working;

        BannerControl banner;
        RichTextBox notesBox;
        Label versionLabel, statusLabel;
        CopperProgress progress;
        Button mainButton;
        LinkLabel skipLink, repairLink;

        public string ScreenshotPath;   // test hook: save a picture of the window then close

        public MainForm(Config cfg)
        {
            this.cfg = cfg;
            updater = new Updater(cfg);
            updater.Report = OnReport;
            BuildUi();
        }

        // ------------------------------------------------------------------ layout

        void BuildUi()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = cfg.GameTitle + " Launcher";
            ClientSize = new Size(740, 590);
            MinimumSize = new Size(560, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Ink;
            Font = new Font("Segoe UI", 9.5F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // --- middle: patch notes (added first so it fills what's left) ---
            Panel middle = new Panel();
            middle.Dock = DockStyle.Fill;
            middle.Padding = new Padding(18, 14, 18, 6);

            Panel notesFrame = new Panel();
            notesFrame.Dock = DockStyle.Fill;
            notesFrame.BackColor = PanelBg;
            notesFrame.Padding = new Padding(14, 12, 6, 12);

            notesBox = new RichTextBox();
            notesBox.Dock = DockStyle.Fill;
            notesBox.ReadOnly = true;
            notesBox.BorderStyle = BorderStyle.None;
            notesBox.BackColor = PanelBg;
            notesBox.ForeColor = Ink;
            notesBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            notesBox.DetectUrls = false;
            notesBox.TabStop = false;
            notesFrame.Controls.Add(notesBox);
            notesBox.HandleCreated += delegate { DarkScrollbars(notesBox); };

            Panel notesHeader = new Panel();
            notesHeader.Dock = DockStyle.Top;
            notesHeader.Height = 30;

            Label notesTitle = new Label();
            notesTitle.Text = "PATCH NOTES";
            notesTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            notesTitle.ForeColor = Copper;
            notesTitle.AutoSize = true;
            notesTitle.Dock = DockStyle.Left;

            versionLabel = new Label();
            versionLabel.ForeColor = Muted;
            versionLabel.AutoSize = true;
            versionLabel.Dock = DockStyle.Right;
            versionLabel.Padding = new Padding(0, 2, 0, 0);

            notesHeader.Controls.Add(versionLabel);
            notesHeader.Controls.Add(notesTitle);

            middle.Controls.Add(notesFrame);
            middle.Controls.Add(notesHeader);

            // --- bottom: status, progress, buttons ---
            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 112;
            bottom.Padding = new Padding(18, 8, 18, 16);
            bottom.ColumnCount = 2;
            bottom.RowCount = 3;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Font = new Font("Segoe UI", 10F);
            statusLabel.AutoEllipsis = true;
            statusLabel.Text = "Starting...";

            progress = new CopperProgress();
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(3, 5, 18, 5);
            progress.Track = PanelBg;
            progress.Fill = Copper;

            FlowLayoutPanel links = new FlowLayoutPanel();
            links.Dock = DockStyle.Fill;
            links.WrapContents = false;
            links.Padding = new Padding(0, 6, 0, 0);
            links.Controls.Add(MakeLink("Website", delegate { OpenUrl(cfg.WebsiteUrl); }));
            links.Controls.Add(MakeLink("Discord", delegate { OpenUrl(cfg.DiscordUrl); }));
            repairLink = MakeLink("Repair game files", delegate { StartCheck(true); });
            links.Controls.Add(repairLink);
            skipLink = MakeLink("Play without updating", delegate { LaunchGame(); });
            skipLink.Visible = false;
            links.Controls.Add(skipLink);

            mainButton = new Button();
            mainButton.Dock = DockStyle.Fill;
            mainButton.Margin = new Padding(0, 4, 0, 0);
            mainButton.FlatStyle = FlatStyle.Flat;
            mainButton.FlatAppearance.BorderSize = 0;
            mainButton.FlatAppearance.MouseOverBackColor = CopperHot;
            mainButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(180, 98, 44);
            mainButton.BackColor = Copper;
            mainButton.ForeColor = Color.White;
            mainButton.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            mainButton.Cursor = Cursors.Hand;
            mainButton.Text = "...";
            mainButton.Click += delegate { MainButtonClicked(); };

            bottom.Controls.Add(statusLabel, 0, 0);
            bottom.Controls.Add(progress, 0, 1);
            bottom.Controls.Add(links, 0, 2);
            bottom.Controls.Add(mainButton, 1, 0);
            bottom.SetRowSpan(mainButton, 3);

            // --- top: banner ---
            banner = new BannerControl();
            banner.Dock = DockStyle.Top;
            banner.Height = 190;
            banner.Image = LoadBanner();

            Controls.Add(middle);
            Controls.Add(bottom);
            Controls.Add(banner);
            AcceptButton = mainButton;
            ResumeLayout(true);

            SetMode(Mode.Busy);
            UpdateVersionLabel();
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        // Windows 10/11 dark scrollbars. Does nothing elsewhere (e.g. under Wine).
        static void DarkScrollbars(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        LinkLabel MakeLink(string text, Action onClick)
        {
            LinkLabel l = new LinkLabel();
            l.Text = text;
            l.AutoSize = true;
            l.LinkColor = Muted;
            l.ActiveLinkColor = CopperHot;
            l.VisitedLinkColor = Muted;
            l.LinkBehavior = LinkBehavior.HoverUnderline;
            l.Margin = new Padding(0, 0, 14, 0);
            l.LinkClicked += delegate { onClick(); };
            return l;
        }

        static Image LoadBanner()
        {
            try
            {
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("banner.png"))
                    return s == null ? null : new Bitmap(s);
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ flow

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Keep the whole window on small or zoomed-in screens.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height) Height = area.Height;
            if (Width > area.Width) Width = area.Width;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ShowCachedNotes();
            StartCheck(false);
        }

        void StartCheck(bool verifyEverything)
        {
            if (working) return;
            SetMode(Mode.Busy);
            Exception error = null;
            string notes = null;
            updater.AllowSelfUpdate = !Program.JustUpdated;
            RunInBackground(delegate
            {
                try { updater.Check(verifyEverything); }
                catch (Exception ex) { error = ex; }
                try { notes = updater.FetchText(cfg.PatchNotesUrl); }
                catch { /* not a big deal, keep showing the saved copy */ }
            }, delegate
            {
                if (notes != null)
                {
                    ShowNotes(notes);
                    try { File.WriteAllText(Path.Combine(cfg.DataDir, "patchnotes.txt"), notes, new UTF8Encoding(false)); } catch { }
                }
                AfterCheck(error);
            });
        }

        void AfterCheck(Exception error)
        {
            if (error == null && updater.LauncherReplaced)
            {
                // A newer launcher was downloaded: open it and close this one.
                try
                {
                    Program.RestartUpdated(ScreenshotPath != null ? new string[] { "--screenshot", "\"" + ScreenshotPath + "\"" } : new string[0]);
                    ScreenshotPath = null;
                    Close();
                    return;
                }
                catch (Exception ex) { Log.Write("Couldn't start the new launcher: " + ex.Message); }
            }
            UpdateVersionLabel();
            if (error != null)
            {
                Log.Write("Update check failed: " + error);
                bool canPlay = updater.GameInstalled;
                if (error is OperationCanceledException) { Status("Check cancelled."); }
                else if (canPlay) Status("Couldn't check for updates. You can still play.");
                else Status(Friendly(error));
                SetMode(canPlay ? Mode.Play : Mode.Retry);
            }
            else if (updater.UpdateAvailable)
            {
                bool fresh = updater.Needed.Count == updater.Remote.Files.Count;
                string size = Updater.Mb(updater.DownloadSize);
                if (fresh) Status("Ready to install " + cfg.GameTitle + " v" + updater.Remote.Version + "  (" + size + " download)");
                else if (updater.State.InstalledVersion == updater.Remote.Version)
                    Status("Some game files are missing or damaged. Click Repair to fix them  (" + size + ")");
                else Status("Version " + updater.Remote.Version + " is available!  (" + size + " download)");
                SetMode(Mode.Update);
            }
            else
            {
                Status("You're up to date. Have fun!");
                SetMode(Mode.Play);
            }

            if (ScreenshotPath != null) TakeScreenshotAndClose();
        }

        void MainButtonClicked()
        {
            if (mode == Mode.Play) LaunchGame();
            else if (mode == Mode.Retry) StartCheck(false);
            else if (mode == Mode.Update) StartUpdate();
        }

        void StartUpdate()
        {
            if (working) return;
            SetMode(Mode.Busy);
            Exception error = null;
            RunInBackground(delegate
            {
                try { updater.Apply(); }
                catch (Exception ex) { error = ex; }
            }, delegate
            {
                UpdateVersionLabel();
                if (error == null)
                {
                    Status("Update complete! You're on v" + updater.Remote.Version + ".");
                    SetMode(Mode.Play);
                }
                else
                {
                    Log.Write("Update failed: " + error);
                    Status(error is OperationCanceledException ? "Update stopped." : Friendly(error));
                    SetMode(Mode.Update);
                    if (!(error is OperationCanceledException))
                        MessageBox.Show(this, Friendly(error), "Update problem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
        }

        void LaunchGame()
        {
            string launch = updater.Remote != null ? updater.Remote.Launch : "Game.exe";
            string exe = Path.Combine(cfg.GameDir, launch);
            if (!File.Exists(exe))
            {
                MessageBox.Show(this, "Couldn't find " + launch + " in this folder.", "Can't start the game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.WorkingDirectory = cfg.GameDir;
                psi.UseShellExecute = false;
                Process.Start(psi);
                Log.Write("Launched " + launch);
                Close();
            }
            catch (Exception ex)
            {
                Log.Write("Launch failed: " + ex);
                MessageBox.Show(this, "Couldn't start the game:\n" + ex.Message, "Can't start the game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (working && ScreenshotPath == null)
            {
                DialogResult r = MessageBox.Show(this, "The launcher is still working. Stop and close?", "Close launcher",
                                                 MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                updater.CancelRequested = true;
            }
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ ui helpers

        void SetMode(Mode m)
        {
            mode = m;
            bool busy = m == Mode.Busy;
            mainButton.Enabled = !busy;
            mainButton.BackColor = busy ? Color.FromArgb(90, 66, 52) : Copper;
            mainButton.Text = m == Mode.Play ? "PLAY" : m == Mode.Update ? (IsFreshInstall() ? "INSTALL" : "UPDATE") : m == Mode.Retry ? "RETRY" : "...";
            repairLink.Visible = !busy && updater.Remote != null;
            skipLink.Visible = m == Mode.Update && updater.GameInstalled;
            if (m == Mode.Busy) progress.Value = -1;
            else if (m == Mode.Play) progress.Value = 1;
            else progress.Value = 0;
        }

        bool IsFreshInstall()
        {
            return updater.Remote != null && updater.Needed.Count == updater.Remote.Files.Count;
        }

        void OnReport(string text, double fraction)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    statusLabel.Text = text;
                    progress.Value = fraction;
                });
            }
            catch (InvalidOperationException) { }
        }

        void Status(string text) { statusLabel.Text = text; }

        void UpdateVersionLabel()
        {
            string installed = updater.State.InstalledVersion;
            if (installed.Length == 0) installed = GuessInstalledVersion();
            string s = "Installed: " + (installed.Length > 0 ? "v" + installed : "none");
            if (updater.Remote != null) s += "     Latest: v" + updater.Remote.Version;
            versionLabel.Text = s;
        }

        // Before the launcher has ever run, read the version from the game window title in mkxp.json.
        string GuessInstalledVersion()
        {
            try
            {
                string path = Path.Combine(cfg.GameDir, "mkxp.json");
                if (!File.Exists(path)) return "";
                Match m = Regex.Match(File.ReadAllText(path, Encoding.UTF8), "\"windowTitle\"\\s*:\\s*\"[^\"]*?v(\\d+(?:\\.\\d+)*)");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        void ShowCachedNotes()
        {
            string path = Path.Combine(cfg.DataDir, "patchnotes.txt");
            try
            {
                if (File.Exists(path)) ShowNotes(File.ReadAllText(path, Encoding.UTF8));
                else ShowNotes("Loading patch notes...");
            }
            catch { }
        }

        void ShowNotes(string text)
        {
            try
            {
                notesBox.Rtf = PatchNotes.ToRtf(text, Ink, Gold, Copper, Muted);
                notesBox.SelectionStart = 0;
                notesBox.ScrollToCaret();
            }
            catch (Exception ex)
            {
                Log.Write("Couldn't format patch notes: " + ex.Message);
                notesBox.Text = text;
            }
        }

        void RunInBackground(Action work, Action done)
        {
            working = true;
            Thread t = new Thread(delegate ()
            {
                work();
                try { BeginInvoke((MethodInvoker)delegate { working = false; done(); }); }
                catch (InvalidOperationException) { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static string Friendly(Exception ex)
        {
            if (ex is FriendlyException) return ex.Message;
            return "Something went wrong: " + ex.Message + "\nDetails are in launcher_data\\launcher.log";
        }

        void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch (Exception ex) { Log.Write("Couldn't open " + url + ": " + ex.Message); }
        }

        void TakeScreenshotAndClose()
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 400;
            t.Tick += delegate
            {
                t.Stop();
                try
                {
                    Rectangle r = RectangleToScreen(ClientRectangle);
                    using (Bitmap bmp = new Bitmap(r.Width, r.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                        bmp.Save(ScreenshotPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception ex) { Log.Write("Screenshot failed: " + ex.Message); }
                Close();
            };
            t.Start();
        }
    }

    // The title art across the top, scaled up crisply (it's pixel art).
    class BannerControl : Control
    {
        public Image Image;

        public BannerControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);
            if (Image == null) return;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // The art is 512x384; the logo sits in rows ~18-153. Scale so that band fills
            // the control (cropping the sides or the sky if needed) and centre on it.
            const float bandTop = 21f, bandHeight = 135f;
            float scale = Math.Max(Width / (float)Image.Width, Height / bandHeight);
            float w = Image.Width * scale, h = Image.Height * scale;
            float x = (Width - w) / 2f;
            float y = -bandTop * scale - (bandHeight * scale - Height) / 2f;
            g.DrawImage(Image, new RectangleF(x, y, w, h));
        }
    }

    // A flat progress bar in the launcher colours. Value -1 shows a moving "busy" stripe.
    class CopperProgress : Control
    {
        public Color Track = Color.DimGray, Fill = Color.Orange;
        double value;
        int phase;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();

        public CopperProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            anim.Interval = 30;
            anim.Tick += delegate { phase = (phase + 6) % 1000; Invalidate(); };
        }

        public double Value
        {
            get { return value; }
            set
            {
                this.value = value;
                anim.Enabled = value < 0;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Track)) g.FillRectangle(b, ClientRectangle);
            using (SolidBrush f = new SolidBrush(Fill))
            {
                if (value < 0)
                {
                    int w = Math.Max(40, Width / 4);
                    int x = (int)((phase / 1000.0) * (Width + w)) - w;
                    g.FillRectangle(f, x, 0, w, Height);
                }
                else
                {
                    g.FillRectangle(f, 0, 0, (int)(Width * Math.Min(1.0, value)), Height);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }
    }
}
