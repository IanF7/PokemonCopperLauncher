"""Builds the launcher's update files for a game version, from a ready-to-ship
game folder. (For a normal release, use release.bat instead: it prepares the
folder, calls this, and also makes the full downloads.)

Usage:
    python tools/make_release.py 1.9.3
    python tools/make_release.py 1.9.3 --replace      rebuild a version you haven't uploaded yet

Options for testing:
    --game <folder>          use a different game folder than release_settings.json
    --releases-dir <folder>  keep the release history somewhere else (default: releases/)

What it does:
  1. Fingerprints every file in the game folder.
  2. Compares against the previous release to find what's new or changed.
  3. Puts only those files in files-v<version>.zip.
  4. Writes manifest.txt (the full file list) and copies patchnotes.txt.
Everything to upload ends up in releases/v<version>/upload/.
"""
import argparse
import re
import fnmatch
import hashlib
import json
import os
import shutil
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MAGIC = "pokemon-copper-manifest\t1"


def sha1_of(path):
    h = hashlib.sha1()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def excluded(rel, patterns):
    # Patterns match the whole path from the game folder, e.g. "Game.rxproj" only
    # matches the one at the top. "*" also matches across folders.
    low = rel.lower()
    return any(fnmatch.fnmatchcase(low, p.lower()) for p in patterns)


def scan_game(folder, patterns):
    """Returns ({path: (size, sha1)}, [empty folders])."""
    files = {}
    all_dirs = []
    for dirpath, dirnames, filenames in os.walk(folder):
        dirnames.sort()
        if dirpath != folder:
            rel_dir = os.path.relpath(dirpath, folder).replace(os.sep, "/")
            if not excluded(rel_dir + "/", patterns):
                all_dirs.append(rel_dir)
        for fn in sorted(filenames):
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, folder).replace(os.sep, "/")
            if excluded(rel, patterns):
                continue
            if "\t" in rel or "\n" in rel:
                sys.exit("File name contains a tab or newline, please rename it: " + rel)
            files[rel] = (os.path.getsize(full), sha1_of(full))
    # Folders with nothing in them still get created (the game ships a few).
    used = set()
    for rel in files:
        parts = rel.split("/")[:-1]
        for i in range(1, len(parts) + 1):
            used.add("/".join(parts[:i]))
    empty = sorted(d for d in all_dirs if d not in used)
    return files, empty


def read_manifest(path):
    m = {"version": "", "tag": "", "launch": "Game.exe", "packs": [], "files": {}, "deletes": []}
    with open(path, encoding="utf-8") as f:
        lines = f.read().split("\n")
    if not lines or not lines[0].startswith("pokemon-copper-manifest"):
        sys.exit("Not a manifest: " + path)
    for line in lines[1:]:
        if not line:
            continue
        p = line.split("\t")
        if p[0] in ("version", "tag", "launch"):
            m[p[0]] = p[1]
        elif p[0] == "pack":
            m["packs"].append({"tag": p[2], "name": p[3], "size": int(p[4]), "sha1": p[5]})
        elif p[0] == "file":
            pack = m["packs"][int(p[1])]
            m["files"][p[4]] = (int(p[2]), p[3], pack["name"])
        elif p[0] == "delete":
            m["deletes"].append(p[1])
    return m


def load_history(releases_dir):
    path = os.path.join(releases_dir, "history.txt")
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return [l.strip() for l in f if l.strip()]


