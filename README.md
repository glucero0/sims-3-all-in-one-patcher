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
