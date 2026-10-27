using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CopperLauncher
{
    // Launcher settings. The defaults below are built into Launcher.exe.
    // A "launcher.ini" file next to Launcher.exe can override any of them
    // (handy for testing against a local server).
    class Config
    {
        // Where releases live. GitHub release URLs look like:
        //   {ReleaseBase}/latest/download/manifest.txt
        //   {ReleaseBase}/download/{tag}/{file}
        public string ReleaseBase = "https://github.com/IanF7/PokemonCopperLauncher/releases";
        public string WebsiteUrl = "https://ianf7.github.io/PokemonCopper/";
        public string DiscordUrl = "https://discord.gg/pavpnHGPYh";
        public string ShowdownUrl = "http://pokemoncoppershowdown.duckdns.org/";
        public string GameTitle = "Pokémon Copper";

        public string GameDir;   // Folder Launcher.exe is in (the game folder).
        public string DataDir;   // GameDir/launcher_data - the launcher's own files.

        public string ManifestUrl { get { return Base + "/latest/download/manifest.txt"; } }
        public string PatchNotesUrl { get { return Base + "/latest/download/patchnotes.txt"; } }

        public string PackUrl(string tag, string file)
        {
            return Base + "/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(file);
        }

        string Base { get { return ReleaseBase.TrimEnd('/'); } }

        public static Config Load(string gameDir)
        {
            Config c = new Config();
            c.GameDir = Path.GetFullPath(gameDir);
            c.DataDir = Path.Combine(c.GameDir, "launcher_data");
            string ini = Path.Combine(c.GameDir, "launcher.ini");
            if (File.Exists(ini))
            {
                foreach (string raw in File.ReadAllLines(ini, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "release_base") c.ReleaseBase = val;
                    else if (key == "website_url") c.WebsiteUrl = val;
                    else if (key == "discord_url") c.DiscordUrl = val;
                    else if (key == "showdown_url") c.ShowdownUrl = val;
                }
                Log.Write("Loaded launcher.ini (release_base = " + c.ReleaseBase + ")");
            }
            return c;
        }
    }

    static class Log
    {
        static string path;
        static readonly object gate = new object();

        public static void Init(string dataDir)
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                path = Path.Combine(dataDir, "launcher.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            }
            catch { path = null; }
        }

        public static void Write(string msg)
        {
            if (path == null) return;
            lock (gate)
            {
                try { File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8); }
                catch { }
            }
        }
    }
}