def save_history(releases_dir, tags):
    with open(os.path.join(releases_dir, "history.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(tags) + "\n")


def mb(n):
    return "%.1f MB" % (n / 1048576.0)


def launcher_version():
    """The version number written in launcher/src/Program.cs."""
    src = open(os.path.join(ROOT, "launcher", "src", "Program.cs"), encoding="utf-8").read()
    m = re.search(r'Version\s*=\s*"([0-9.]+)"', src)
    return m.group(1) if m else None


def build_release(version, game=None, releases_dir=None, replace=False, allow_extracted_scripts=False):
    """Builds the launcher's update files for one version. Returns a dict with
    the upload folder, the list of shipped files and the empty folders."""
    class _Args:
        pass
    args = _Args()
    args.version, args.game, args.replace = version, game, replace
    args.releases_dir = releases_dir or os.path.join(ROOT, "releases")
    args.allow_extracted_scripts = allow_extracted_scripts

    version = args.version.lstrip("vV")
    tag = "v" + version
    settings = json.load(open(os.path.join(ROOT, "release_settings.json"), encoding="utf-8"))
    game = os.path.abspath(args.game or os.path.join(ROOT, settings["game_folder"]))
    rel_dir = os.path.abspath(args.releases_dir)
    os.makedirs(rel_dir, exist_ok=True)
    launch = settings.get("launch", "Game.exe")

    if not os.path.isfile(os.path.join(game, launch)):
        sys.exit("Couldn't find %s in %s" % (launch, game))

    history = load_history(rel_dir)
    if tag in history:
        if not args.replace:
            sys.exit("%s was already built. Use --replace to rebuild it (only if you haven't uploaded it yet)." % tag)
        if history[-1] != tag:
            sys.exit("Can only --replace the newest release (%s)." % history[-1])
        history.pop()
    prev = None
    if history:
        prev = read_manifest(os.path.join(rel_dir, history[-1], "upload", "manifest.txt"))

    # Friendly sanity checks.
    notes_path = os.path.join(ROOT, "patchnotes.txt")
    notes = open(notes_path, encoding="utf-8").read() if os.path.exists(notes_path) else ""
    first_heading = next((l[2:].strip() for l in notes.splitlines() if l.startswith("# ")), "")
    if version not in first_heading.split():
        print("WARNING: the top of patchnotes.txt is '%s', not 'Version %s'." % (first_heading, version))
    try:
        mkxp = open(os.path.join(game, "mkxp.json"), encoding="utf-8").read()
        if "v" + version + '"' not in mkxp:
            print("WARNING: mkxp.json's windowTitle doesn't seem to say v%s." % version)
    except OSError:
        pass

    # Essentials can run with its scripts split out into Data/Scripts/ (Scripts.rxdata is
    # then a tiny loader). Releases should have them combined back into Scripts.rxdata.
    patterns = list(settings.get("exclude", []))
    scripts_rx = os.path.join(game, "Data", "Scripts.rxdata")
    extracted = (os.path.isdir(os.path.join(game, "Data", "Scripts"))
                 and os.path.isfile(scripts_rx) and os.path.getsize(scripts_rx) < 50 * 1024)
    if extracted:
        if not args.allow_extracted_scripts:
            sys.exit("Your scripts are currently extracted into Data/Scripts (Scripts.rxdata is only a loader).\n"
                     "Run scripts_combine.rb in the game folder first, like you do before zipping a release,\n"
                     "then run this again.")
        print("Note: scripts are extracted, so the Data/Scripts folder is included in this release.")
        patterns = [p for p in patterns if p.lower() != "data/scripts/*"]

    print("Scanning %s ..." % game)
    files, empty_dirs = scan_game(game, patterns)
    print("  %d files, %s" % (len(files), mb(sum(s for s, _ in files.values()))))

    out_dir = os.path.join(rel_dir, tag, "upload")
    if os.path.exists(os.path.join(rel_dir, tag)):
        shutil.rmtree(os.path.join(rel_dir, tag))
    os.makedirs(out_dir)

    assigned = {}
    changed = []
    for rel, (size, sha) in files.items():
        old = prev["files"].get(rel) if prev else None
        if old and old[0] == size and old[1] == sha:
            assigned[rel] = old[2]
        else:
            changed.append(rel)
    changed.sort()

    # New/changed files go into zips of about pack_size_mb each, so fixing one
    # damaged file never means re-downloading the whole game.
    limit = int(settings.get("pack_size_mb", 32)) * 1048576
    chunks = [[]]
    chunk_bytes = 0
    for rel in changed:
        if chunks[-1] and chunk_bytes + files[rel][0] > limit:
            chunks.append([])
            chunk_bytes = 0
        chunks[-1].append(rel)
        chunk_bytes += files[rel][0]
    if not changed:
        chunks = []

    new_packs = []
    for i, chunk in enumerate(chunks):
        name = "files-%s.zip" % tag if len(chunks) == 1 else "files-%s-%02d.zip" % (tag, i + 1)
        zip_path = os.path.join(out_dir, name)
        print("Packing %d file(s) into %s ..." % (len(chunk), name))
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for rel in chunk:
                z.write(os.path.join(game, rel.replace("/", os.sep)), rel)
                assigned[rel] = name
        new_packs.append({"tag": tag, "name": name, "size": os.path.getsize(zip_path), "sha1": sha1_of(zip_path)})

    # Packs still in use, oldest first, then the new ones.
    in_use = set(assigned.values())
    pack_order = [p for p in prev["packs"] if p["name"] in in_use] if prev else []
    pack_order += new_packs
    pack_index = {p["name"]: i for i, p in enumerate(pack_order)}

    deletes = set()
    if prev:
        deletes.update(prev["deletes"])
        deletes.update(r for r in prev["files"] if r not in files)
    deletes = sorted(d for d in deletes if d not in files)

    lines = [MAGIC, "version\t" + version, "tag\t" + tag, "launch\t" + launch]
    for i, p in enumerate(pack_order):
        lines.append("pack\t%d\t%s\t%s\t%d\t%s" % (i, p["tag"], p["name"], p["size"], p["sha1"]))
    for rel in sorted(files):
        size, sha = files[rel]
        lines.append("file\t%d\t%d\t%s\t%s" % (pack_index[assigned[rel]], size, sha, rel))
    for d in empty_dirs:
        lines.append("dir\t" + d)
    for d in deletes:
        lines.append("delete\t" + d)
    # The newest launcher rides along with every release so players' launchers can update themselves.
    launcher_exe = os.path.join(ROOT, "launcher", "dist", "Launcher.exe")
    lver = launcher_version()
    if os.path.exists(launcher_exe) and lver:
        shutil.copyfile(launcher_exe, os.path.join(out_dir, "Launcher.exe"))
        lines.append("launcher\t%s\t%s\tLauncher.exe\t%d\t%s" % (lver, tag, os.path.getsize(launcher_exe), sha1_of(launcher_exe)))
    else:
        print("WARNING: launcher/dist/Launcher.exe not found; this release won't update players' launchers.")
    with open(os.path.join(out_dir, "manifest.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    if notes:
        shutil.copyfile(notes_path, os.path.join(out_dir, "patchnotes.txt"))

    history.append(tag)
    save_history(rel_dir, history)

    print()
    print("Done! %s" % tag)
    print("  Changed files: %d   Removed files: %d" % (len(changed), len([d for d in deletes if not prev or d not in prev["deletes"]])))
    if new_packs:
        print("  Players updating from %s download about %s" % (prev["tag"] if prev else "nothing",
                                                              mb(sum(p["size"] for p in new_packs))))
    print()
    print("Upload everything in this folder to a new GitHub release tagged %s:" % tag)
    print("  " + out_dir)
    for fn in sorted(os.listdir(out_dir)):
        print("    - %s (%s)" % (fn, mb(os.path.getsize(os.path.join(out_dir, fn)))))
    return {"tag": tag, "out_dir": out_dir, "files": files, "empty_dirs": empty_dirs, "game": game}


def main():
    ap = argparse.ArgumentParser(description="Build the launcher's update files for a version.")
    ap.add_argument("version", help="e.g. 1.9.3")
    ap.add_argument("--game", help="game folder (default from release_settings.json)")
    ap.add_argument("--releases-dir", default=os.path.join(ROOT, "releases"))
    ap.add_argument("--replace", action="store_true", help="rebuild this version if it already exists")
    ap.add_argument("--allow-extracted-scripts", action="store_true",
                    help="for testing: ship Data/Scripts as-is instead of requiring scripts_combine.rb")
    a = ap.parse_args()
    build_release(a.version, a.game, a.releases_dir, a.replace, a.allow_extracted_scripts)


if __name__ == "__main__":
    main()
