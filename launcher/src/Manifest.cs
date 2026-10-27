using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CopperLauncher
{
    // A zip on the release page holding some of the game's files.
    class PackInfo
    {
        public string Tag;   // Release the zip is attached to, e.g. "v1.9.3"
        public string Name;  // File name, e.g. "files-v1.9.3.zip"
        public long Size;
        public string Sha1;
    }

    class FileEntry
    {
        public string Path;  // Relative to the game folder, forward slashes.
        public long Size;
        public string Sha1;
        public int Pack;     // Index into Manifest.Packs
    }

    // manifest.txt: the list of every file in the latest version.
    // Tab-separated lines (made by tools/make_release.py):
    //   pokemon-copper-manifest  1
    //   version  1.9.3
    //   tag      v1.9.3
    //   launch   Game.exe
    //   pack     <index> <tag> <zip name> <size> <sha1>
    //   file     <pack index> <size> <sha1> <path>
    //   dir      <path>     (an empty folder the game expects)
    //   delete   <path>
    //   launcher <version> <tag> <file name> <size> <sha1>   (newest Launcher.exe)
    class Manifest
    {
        public string Version = "";
        public string Tag = "";
        public string Launch = "Game.exe";
        public string LauncherVersion;    // null if the release doesn't include a launcher
        public PackInfo LauncherFile;
        public List<PackInfo> Packs = new List<PackInfo>();
        public List<FileEntry> Files = new List<FileEntry>();
        public List<string> Deletes = new List<string>();
        public List<string> Dirs = new List<string>();

        public static Manifest Parse(string text)
        {
            Manifest m = new Manifest();
            string[] lines = text.Replace("\r", "").Split('\n');
            if (lines.Length == 0 || !lines[0].StartsWith("pokemon-copper-manifest"))
                throw new InvalidDataException("The update server sent something that isn't a game manifest.");
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] p = lines[i].Split('\t');
                switch (p[0])
                {
                    case "version": m.Version = p[1]; break;
                    case "tag": m.Tag = p[1]; break;
                    case "launch": m.Launch = p[1]; break;
                    case "pack":
                        PackInfo pk = new PackInfo();
                        pk.Tag = p[2]; pk.Name = p[3];
                        pk.Size = long.Parse(p[4], CultureInfo.InvariantCulture);
                        pk.Sha1 = p[5];
                        if (int.Parse(p[1], CultureInfo.InvariantCulture) != m.Packs.Count)
                            throw new InvalidDataException("Manifest packs are out of order.");
                        m.Packs.Add(pk);
                        break;
                    case "file":
                        FileEntry f = new FileEntry();
                        f.Pack = int.Parse(p[1], CultureInfo.InvariantCulture);
                        f.Size = long.Parse(p[2], CultureInfo.InvariantCulture);
                        f.Sha1 = p[3];
                        f.Path = p[4];
                        if (f.Pack < 0 || f.Pack >= m.Packs.Count)
                            throw new InvalidDataException("Manifest refers to a missing pack.");
                        m.Files.Add(f);
                        break;
                    case "delete": m.Deletes.Add(p[1]); break;
                    case "dir": m.Dirs.Add(p[1]); break;
                    case "launcher":
                        m.LauncherVersion = p[1];
                        m.LauncherFile = new PackInfo();
                        m.LauncherFile.Tag = p[2]; m.LauncherFile.Name = p[3];
                        m.LauncherFile.Size = long.Parse(p[4], CultureInfo.InvariantCulture);
                        m.LauncherFile.Sha1 = p[5];
                        break;
                    // Unknown lines are ignored so newer manifests still work with older launchers.
                }
            }
            if (m.Files.Count == 0) throw new InvalidDataException("The game manifest is empty.");
            return m;
        }
    }

    // launcher_data/state.txt: the installed version plus a cache of file
    // fingerprints, so the launcher only re-reads files that changed on disk.
    class LocalState
    {
        class Cached { public long Size; public long Ticks; public string Sha1; }

        public string InstalledVersion = "";
        readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        readonly string path;

        public LocalState(string dataDir)
        {
            path = Path.Combine(dataDir, "state.txt");
            try
            {
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p[0] == "installed" && p.Length > 1) InstalledVersion = p[1];
                    else if (p[0] == "f" && p.Length == 5)
                    {
                        Cached c = new Cached();
                        c.Size = long.Parse(p[1], CultureInfo.InvariantCulture);
                        c.Ticks = long.Parse(p[2], CultureInfo.InvariantCulture);
                        c.Sha1 = p[3];
                        cache[p[4]] = c;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Couldn't read state.txt, starting fresh: " + ex.Message);
                cache.Clear();
            }
        }

        public string Lookup(string rel, FileInfo fi)
        {
            Cached c;
            if (cache.TryGetValue(rel, out c) && c.Size == fi.Length && c.Ticks == fi.LastWriteTimeUtc.Ticks)
                return c.Sha1;
            return null;
        }

        public void Remember(string rel, FileInfo fi, string sha1)
        {
            fi.Refresh();
            Cached c = new Cached();
            c.Size = fi.Length; c.Ticks = fi.LastWriteTimeUtc.Ticks; c.Sha1 = sha1;
            cache[rel] = c;
        }

        public void Forget(string rel) { cache.Remove(rel); }

        public List<string> KnownFiles() { return new List<string>(cache.Keys); }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("pokemon-copper-state\t1\n");
            sb.Append("installed\t").Append(InstalledVersion).Append('\n');
            foreach (KeyValuePair<string, Cached> kv in cache)
            {
                sb.Append("f\t").Append(kv.Value.Size.ToString(CultureInfo.InvariantCulture))
                  .Append('\t').Append(kv.Value.Ticks.ToString(CultureInfo.InvariantCulture))
                  .Append('\t').Append(kv.Value.Sha1).Append('\t').Append(kv.Key).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
