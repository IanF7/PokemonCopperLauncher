using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CopperLauncher
{
    // Thrown for problems we can explain to the player in plain words.
    class FriendlyException : Exception
    {
        public FriendlyException(string msg) : base(msg) { }
        public FriendlyException(string msg, Exception inner) : base(msg, inner) { }
    }

    // Checks the game folder against the latest manifest and brings it up to date.
    // All methods run on a background thread; progress goes out through Report.
    class Updater
    {
        readonly Config cfg;
        public readonly LocalState State;

        // status text, progress 0..1 (or -1 for "busy, unknown length")
        public Action<string, double> Report = delegate { };
        public volatile bool CancelRequested;

        public Manifest Remote;
        public List<FileEntry> Needed = new List<FileEntry>();
        public List<string> ToDelete = new List<string>();

        public Updater(Config cfg)
        {
            this.cfg = cfg;
            State = new LocalState(cfg.DataDir);
        }

        public bool UpdateAvailable { get { return Needed.Count > 0 || ToDelete.Count > 0; } }

        public bool GameInstalled
        {
            get
            {
                string launch = Remote != null ? Remote.Launch : "Game.exe";
                return File.Exists(Path.Combine(cfg.GameDir, launch));
            }
        }

        // Total size of the zips that need downloading.
        public long DownloadSize
        {
            get
            {
                long total = 0;
                foreach (int i in NeededPacks().Keys) total += Remote.Packs[i].Size;
                return total;
            }
        }

        // ---------------------------------------------------------------- check

        // Set when Check replaced Launcher.exe with a newer one; the caller should
        // start the new launcher and close this one.
        public bool LauncherReplaced;
        public bool AllowSelfUpdate = true;

        public void Check(bool verifyEverything)
        {
            Report("Checking for updates...", -1);
            string text = FetchText(cfg.ManifestUrl);
            Remote = Manifest.Parse(text);
            Log.Write("Latest version on server: " + Remote.Version + " (" + Remote.Files.Count + " files, " + Remote.Packs.Count + " packs)");
            if (AllowSelfUpdate && UpdateLauncher()) return;
            Scan(verifyEverything);
        }

        // ---------------------------------------------------------------- self-update

        public static string ExePath
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
        }

        // Compares "1.2.10" style versions. Returns <0, 0 or >0.
        public static int CompareVersions(string a, string b)
        {
            string[] pa = a.Split('.'), pb = b.Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = 0, y = 0;
                if (i < pa.Length) int.TryParse(pa[i], out x);
                if (i < pb.Length) int.TryParse(pb[i], out y);
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        // Downloads a newer Launcher.exe if the release has one, and swaps it in.
        // Windows lets a running program be renamed, so the running copy becomes
        // Launcher.exe.old (deleted next time) and the new one takes its name.
        // Any problem just leaves the current launcher in place.
        bool UpdateLauncher()
        {
            if (Remote.LauncherVersion == null || CompareVersions(Remote.LauncherVersion, Program.Version) <= 0)
                return false;
            string exe = ExePath;
            string fresh = exe + ".new", old = exe + ".old";
            Log.Write("Launcher " + Program.Version + " -> " + Remote.LauncherVersion);
            try
            {
                Report("Updating the launcher...", -1);
                Download(cfg.PackUrl(Remote.LauncherFile.Tag, Remote.LauncherFile.Name), fresh, Remote.LauncherFile,
                         0, Remote.LauncherFile.Size, "", "Updating the launcher");
                TryDelete(old);
                File.Move(exe, old);
                try { File.Move(fresh, exe); }
                catch { File.Move(old, exe); throw; }
                LauncherReplaced = true;
                Log.Write("Launcher updated; restarting.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Launcher self-update failed, carrying on with this one: " + ex.Message);
                TryDelete(fresh);
                return false;
            }
        }

        // Removes the previous launcher left behind by a self-update. It may still
        // be closing, so this tries for a few seconds.
        public static void CleanUpOldLauncher()
        {
            string old = ExePath + ".old";
            if (!File.Exists(old)) return;
            Thread t = new Thread(delegate ()
            {
                for (int i = 0; i < 20 && File.Exists(old); i++)
                {
                    try { File.Delete(old); } catch { Thread.Sleep(250); }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Scan(bool verifyEverything)
        {
            Needed.Clear();
            ToDelete.Clear();

            long totalBytes = 0, doneBytes = 0, counted = 0;
            foreach (FileEntry f in Remote.Files) totalBytes += f.Size;
            int lastTick = 0;

            foreach (FileEntry f in Remote.Files)
            {
                ThrowIfCancelled();
                string full = SafePath(f.Path);
                FileInfo fi = new FileInfo(full);
                if (!fi.Exists || fi.Length != f.Size)
                {
                    Needed.Add(f);
                }
                else
                {
                    string hash = verifyEverything ? null : State.Lookup(f.Path, fi);
                    if (hash == null)
                    {
                        long before = counted;
                        hash = HashFile(full, delegate (long n)
                        {
                            doneBytes = before + n;
                            if (Environment.TickCount - lastTick > 150)
                            {
                                lastTick = Environment.TickCount;
                                Report("Checking game files...", totalBytes > 0 ? (double)doneBytes / totalBytes : -1);
                            }
                        });
                        State.Remember(f.Path, fi, hash);
                    }
                    if (!string.Equals(hash, f.Sha1, StringComparison.OrdinalIgnoreCase)) Needed.Add(f);
                }
                counted += f.Size;
                doneBytes = counted;
                if (Environment.TickCount - lastTick > 150)
                {
                    lastTick = Environment.TickCount;
                    Report("Checking game files...", totalBytes > 0 ? (double)doneBytes / totalBytes : -1);
                }
            }

            foreach (string d in Remote.Deletes)
                if (File.Exists(SafePath(d))) ToDelete.Add(d);

            // Empty folders are harmless to create right away.
            foreach (string d in Remote.Dirs)
            {
                try { Directory.CreateDirectory(SafePath(d)); }
                catch (Exception ex) { Log.Write("Couldn't create folder " + d + ": " + ex.Message); }
            }

            State.Save();
            Log.Write("Scan done: " + Needed.Count + " file(s) to download, " + ToDelete.Count + " to remove. Installed version: '" + State.InstalledVersion + "'");

            // Everything matches, so whatever the player had, they're on the latest version now.
            if (!UpdateAvailable && State.InstalledVersion != Remote.Version)
            {
                State.InstalledVersion = Remote.Version;
                State.Save();
            }
        }

        // ---------------------------------------------------------------- apply

        public void Apply()
        {
            Dictionary<int, List<FileEntry>> byPack = NeededPacks();
            long totalDownload = DownloadSize;
            long downloadedBefore = 0;
            string dlDir = Path.Combine(cfg.DataDir, "downloads");
            Directory.CreateDirectory(dlDir);

            int packNum = 0;
            foreach (KeyValuePair<int, List<FileEntry>> kv in byPack)
            {
                packNum++;
                PackInfo pack = Remote.Packs[kv.Key];
                string zipPath = Path.Combine(dlDir, pack.Name);
                string label = byPack.Count > 1 ? " (part " + packNum + " of " + byPack.Count + ")" : "";

                Download(cfg.PackUrl(pack.Tag, pack.Name), zipPath, pack, downloadedBefore, totalDownload, label, "Downloading update");
                downloadedBefore += pack.Size;

                Extract(zipPath, kv.Value, label);
                TryDelete(zipPath);
            }

            foreach (string d in ToDelete)
            {
                string full = SafePath(d);
                try
                {
                    if (File.Exists(full)) { File.SetAttributes(full, FileAttributes.Normal); File.Delete(full); }
                    State.Forget(d);
                    Log.Write("Removed old file " + d);
                }
                catch (Exception ex) { Log.Write("Couldn't remove " + d + ": " + ex.Message); }
            }

            Needed.Clear();
            ToDelete.Clear();
            State.InstalledVersion = Remote.Version;
            State.Save();
            Log.Write("Update to " + Remote.Version + " finished.");
            Report("Up to date!", 1);
        }

        Dictionary<int, List<FileEntry>> NeededPacks()
        {
            Dictionary<int, List<FileEntry>> byPack = new Dictionary<int, List<FileEntry>>();
            foreach (FileEntry f in Needed)
            {
                List<FileEntry> list;
                if (!byPack.TryGetValue(f.Pack, out list)) { list = new List<FileEntry>(); byPack[f.Pack] = list; }
                list.Add(f);
            }
            return byPack;
        }

        void Download(string url, string dest, PackInfo pack, long before, long total, string label, string verb)
        {
            string part = dest + ".part";

            // Left over from an earlier run that was interrupted after downloading?
            if (File.Exists(dest))
            {
                if (new FileInfo(dest).Length == pack.Size && HashFile(dest, null) == pack.Sha1) return;
                TryDelete(dest);
            }

            Log.Write("Downloading " + url);
            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    DownloadOnce(url, part, pack.Size, before, total, label, verb);
                    break;
                }
                catch (WebException ex)
                {
                    Log.Write("Download attempt " + attempt + " failed: " + ex.Message);
                    if (CancelRequested) throw new OperationCanceledException();
                    if (attempt >= 4)
                        throw new FriendlyException("The download kept failing. Check your internet connection and try again.\n(" + ex.Message + ")", ex);
                    Report("Connection hiccup, retrying...", -1);
                    Thread.Sleep(2000 * attempt);
                }
                catch (IOException ex)
                {
                    Log.Write("Download attempt " + attempt + " failed: " + ex.Message);
                    if (attempt >= 4) throw new FriendlyException("Couldn't save the download. Is your drive full?\n(" + ex.Message + ")", ex);
                    Thread.Sleep(2000 * attempt);
                }
            }

            Report("Verifying download" + label + "...", -1);
            long size = new FileInfo(part).Length;
            string hash = HashFile(part, null);
            if (size != pack.Size || !string.Equals(hash, pack.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(part);
                Log.Write("Bad download: got " + size + " bytes / " + hash + ", expected " + pack.Size + " / " + pack.Sha1);
                throw new FriendlyException("The download came through damaged. Please try again.");
            }
            File.Move(part, dest);
        }

        void DownloadOnce(string url, string part, long expected, long before, long total, string label, string verb)
        {
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have >= expected) { TryDelete(part); have = 0; }

            HttpWebRequest req = NewRequest(url);
            if (have > 0) req.AddRange(have);

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                bool resuming = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
                if (!resuming) have = 0;
                else Log.Write("Resuming download at " + have + " bytes");

                using (Stream src = resp.GetResponseStream())
                using (FileStream dst = new FileStream(part, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[1 << 16];
                    int lastTick = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        ThrowIfCancelled();
                        dst.Write(buf, 0, n);
                        have += n;
                        if (Environment.TickCount - lastTick > 100)
                        {
                            lastTick = Environment.TickCount;
                            long done = before + have;
                            Report(verb + label + "...   " + Mb(done) + " / " + Mb(total),
                                   total > 0 ? (double)done / total : -1);
                        }
                    }
                }
            }
        }

        void Extract(string zipPath, List<FileEntry> files, string label)
        {
            using (FileStream fs = File.OpenRead(zipPath))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchiveEntry e in zip.Entries) entries[e.FullName.Replace('\\', '/')] = e;

                int done = 0, lastTick = 0;
                foreach (FileEntry f in files)
                {
                    ThrowIfCancelled();
                    ZipArchiveEntry entry;
                    if (!entries.TryGetValue(f.Path, out entry))
                        throw new FriendlyException("The update is missing a file (" + f.Path + "). Please let the developer know!");

                    string full = SafePath(f.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    string tmp = full + ".launcher-new";
                    string hash;
                    using (Stream src = entry.Open())
                    using (FileStream dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    using (SHA1 sha = SHA1.Create())
                    {
                        byte[] buf = new byte[1 << 16];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            sha.TransformBlock(buf, 0, n, null, 0);
                            dst.Write(buf, 0, n);
                        }
                        sha.TransformFinalBlock(buf, 0, 0);
                        hash = Hex(sha.Hash);
                    }
                    if (!string.Equals(hash, f.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(tmp);
                        throw new FriendlyException("A file in the update didn't match what was expected (" + f.Path + "). Please try again.");
                    }
                    ReplaceFile(tmp, full, f.Path);
                    State.Remember(f.Path, new FileInfo(full), hash);

                    done++;
                    if (Environment.TickCount - lastTick > 100)
                    {
                        lastTick = Environment.TickCount;
                        Report("Installing update" + label + "...   " + done + " / " + files.Count + " files", (double)done / files.Count);
                    }
                }
            }
            State.Save();
        }

        static void ReplaceFile(string tmp, string full, string rel)
        {
            try
            {
                if (File.Exists(full))
                {
                    File.SetAttributes(full, FileAttributes.Normal);
                    File.Delete(full);
                }
                File.Move(tmp, full);
            }
            catch (Exception ex)
            {
                TryDelete(tmp);
                if (ex is IOException || ex is UnauthorizedAccessException)
                    throw new FriendlyException("Couldn't replace " + rel + ".\nMake sure the game is closed, then try again.", ex);
                throw;
            }
        }

        // ---------------------------------------------------------------- helpers

        public string FetchText(string url)
        {
            try
            {
                HttpWebRequest req = NewRequest(url);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (WebException ex)
            {
                Log.Write("Fetching " + url + " failed: " + ex.Message);
                throw new FriendlyException("Couldn't reach the update server.", ex);
            }
        }

        static HttpWebRequest NewRequest(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "PokemonCopperLauncher/" + Program.Version;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            req.AllowAutoRedirect = true;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            return req;
        }

        // Turns a manifest path into a full path, refusing anything that would
        // land outside the game folder.
        string SafePath(string rel)
        {
            if (string.IsNullOrEmpty(rel) || rel.Contains(":") || rel.StartsWith("/") || rel.StartsWith("\\"))
                throw new InvalidDataException("Bad path in manifest: " + rel);
            foreach (string part in rel.Split('/', '\\'))
                if (part == "..") throw new InvalidDataException("Bad path in manifest: " + rel);
            string full = Path.GetFullPath(Path.Combine(cfg.GameDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            string root = cfg.GameDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Bad path in manifest: " + rel);
            return full;
        }

        public static string HashFile(string path, Action<long> progress)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            using (SHA1 sha = SHA1.Create())
            {
                byte[] buf = new byte[1 << 16];
                long total = 0;
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    sha.TransformBlock(buf, 0, n, null, 0);
                    total += n;
                    if (progress != null) progress(total);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                return Hex(sha.Hash);
            }
        }

        static string Hex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string Mb(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        void ThrowIfCancelled()
        {
            if (CancelRequested) throw new OperationCanceledException();
        }
    }
}
