# Pokémon Copper Launcher

A small `Launcher.exe` that sits next to `Game.exe`. When a player opens it, it:

1. Checks GitHub for the newest version.
2. Downloads only the files that changed, then checks every file it installs.
3. Shows the patch notes, then starts the game when the player clicks **PLAY**.

Saves aren't touched. Essentials keeps them in AppData, not in the game folder.

## What's in this folder

| Path | What it is |
|---|---|
| `launcher/` | The launcher's source code. `launcher/build.bat` builds `launcher/dist/Launcher.exe`. |
| `patchnotes.txt` | The patch notes the launcher shows. Edit this for each update (newest at the top). |
| `release_settings.json` | Where your game folder is and which dev files to leave out of releases. |
| `tools/make_release.py` | Builds the files to upload for a new version. |
| `tools/serve_local.py` | A pretend GitHub server for testing on your PC. |
| `tools/import_patchnotes.py` | Copies patch notes from the website's `downloads.html` (read-only). Already run once. |
| `test_locally.bat` | One-step local testing (see below). |
| `releases/` | Release history. **Keep this folder.** Each new release is compared against the last one. |

Nothing here needs installing. The launcher is built with the C# compiler that comes with Windows, and the tools use the Python you already have.

## Testing on your PC

1. Double-click or run `test_locally.bat 1.9.2`.
   - This builds a pretend 1.9.2 release from your game folder into `test/releases/`.
   - It opens a "test server" window. Leave it open; it pretends to be GitHub.
   - It opens the launcher in `test/Pokemon Copper/`. Because that folder starts empty, you'll see a fresh install.
2. Click **INSTALL**, then **PLAY**.
3. To test an update, change something in the game (or copy the game folder and change the copy), add a `# Version 1.9.3` section to the top of `patchnotes.txt`, then run `test_locally.bat 1.9.3` (add the copy's path at the end if you used one). The launcher should offer **UPDATE** and download only what changed.
4. To pretend to be a player who downloaded from MEGA, unzip an old release somewhere, put `Launcher.exe` and the `launcher.ini` from `test/Pokemon Copper/` into it, and run it.

Delete the `test` folder whenever you want to start over.

## Making a real release (once you're ready to go live)

1. Finish the update in RPG Maker as usual, and update the version in `mkxp.json`'s `windowTitle`.
   Run `scripts_combine.rb` like you do before zipping. The release tool refuses to run while the scripts are still split out into `Data/Scripts`.
2. Add the new version's notes to the top of `patchnotes.txt`.
3. Run `python tools/make_release.py 1.9.3`.
   Everything to upload lands in `releases/v1.9.3/upload/`: one or more `files-….zip`, `manifest.txt` and `patchnotes.txt`.
   Add `--full-zip` to also get a normal full-game zip (with the launcher inside) for your website.
4. On GitHub, go to the repository → **Releases** → **Draft a new release**, set the tag to `v1.9.3`, drag in **everything** from the `upload` folder, and click **Publish release**.

Once it's published, every launcher picks up the update the next time it opens.

### The very first release: start from the 1.9.2 zip

To keep existing players from re-downloading the whole game, make the first GitHub release **1.9.2, built from the exact zip you put on MEGA**:

```
python tools/make_release.py 1.9.2 --game "C:\path\to\unzipped\Pokemon Copper 1.9.2\Pokemon Copper"
```

Upload that as release `v1.9.2`, then make 1.9.3 normally. A player with the MEGA copy of 1.9.2 who drops in the launcher then downloads only what changed in 1.9.3. If you skip this step, they still end up with a working game, but the download is bigger.

### Where releases go

`Launcher.exe` currently looks at **github.com/IanF7/PokemonCopper/releases** (your website's repository). Releases don't change the website. If you'd rather use a separate repository, change `ReleaseBase` at the top of `launcher/src/Config.cs` and run `launcher/build.bat` again.

GitHub's limits are 2 GB per file, with no total limit for releases. Release zips are split into roughly 32 MB pieces, so repairing one damaged file doesn't mean re-downloading the whole game.

## Patch notes format

```
# Version 1.9.3
## Bug Fixes
- Fixed a thing.
  - A detail under it (indent two spaces).
## Balancing
- Changed a thing.
```

## For players (draft for the website later)

- **Windows:** unzip, then open `Launcher.exe` instead of `Game.exe`.
- **Linux:** `wine Launcher.exe` instead of `wine Game.exe`.
- **Mac (Porting Kit):** point Porting Kit at `Launcher.exe` instead of `Game.exe`.
- **Steam Deck:** add `Launcher.exe` to Steam instead of `Game.exe`.
- **Android (JoiPlay):** still updated by hand.
- Already have the game? Drop `Launcher.exe` into your current game folder and open it. It checks your files and downloads only what's different.
- Windows may show "Windows protected your PC" the first time. Click **More info → Run anyway**.

## Not done yet / things to know

- **Wine, Mac and Steam Deck haven't been tested.** The launcher uses .NET, which Wine provides through "wine-mono" (Steam Deck's Proton includes it, and plain Wine usually offers to install it). Ask a Linux, Mac or Steam Deck player to try it before announcing.
- **The launcher can't update itself yet.** If `Launcher.exe` changes, players need the new one by hand. This could be added later.
- **Test options:** `Launcher.exe --headless` updates without a window, and `Launcher.exe --screenshot file.png` saves a picture of the window. Both write to `launcher_data/launcher.log`, which is also the first place to look when a player reports a problem.
