# The Sims 3 Modern Compatibility Patcher (.NET 8 WPF Self-Contained)

Native **C# .NET 8 WPF** app for Windows 11. Publishes to a single self-contained `Sims3ModernPatcher.exe` with no extra runtime install.

## Software Disclaimer

> [!WARNING]
> This application is alpha software. It is in active development, minimally untested, and may
> contain defects that damage a Sims 3 installation, mods, settings, or saved games. Use it
> entirely at your own risk and keep independent backups of anything important.

This software is provided **as is**, without guarantees or warranties of any kind, express or
implied. There is no guarantee that it will work with any particular computer, Sims 3 release,
storefront, mod configuration, or future third-party download. The authors and contributors
accept no responsibility for data loss, corruption, service interruption, or other damage
resulting from its use.

The application was designed and tested by **Gary Lucero**, but was largely written with
**Cursor and Grok 4.5**. AI-generated code can contain subtle or unexpected errors and should
not be treated as a substitute for independent review and testing.

## Tested configuration (early results)

> [!NOTE]
> Despite the warning above, limited real-world testing on one contemporary desktop has been
> encouraging. On this system, The Sims 3 launche and can be played successfully in both a fresh
> new-world save and an existing, larger legacy save after patching.
>
> That test machine was a 64-bit **Windows 11 Home** desktop with:
>
> - **CPU**: Intel Core Ultra 5 (Arrow Lake generation, LGA1851)
> - **GPU**: NVIDIA GeForce RTX 5050 (8 GB GDDR6)
> - **RAM**: 16 GB DDR5
> - **Storage**: 1 TB PCIe NVMe Gen4 SSD
> - **EA Store**: Game and DLC purchased/downloaded with EA app
> - **Mods**: No mods outside of those installed by this patch utility
>
> This is a single data point, not a guarantee for other hardware, storefronts, mod sets, or
> save files. Your mileage may vary.

## If the patcher (or the game after patching) does not work

