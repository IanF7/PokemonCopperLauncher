"""Makes everything for a new Pokemon Copper release in one go.

Usage (or just run release.bat):
    python tools/release.py 2.0.0

Your game folder is only read, never changed. The tool:
  1. Copies the game into build/ (a PC copy and an Android copy).
  2. Runs scripts_combine.rb in the copy and removes Data/Scripts. If your
     folder also has combined scripts that don't match Data/Scripts, it stops.
     The Android copy is the same, with SetControls' ENABLED = false.
  3. Builds the launcher's update files from the PC copy (see make_release.py).
  4. Builds the two full downloads:
       Pokemon-Copper.zip          Windows / Mac / Linux / Steam Deck, with Launcher.exe
       Pokemon-Copper-Android.zip  for JoiPlay
  5. Deletes build/ again.
Everything to upload ends up in releases/v<version>/upload/.

For the first release, built from zips you already shipped:
    python tools/release.py 1.9.2 --from-zip "C:/.../Pokemon Copper 1.9.2.zip"
                                  --android-zip "C:/.../Pokemon Copper 1.9.2 Android.zip"
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import make_release  # noqa: E402

SETTINGS = json.load(open(os.path.join(ROOT, "release_settings.json"), encoding="utf-8"))
SETCONTROLS_SECTION = SETTINGS.get("android_setcontrols_section", "Essentials-SetControls")
PC_ZIP = "Pokemon-Copper.zip"
ANDROID_ZIP = "Pokemon-Copper-Android.zip"
ZIP_ROOT = "Pokemon Copper/"


def step(msg):
    print()
    print("== " + msg)


def find_ruby():
    for cand in (shutil.which("ruby"), r"C:\Ruby40-x64\bin\ruby.exe"):
        if cand and os.path.exists(cand):
            return cand
    sys.exit("Couldn't find Ruby, which is needed to run scripts_combine.rb.")


def build_launcher():
    step("Building Launcher.exe")
    bat = os.path.join(ROOT, "launcher", "build.bat")
    if subprocess.call(["cmd", "/c", bat]) != 0:
        sys.exit("The launcher didn't build; see the messages above.")


def copy_game(src, dest):
    print("Copying %s\n     -> %s" % (src, dest))
    shutil.copytree(src, dest, ignore=shutil.ignore_patterns(".git", "launcher_data"))


def rx(ruby, *args):
    """Runs tools/rxscripts.rb; returns (exit code, output)."""
    r = subprocess.run([ruby, os.path.join(HERE, "rxscripts.rb")] + list(args), capture_output=True, text=True)
    return r.returncode, (r.stdout + r.stderr).strip()


def combine_scripts(folder, ruby, trust_files=False):
    """Makes the copy's Data/Scripts.rxdata from its Data/Scripts folder (the
    same thing scripts_combine.rb does) and removes the folder. If the game
    folder also had combined scripts, they must match the folder, otherwise
    it isn't clear which ones are current, so it stops."""
    scripts_dir = os.path.join(folder, "Data", "Scripts")
    rx_path = os.path.join(folder, "Data", "Scripts.rxdata")
    is_loader = os.path.getsize(rx_path) < 50 * 1024
    if not os.path.isdir(scripts_dir):
        if is_loader:
            sys.exit("Scripts.rxdata is only a loader, but there's no Data/Scripts folder. Stopping.")
        print("No Data/Scripts folder; using the combined Scripts.rxdata as it is.")
        return
    if not os.path.exists(os.path.join(folder, "scripts_combine.rb")):
        sys.exit("scripts_combine.rb is missing from the game folder.")
    previous = rx_path + ".previous"
    shutil.copyfile(rx_path, previous)
    rx(ruby, "empty", rx_path)   # scripts_combine.rb only rebuilds when Scripts.rxdata is a loader
    print("Running scripts_combine.rb ...")
    subprocess.check_call([ruby, "scripts_combine.rb"], cwd=folder)
    if os.path.getsize(rx_path) < 50 * 1024:
        sys.exit("scripts_combine.rb ran, but Scripts.rxdata still looks empty. Stopping.")
    if not is_loader and not trust_files:
        code, out = rx(ruby, "compare", previous, rx_path)
        if code != 0:
            sys.exit("Your game folder has both combined scripts (Data/Scripts.rxdata) and a Data/Scripts\n"
                     "folder, and they don't match, so it's not clear which has your latest changes.\n"
                     + out + "\n"
                     "  - If your latest edits are in the Data/Scripts files, run the release again with\n"
                     "    --use-script-files at the end (e.g. release.bat 2.0.0 --use-script-files).\n"
                     "  - If you made your latest edits in RPG Maker's script editor, run scripts_extract.rb\n"
                     "    in the game folder first (it updates Data/Scripts from them), then run this again.\n"
                     "Nothing was released.")
    os.remove(previous)
    shutil.rmtree(scripts_dir)
    print("Combined into Data/Scripts.rxdata (%s) and removed Data/Scripts." % make_release.mb(os.path.getsize(rx_path)))


def disable_setcontrols(folder, ruby):
    """Turns SetControls off inside the copy's combined Scripts.rxdata (Android)."""
    rx_path = os.path.join(folder, "Data", "Scripts.rxdata")
    code, out = rx(ruby, "disable", rx_path, SETCONTROLS_SECTION)
    if code != 0:
        sys.exit("Couldn't switch off SetControls for Android: " + out)
    code, out = rx(ruby, "show", rx_path, SETCONTROLS_SECTION)
    if code != 0 or not re.search(r"ENABLED\s*=\s*false", out):
        sys.exit("SetControls still isn't switched off in the Android copy (%s). Stopping." % out)
    print("Android copy: %s -> %s" % (SETCONTROLS_SECTION, out))


