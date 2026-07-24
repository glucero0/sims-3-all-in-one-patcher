# The Sims 3 Modern Compatibility Patcher (.NET 8 WPF Self-Contained)

Native **C# .NET 8 WPF** app for Windows 11. Publishes to a single self-contained `Sims3ModernPatcher.exe` with no extra runtime install.

## Alpha software and disclaimer

> [!WARNING]
> This application is alpha software. It is in active development, largely untested, and may
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
- **Detects your PC**: CPU, GPU, and Windows edition/build (not hardcoded).
- **Finds Sims 3**: Steam (all libraries via `libraryfolders.vdf`), EA App / Origin registry + common folders, plus Uninstall entries. Lets you browse if auto-detect misses.
- **Downloads & applies modern fixes**:
  - 4GB Large Address Aware EXE flag
  - Correct modern-GPU entries in `GraphicsCards.sgr` and the 1024 MB unknown-GPU texture fallback
  - Ultimate ASI Loader (`wininet.dll`)
  - [Sims 3 Settings Setter](https://github.com/sims3fiend/Sims3SettingsSetter) (hybrid CPU / smooth gameplay)
  - Optional **DXVK** when it conflicts with native DirectX (asked for AMD/Intel/unknown GPUs)
  - NRaas stability mods into `Mods\Packages`: **ErrorTrap** (correct Steam/EA variant), **Overwatch**, **Traveler**, **Saver**
  - Mods `Resource.cfg`, reliable cache-purge launcher, desktop shortcut
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
- **Asks only on conflicts** (multiple installs, or DXVK vs DirectX). Then one **GO** button.
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
