# Pokémon Copper Launcher

`Launcher.exe` sits next to `Game.exe`. When a player opens it, it:

1. Updates itself if there's a newer launcher.
2. Checks GitHub for the newest game version and downloads only the files that changed, checking every file it installs.
3. Shows the patch notes, then starts the game when the player clicks **PLAY**.

Saves aren't touched: Essentials keeps them in AppData, not in the game folder. Releases live on the public **IanF7/PokemonCopperLauncher** repository's Releases page. Players can't download from a private repository.

## Releasing an update

1. Finish the update and change the version in `mkxp.json`'s `windowTitle`.
2. Add the new version's notes to the top of `patchnotes.txt` (format below).
3. If you changed any plugins, start the game once from RPG Maker so `PluginScripts.rxdata` is up to date.
4. Run **`release.bat`** and type the version, for example `2.0.0`. It takes about 4 minutes and doesn't change your game folder. It:
   - copies the game into `build/`, runs `scripts_combine.rb` on the copy, and removes `Data/Scripts`
   - leaves out `scripts_combine.rb`, `scripts_extract.rb`, `Game.rxproj`, `Town Map Generator.html` and `Essentials Docs Wiki.URL`
   - makes the Android copy with SetControls' `ENABLED = false`
   - builds the launcher's update files and the full downloads
   - puts everything to upload in `releases\v2.0.0\upload\`, then deletes `build/`
5. On GitHub, go to **PokemonCopperLauncher → Releases → Draft a new release**, set the tag to `v2.0.0` (with the `v`), drag in **every file** from that `upload` folder, and click **Publish release**.
6. Commit and push this folder. This saves `releases\history.txt` and the new manifest, which the next release is compared against.

Players' launchers pick up the update the next time they open them.

### What's in the upload folder

| File | What it's for |
|---|---|
| `Pokemon-Copper.zip` | Full download for Windows, Mac, Linux and Steam Deck (includes `Launcher.exe`) |
| `Pokemon-Copper-Android.zip` | Full download for JoiPlay |
| `files-v….zip`, `manifest.txt`, `patchnotes.txt` | What the launcher downloads |
| `Launcher.exe` | The newest launcher; players' launchers update themselves from it |

The two full zips have the same name every release, so these links always point at the newest version and never need changing on the website:

- `https://github.com/IanF7/PokemonCopperLauncher/releases/latest/download/Pokemon-Copper.zip`
- `https://github.com/IanF7/PokemonCopperLauncher/releases/latest/download/Pokemon-Copper-Android.zip`

### If release.bat stops about scripts

Your game folder can hold scripts in two places: combined in `Data/Scripts.rxdata`, and split into the `Data/Scripts` folder. If both exist and don't match, release.bat stops instead of guessing which one is current:

- If your latest edits are in the `Data/Scripts` files, run it again as `release.bat 2.0.0 --use-script-files`.
- If you made your latest edits in RPG Maker's script editor, run `scripts_extract.rb` in the game folder first, then run release.bat again.

### Changing the launcher

Edit the code in `launcher/src/`, then raise `Version` near the top of `launcher/src/Program.cs` (for example `1.1.0` to `1.2.0`). The next release ships the new launcher, and players' launchers replace themselves with it.

## The very first release (1.9.2)

Publish 1.9.2 first, built from the zips you put on MEGA, so current players only download what changed in 2.0.0:

```
python tools\release.py 1.9.2 --from-zip "..\Releases\Pokemon Copper 1.9.2.zip" --android-zip "..\Releases\Pokemon Copper 1.9.2 Android.zip"
```

Upload `releases\v1.9.2\upload\` as release `v1.9.2`, then release 2.0.0 normally.

## Patch notes format

```
# Version 2.0.0
## Bug Fixes
- Fixed a thing.
  - A detail under it (indent two spaces).
## Balancing
- Changed a thing.
```

## For players (wording for the website later)

- **Windows:** unzip, then open `Launcher.exe` instead of `Game.exe`.
- **Linux:** `wine Launcher.exe` instead of `wine Game.exe`.
- **Mac (Porting Kit):** point Porting Kit at `Launcher.exe` instead of `Game.exe`.
- **Steam Deck:** add `Launcher.exe` to Steam instead of `Game.exe`.
- **Android (JoiPlay):** download `Pokemon-Copper-Android.zip` each update, as before.
- **Already have the game?** Put `Launcher.exe` into your current game folder and open it. The first check takes a minute or two; after that it only downloads what's different.
- Windows may say "Windows protected your PC" the first time. Click **More info → Run anyway**.

## What's in this folder

| Path | What it is |
|---|---|
| `release.bat` / `tools/release.py` | The one-step release |
| `tools/make_release.py` | Builds the launcher's update files (used by release.py) |
| `tools/rxscripts.rb` | Combines scripts and switches SetControls off for Android (used by release.py) |
| `tools/serve_local.py` | A pretend GitHub server for testing on your PC |
| `tools/import_patchnotes.py`, `tools/extract_icon.py` | One-time helpers, already run |
| `launcher/` | The launcher's code. `launcher/build.bat` builds `launcher/dist/Launcher.exe` |
| `releases/` | Release history. Keep it; each release is compared against the last one |

## Things to know

- **Not tested on Wine, Mac or Steam Deck.** Ask a player on each to try the launcher before announcing it.
- **Test options:** `Launcher.exe --headless` updates without a window. Both that and normal runs write to `launcher_data/launcher.log`, which is the first place to look when a player reports a problem.