def extract_zip(zip_path, dest):
    """Unzips a shipped release, dropping its top 'Pokemon Copper/' folder."""
    print("Unzipping %s ..." % zip_path)
    with zipfile.ZipFile(zip_path) as z:
        names = z.namelist()
        roots = {n.split("/")[0] for n in names}
        prefix = (roots.pop() + "/") if len(roots) == 1 else ""
        for info in z.infolist():
            rel = info.filename[len(prefix):]
            if not rel:
                continue
            target = os.path.join(dest, rel.replace("/", os.sep))
            if info.is_dir():
                os.makedirs(target, exist_ok=True)
            else:
                os.makedirs(os.path.dirname(target), exist_ok=True)
                with z.open(info) as src, open(target, "wb") as out:
                    shutil.copyfileobj(src, out)


def shipped_files(folder):
    """The files and empty folders that go in a full download (same rules as the launcher)."""
    patterns = SETTINGS.get("exclude", [])
    files, dirs = [], []
    for dirpath, dirnames, filenames in os.walk(folder):
        dirnames.sort()
        rel_dir = os.path.relpath(dirpath, folder).replace(os.sep, "/")
        kept = [f for f in sorted(filenames)
                if not make_release.excluded((f if rel_dir == "." else rel_dir + "/" + f), patterns)]
        files += [(f if rel_dir == "." else rel_dir + "/" + f) for f in kept]
        if rel_dir != "." and not make_release.excluded(rel_dir + "/", patterns) and not filenames and not dirnames:
            dirs.append(rel_dir)
    return files, dirs


def write_full_zip(folder, out_path, launcher_exe=None):
    print("Zipping %s ..." % os.path.basename(out_path))
    files, dirs = shipped_files(folder)
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for d in dirs:
            z.writestr(ZIP_ROOT + d + "/", "")
        for rel in files:
            z.write(os.path.join(folder, rel.replace("/", os.sep)), ZIP_ROOT + rel)
        if launcher_exe:
            z.write(launcher_exe, ZIP_ROOT + "Launcher.exe")
    print("  %d files, %s" % (len(files), make_release.mb(os.path.getsize(out_path))))


def main():
    ap = argparse.ArgumentParser(description="Make everything for a new Pokemon Copper release.")
    ap.add_argument("version", help="e.g. 2.0.0")
    ap.add_argument("--from-zip", help="build from an already-shipped PC zip instead of the game folder")
    ap.add_argument("--android-zip", help="with --from-zip: the matching Android zip, shipped as-is")
    ap.add_argument("--game", help="game folder (default from release_settings.json)")
    ap.add_argument("--releases-dir", default=os.path.join(ROOT, "releases"))
    ap.add_argument("--replace", action="store_true", help="rebuild this version if you haven't uploaded it yet")
    ap.add_argument("--no-android", action="store_true", help="skip the Android download")
    ap.add_argument("--use-script-files", action="store_true",
                    help="if Scripts.rxdata and Data/Scripts disagree, use the Data/Scripts files")
    ap.add_argument("--keep-build", action="store_true", help="keep the build/ folder afterwards (for checking)")
    args = ap.parse_args()

    version = args.version.lstrip("vV")
    tag = "v" + version
    game = os.path.abspath(args.game or os.path.join(ROOT, SETTINGS["game_folder"]))
    build = os.path.join(ROOT, "build", tag)
    pc = os.path.join(build, "pc")
    android = os.path.join(build, "android")
    if os.path.exists(build):
        shutil.rmtree(build)
    os.makedirs(build)

    try:
        build_launcher()
        launcher_exe = os.path.join(ROOT, "launcher", "dist", "Launcher.exe")

        step("Preparing the PC copy")
        ruby = find_ruby()
        if args.from_zip:
            extract_zip(args.from_zip, pc)
        else:
            copy_game(game, pc)
            combine_scripts(pc, ruby, args.use_script_files)
            if not args.no_android:
                step("Preparing the Android copy")
                print("Copying the PC copy ...")
                shutil.copytree(pc, android)
                disable_setcontrols(android, ruby)
                code, out = rx(ruby, "show", os.path.join(pc, "Data", "Scripts.rxdata"), SETCONTROLS_SECTION)
                print("PC copy:      %s -> %s" % (SETCONTROLS_SECTION, out))

        step("Building the launcher's update files")
        info = make_release.build_release(version, pc, args.releases_dir, args.replace)
        out_dir = info["out_dir"]

        step("Building the full downloads")
        write_full_zip(pc, os.path.join(out_dir, PC_ZIP), launcher_exe)
        if args.from_zip and args.android_zip:
            shutil.copyfile(args.android_zip, os.path.join(out_dir, ANDROID_ZIP))
            print("Copied %s as %s" % (os.path.basename(args.android_zip), ANDROID_ZIP))
        elif not args.from_zip and not args.no_android:
            write_full_zip(android, os.path.join(out_dir, ANDROID_ZIP))
        else:
            print("No Android download this time.")
    finally:
        if not args.keep_build:
            shutil.rmtree(os.path.join(ROOT, "build"), ignore_errors=True)

    step("All done! Upload everything in this folder to a new GitHub release tagged %s:" % tag)
    print("  " + out_dir)
    total = 0
    for fn in sorted(os.listdir(out_dir)):
        size = os.path.getsize(os.path.join(out_dir, fn))
        total += size
        print("    - %s (%s)" % (fn, make_release.mb(size)))
    print("  Total: %s" % make_release.mb(total))


if __name__ == "__main__":
    main()