Please [open a GitHub issue](https://github.com/glucero0/sims-3-all-in-one-patcher/issues/new)
and include as much of the checklist below as you can. Exact logs and error text matter more
than technical detail — you do not need to explain *why* something failed.

### 1. Say what went wrong (in plain language)

- Did **the patcher itself** fail (red error popup, Patch button did not finish)?
- Or did Patch say **success**, but **The Sims 3** still crashes, freezes, shows a Serious Error,
  black screens, or will not load a world / save?
- What were you trying to do when it failed (first run of the patcher, re-run, launching the
  game, loading a save, creating a new game, etc.)?
- Copy the **exact text** from any error popup, or paste a screenshot of it.

### 2. Attach the patcher log (most important)

Every run writes a log under:

`%LOCALAPPDATA%\Sims3ModernPatcher\Logs\`

(full path example:
`C:\Users\<YourName>\AppData\Local\Sims3ModernPatcher\Logs\`)

**Easiest way:** in the patcher, click **Open Logs**, then attach the newest
`patcher-….log` file to the GitHub issue.

That log already records your detected CPU / GPU / Windows edition, Sims 3 folder and
storefront (Steam / EA App / other), game version when found, whether DXVK was selected, and
each step until the failure. Attaching it is usually enough for a useful diagnosis.

### 3. Fill in a short environment snapshot

You can copy most of this from the top of the patcher window (or from the log):

| What | Example / where to find it |
| --- | --- |
| Windows edition | Shown as **Windows** in the patcher (e.g. Windows 11 Home 24H2, 64-bit) |
| CPU | Shown as **Processor** |
| GPU | Shown as **Graphics** (mention a laptop / dual-GPU setup if you have one) |
| Storefront | Steam, EA App, disc / other — and the install path shown in the patcher |
| DXVK checkbox | Was **Install DXVK** checked or unchecked when you clicked Patch? |
| Ran as Administrator? | Yes / No (right-click → Run as administrator) |
| Other mods? | Any mods **not** installed by this patcher? (list names if you know them) |
| Which EXE | Approximate download date, or the release / build you used |

### 4. Extra files — only if the *game* misbehaves after a successful Patch

Attach these if they exist (skip any that are missing):

- Sims 3 Documents folder (usually
  `Documents\Electronic Arts\The Sims 3\`, or under OneDrive Documents):
  - `Exception.log`
  - `LastException.txt`
  - `DeviceConfig.log`
  - `Options.ini`
- Inside the game install `Game\Bin\` folder:
  - `skuversion.txt` (game patch version)
  - `TS3_d3d9.log` (only if present — DXVK write-up of graphics startup)
- Optional: a screenshot of the in-game error, or of the patcher’s Detected panel if CPU/GPU/install look wrong.

Do **not** upload full save files or large save-backup ZIPs unless a maintainer asks for them.
Save backups live at `%LOCALAPPDATA%\Sims3ModernPatcher\SaveBackups\` (**Open Save Backups**
in the UI) and are for your recovery, not for routine bug reports.

### 5. How to report it

1. Gather the items above (at minimum: short description + newest patcher log + popup text).
2. Open a new issue:
   [https://github.com/glucero0/sims-3-all-in-one-patcher/issues/new](https://github.com/glucero0/sims-3-all-in-one-patcher/issues/new)
3. Use a clear title (e.g. “Patch fails downloading DXVK on Windows 11” or
   “Serious Error after successful Patch — NVIDIA RTX 4070”).
4. Paste the checklist answers in the body and attach the log / extra files.

That package is what maintainers (and automated helpers) need to reproduce the failure path,
tell download vs install vs post-launch graphics/mod problems apart, and ship a fix.

## Intended mutation scope

This is intentionally an invasive repair/installation tool. With Administrator approval it may:

- overwrite or disable files inside the selected Sims 3 installation after backing them up;
- install package mods in the Sims 3 Documents folder;
- create app-owned download caches and save snapshots under Local AppData;
- create or replace its Sims 3 desktop shortcut;
- require Sims 3 and its launcher to be closed while files are changed; and
- schedule a Windows restart only after the user explicitly chooses **Yes**.

It does not modify unrelated applications, services, documents, or registry values. Sims 3/EA
process or service management may be added when a specific compatibility repair requires it,
but must remain explicitly limited to known Sims 3/EA names.

## What it does
- **Detects your PC**: CPU, GPU, and Windows edition/build.
- **Finds Sims 3**: Steam (all libraries via `libraryfolders.vdf`), EA App / Origin registry + common folders, plus Uninstall entries. Lets you browse if auto-detect misses.
- **Downloads & applies modern fixes**:
  - 4GB Large Address Aware EXE flag
  - Sets modern-GPU entries in `GraphicsCards.sgr` and 'GraphicsRules.sgr' (1024 MB unknown-GPU texture fallback)
  - Ultimate ASI Loader (`wininet.dll`) (https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases)
  - Downloads/installs Sims 3 Settings Setter .asi (uses its defaults; patcher doesn’t tune hybrid/smooth). [Sims 3 Settings Setter](https://github.com/sims3fiend/Sims3SettingsSetter)
  - Optional **DXVK** (recommended for NVidia GPUs) (https://github.com/doitsujin/dxvk/releases)
  - NRaas stability mods into `Mods\Packages`: **ErrorTrap** (correct Steam/EA variant), **Overwatch**, **Traveler**, **Saver** (https://www.nraas.net/community/Mods-List)
  - Mods `Resource.cfg` (creates if missing) 
  - Reliable cache-purge launcher (writes batch file)
  - Desktop shortcut (optional)
- **Permission**: only applies patches once user gives express Permission
- **Protects saves first**: creates a timestamped ZIP under
  `%LOCALAPPDATA%\Sims3ModernPatcher\SaveBackups` before patching and keeps the newest ten.
  Backups are never restored automatically.
- **Writes a patcher log**: every progress line is also saved under
  `%LOCALAPPDATA%\Sims3ModernPatcher\Logs\patcher-*.log` (Open Logs button in the UI).
- **Verifies downloads**: pinned third-party releases are SHA-256 checked before extraction.
- **Avoids partial installs**: all downloads finish before game files change, and a per-run
  rollback snapshot restores touched files if a later local step fails.
- **Launches through the correct platform**: Steam shortcuts use Steam AppID 47890;
  EA/retail installs use the detected `TS3.exe` or `TS3W.exe`.
- **Asks only on conflicts** (multiple installs, or DXVK vs DirectX). Then one **Patch** button.
- On success: shows a message and offers a restart.

## Build the standalone EXE

```cmd
dotnet publish Sims3ModernPatcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./Output/Sims3Patcher
```

Or double-click `build-standalone-exe.bat`.

Output: `./Output/Sims3Patcher/Sims3ModernPatcher.exe`

The executable requests Administrator access because most game installs live under `Program Files`.

## Tests

```cmd
dotnet test Sims3ModernPatcher.sln -c Release
```

The suite covers executable/version selection, Steam/EA detection helpers, PE patching,
graphics configuration edits, archive safety, save backups, launcher behavior, and WPF UI components.

### CI (GitHub Actions)

PRs and pushes to `main` run the same tests on `windows-latest` via `.github/workflows/ci.yml`
(WPF requires a Windows runner).

**Enable / verify:**

1. Push this workflow (or merge this branch) so `.github/workflows/ci.yml` exists on GitHub.
2. Repo **Settings → Actions → General**: allow Actions (and allow GitHub-hosted runners).
   Public repos usually already allow this; private repos may need Actions turned on.
3. Open a PR (or re-run checks) and confirm the **CI / Test** check appears on the PR.
4. Optional — require it before merge: **Settings → Branches → Branch protection** (or rulesets)
   for `main` → require status check **Test** (or the full job name shown on the PR).
