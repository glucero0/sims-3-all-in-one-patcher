using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Sims3ModernPatcher
{
    public sealed class PatcherEngine
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private const string AsiLoaderUrl = "https://api.github.com/repos/ThirteenAG/Ultimate-ASI-Loader/releases/assets/436296109";
        private const string AsiLoaderSha256 = "e5bb99c880faa39181997097fbde3ba553f4ac03bdb8f50421d1c7e4a003a57b";
        private const string S3ssUrl = "https://github.com/sims3fiend/Sims3SettingsSetter/releases/download/1.6.3/Sims3SettingsSetter.asi";
        private const string S3ssSha256 = "69ce87ee84528748ee1f19c1f7cb2183e8e8cb7f7cae57cdb64098a7ebcdc13a";
        private const string DxvkUrl = "https://github.com/doitsujin/dxvk/releases/download/v2.6.1/dxvk-2.6.1.tar.gz";
        private const string DxvkSha256 = "7ee0bef415910c943d3bda47d9d6821b9c8ca7a74f1e9f6151707d268cf3ce7f";

        public HardwareInfo DetectHardware() => HardwareDetector.Detect();

        public List<GameInstall> FindInstallations() => GameLocator.FindAllInstallations();

        public List<ConflictChoice> BuildConflicts(IReadOnlyList<GameInstall> installs, HardwareInfo hardware)
            => PatchCatalog.BuildConflicts(installs, hardware);

        public async Task<PatchResult> ApplyAsync(PatchPlan plan, Action<string> log)
        {
            string sims3Path = plan.Install.Path;
            if (!GameLocator.IsValidSims3Install(sims3Path))
            {
                log("[X] No valid Sims 3 install was selected.");
                throw new InvalidOperationException("Sims 3 installation was not found or is invalid.");
            }

            string binFolder = Path.Combine(sims3Path, "Game", "Bin");
            EnsureGameIsNotRunning();
            EnsureDirectoryWritable(binFolder);

            IReadOnlyList<string> gameExes = GameExecutableSelector.FindExisting(binFolder);
            if (gameExes.Count == 0)
            {
                log("[X] Neither TS3W.exe nor TS3.exe was found in Game\\Bin.");
                throw new InvalidOperationException("No Sims 3 game executable was found (TS3W.exe / TS3.exe).");
            }

            string? gameVersion = GameVersionReader.Read(sims3Path);
            string primaryExeName = GameExecutableSelector.SelectPrimary(
                gameExes,
                plan.Install.Platform,
                gameVersion);
            string primaryExePath = Path.Combine(binFolder, primaryExeName);
            string backupFolder = Path.Combine(binFolder, "Backup_Original_Win11");
            Directory.CreateDirectory(backupFolder);
            string sims3DocumentsPath = FindSims3DocumentsFolder();
            string saveBackupRoot = SaveBackupManager.GetBackupRoot();
            string? saveBackupPath = SaveBackupManager.CreateSnapshot(
                sims3DocumentsPath,
                saveBackupRoot,
                log);

            log($"[+] Platform: {plan.Install.PlatformLabel}");
            log($"[+] Game folder: {sims3Path}");
            log($"[+] CPU: {plan.Hardware.CpuDisplay}");
            log($"[+] GPU: {plan.Hardware.GpuDisplay}");
            log($"[+] OS: {plan.Hardware.OsName}");
            log($"[+] Game version: {gameVersion ?? "unknown"}");
            log($"[+] Game EXE(s): {string.Join(", ", gameExes)} (launch target: {primaryExeName})");

            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sims3ModernPatcher", "cache");
            string nraasCache = Path.Combine(cacheDir, "nraas");
            Directory.CreateDirectory(cacheDir);

            bool useDxvk = plan.Choices.TryGetValue(PatchCatalog.ChoiceGraphicsApi, out string? gfx)
                           && gfx == PatchCatalog.OptDxvk;

            // Complete every network operation and integrity check before changing game files.
            await PreflightCoreDownloadsAsync(cacheDir, useDxvk, log);
            await NRaasInstaller.PreflightDownloadsAsync(plan.Install, nraasCache, log);
            log("[SUCCESS] Download preflight complete; beginning local file changes.");

            using var rollback = new FileRollbackSession(log);
            CapturePatchTargets(
                rollback,
                binFolder,
                sims3DocumentsPath,
                gameExes,
                plan.CreateDesktopShortcut);

            foreach (string exeName in gameExes)
                ApplyLaaPatch(Path.Combine(binFolder, exeName), backupFolder, exeName, log);

            PatchGraphicsConfiguration(binFolder, backupFolder, plan.Hardware, log);
            string packagesDir = EnsureModsFolder(log);

            await InstallAsiLoaderAsync(binFolder, backupFolder, cacheDir, log);
            await InstallSims3SettingsSetterAsync(binFolder, cacheDir, log);

            if (useDxvk)
                await InstallDxvkAsync(binFolder, backupFolder, cacheDir, log);
            else
                RestoreNativeDirectX(binFolder, backupFolder, log);

            await NRaasInstaller.InstallStabilityModsAsync(plan.Install, packagesDir, nraasCache, log);

            // Remove obsolete single-purpose patches that conflict with S3SS.
            RemoveLegacyConflictingFiles(binFolder, log);

            string launcherBatPath = DeployReliableLauncher(
                binFolder,
                primaryExeName,
                plan.Install.Platform,
                sims3DocumentsPath,
                log);

            bool shortcutCreated = plan.CreateDesktopShortcut
                && CreateDesktopShortcut(launcherBatPath, binFolder, primaryExePath, log);

            rollback.Commit();
            log("[SUCCESS] All modern compatibility patches have been applied.");
            return new PatchResult
            {
                DesktopShortcutCreated = shortcutCreated,
                SaveBackupPath = saveBackupPath
            };
        }

        private static void CapturePatchTargets(
            FileRollbackSession rollback,
            string binFolder,
            string sims3DocumentsPath,
            IReadOnlyList<string> gameExes,
            bool includeDesktopShortcut)
        {
            foreach (string exeName in gameExes)
                rollback.Capture(Path.Combine(binFolder, exeName));

            string[] binFiles =
            {
                "GraphicsRules.sgr",
                "GraphicsRules.sfp",
                "GraphicsCards.sgr",
                "wininet.dll",
                "Sims3SettingsSetter.asi",
                "d3d9.dll",
                ".Sims3ModernPatcher.dxvk",
                "Sims3_Reliable_Launcher.bat",
                "TS3AlderLakePatch.ini",
                "TS3AlderLakePatch.dll",
                "TS3SmoothPatch.ini",
                "TS3SmoothPatch.asi",
                "SmoothPatch.asi"
            };
            foreach (string name in binFiles)
                rollback.Capture(Path.Combine(binFolder, name));
            rollback.Capture(Path.Combine(
                binFolder,
                "Backup_Original_Win11",
                "d3d9.dll.bak"));

            string disabledDir = Path.Combine(
                binFolder,
                "Backup_Original_Win11",
                "DisabledConflicts");
            foreach (string name in binFiles.Skip(8))
                rollback.Capture(Path.Combine(disabledDir, name));

            string modsRoot = Path.Combine(sims3DocumentsPath, "Mods");
            rollback.Capture(Path.Combine(modsRoot, "Resource.cfg"));
            string packages = Path.Combine(modsRoot, "Packages");
            foreach (string package in new[]
                     {
                         "NRaas_ErrorTrap.package",
                         "NRaas_Overwatch.package",
                         "NRaas_Traveler.package",
                         "NRaas_Saver.package"
                     })
            {
                rollback.Capture(Path.Combine(packages, package));
            }

            rollback.Capture(Path.Combine(sims3DocumentsPath, "S3SS", "config.toml"));

            if (includeDesktopShortcut)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                rollback.Capture(Path.Combine(desktop, "The Sims 3 (Reliable Win11).lnk"));
            }
        }

        private static void EnsureGameIsNotRunning()
        {
            string[] processNames = { "TS3W", "TS3", "Sims3Launcher", "Sims3LauncherW" };
            foreach (string processName in processNames)
            {
                Process[] processes = Process.GetProcessesByName(processName);
                try
                {
                    if (processes.Length > 0)
                    {
                        throw new InvalidOperationException(
                            "The Sims 3 or its launcher is currently running. " +
                            "Save and exit it before applying patches.");
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                        process.Dispose();
                }
            }
        }

        private static void EnsureDirectoryWritable(string directory)
        {
            string probe = Path.Combine(directory, $".sims3modernpatcher-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(probe, "write test");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException(
                    "The game folder is not writable. Close the game and run this patcher as Administrator.",
                    ex);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Sims3ModernPatcher/2.0");
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }

        private static async Task PreflightCoreDownloadsAsync(
            string cacheDir,
            bool includeDxvk,
            Action<string> log)
        {
            log("[*] Preflight: Ultimate ASI Loader...");
            await DownloadFileAsync(
                AsiLoaderUrl,
                Path.Combine(cacheDir, "wininet-Win32.zip"),
                AsiLoaderSha256,
                log);

            log("[*] Preflight: Sims 3 Settings Setter 1.6.3...");
            await DownloadFileAsync(
                S3ssUrl,
                Path.Combine(cacheDir, "Sims3SettingsSetter-1.6.3.asi"),
                S3ssSha256,
                log);

            if (includeDxvk)
            {
                log("[*] Preflight: DXVK 2.6.1...");
                await DownloadFileAsync(
                    DxvkUrl,
                    Path.Combine(cacheDir, "dxvk-2.6.1.tar.gz"),
                    DxvkSha256,
                    log);
            }
        }

        private static void ApplyLaaPatch(string fullExePath, string backupFolder, string exeName, Action<string> log)
        {
            string backupPath = Path.Combine(backupFolder, exeName + ".bak");
            bool changed = PeLargeAddressAware.Apply(fullExePath, backupPath);
            if (changed)
            {
                log($"[SUCCESS] Enabled 4GB Large Address Aware on {exeName}.");
            }
            else
            {
                log($"[INFO] 4GB Large Address Aware already set on {exeName}.");
            }
        }

        private static void PatchGraphicsConfiguration(
            string binFolder,
            string backupFolder,
            HardwareInfo hardware,
            Action<string> log)
        {
            // Sims 3 ships GraphicsRules.sgr (sometimes referenced as .sfp in older docs).
            string[] candidates = { "GraphicsRules.sgr", "GraphicsRules.sfp" };
            string? rulesFile = candidates
                .Select(name => Path.Combine(binFolder, name))
                .FirstOrDefault(File.Exists);

            if (rulesFile is null)
            {
                log("[!] GraphicsRules file not found — skipped VRAM ceiling update.");
            }
            else
            {
                BackupFile(rulesFile, Path.Combine(backupFolder, Path.GetFileName(rulesFile) + ".bak"));
                string original = File.ReadAllText(rulesFile);
                string updated = GraphicsRulesEditor.ApplyTextureMemoryFallback(original, out bool changed);
                if (changed)
                {
                    File.WriteAllText(rulesFile, updated);
                    log($"[SUCCESS] Set the unrecognized-GPU texture-memory fallback to 1024 MB in {Path.GetFileName(rulesFile)}.");
                }
                else
                {
                    log($"[INFO] {Path.GetFileName(rulesFile)} did not need the texture-memory fallback update.");
                }
            }

            string cardsFile = Path.Combine(binFolder, "GraphicsCards.sgr");
            if (!File.Exists(cardsFile))
            {
                log("[!] GraphicsCards.sgr not found — skipped explicit GPU recognition.");
                return;
            }

            BackupFile(cardsFile, Path.Combine(backupFolder, "GraphicsCards.sgr.bak"));
            string cardsOriginal = File.ReadAllText(cardsFile);
            string cardsUpdated = GraphicsCardsEditor.AddDetectedCard(
                cardsOriginal,
                hardware.GpuVendorId,
                hardware.GpuDeviceId,
                hardware.GpuName,
                out bool cardAdded);
            if (cardAdded)
            {
                File.WriteAllText(cardsFile, cardsUpdated);
                log($"[SUCCESS] Added detected GPU {hardware.GpuName} (0x{hardware.GpuDeviceId}) to GraphicsCards.sgr.");
            }
            else if (string.IsNullOrEmpty(hardware.GpuDeviceId))
            {
                log("[!] GPU PCI device ID was unavailable — skipped explicit GraphicsCards.sgr entry.");
            }
            else
            {
                log("[INFO] Detected GPU is already listed, or its vendor section was not found in GraphicsCards.sgr.");
            }
        }

        private static string EnsureModsFolder(Action<string> log)
        {
            string docsRoot = FindSims3DocumentsFolder();
            string modsDir = Path.Combine(docsRoot, "Mods", "Packages");
            Directory.CreateDirectory(modsDir);

            string resourceCfg = Path.Combine(docsRoot, "Mods", "Resource.cfg");
            if (!File.Exists(resourceCfg))
            {
                File.WriteAllText(resourceCfg,
                    "Priority 500\nPackedFile Packages/*.package\nPackedFile Packages/*/*.package\n");
                log("[+] Created Mods/Resource.cfg so package mods can load.");
            }
            else
            {
                log("[INFO] Mods/Resource.cfg already present.");
            }

            log($"[+] Mods packages folder: {modsDir}");
            return modsDir;
        }

        private static string FindSims3DocumentsFolder()
        {
            // SpecialFolder.MyDocuments follows Windows Known Folder redirection,
            // including OneDrive and custom drive locations.
            string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string sims3 = Path.Combine(myDocs, "Electronic Arts", "The Sims 3");
            Directory.CreateDirectory(sims3);
            return sims3;
        }

        private static async Task InstallAsiLoaderAsync(
            string binFolder, string backupFolder, string cacheDir, Action<string> log)
        {
            // Sims 3 is 32-bit. wininet.dll is the S3SS-recommended loader name and leaves d3d9.dll free for DXVK.
            string targetDll = Path.Combine(binFolder, "wininet.dll");
            string zipPath = Path.Combine(cacheDir, "wininet-Win32.zip");

            log("[*] Downloading Ultimate ASI Loader (32-bit wininet.dll)...");
            await DownloadFileAsync(AsiLoaderUrl, zipPath, AsiLoaderSha256, log);

            if (File.Exists(targetDll))
                BackupFile(targetDll, Path.Combine(backupFolder, "wininet.dll.bak"));

            SafeArchiveExtractor.ExtractZipEntry(zipPath, "wininet.dll", targetDll);
            log("[SUCCESS] Installed ASI Loader as Game\\Bin\\wininet.dll.");
        }

        private static async Task InstallSims3SettingsSetterAsync(
            string binFolder, string cacheDir, Action<string> log)
        {
            string asiPath = Path.Combine(binFolder, "Sims3SettingsSetter.asi");
            string downloadPath = Path.Combine(cacheDir, "Sims3SettingsSetter-1.6.3.asi");

            log("[*] Downloading Sims 3 Settings Setter (modern CPU/GPU/smooth fixes)...");
            await DownloadFileAsync(S3ssUrl, downloadPath, S3ssSha256, log);
            File.Copy(downloadPath, asiPath, true);
            log("[SUCCESS] Installed Sims3SettingsSetter.asi (includes Alder Lake / hybrid CPU fix).");

            // S3SS already enables CPU optimization by default and defaults its limiter to 60 FPS.
            // Do not synthesize settings: S3SS owns the S3SS.toml schema and creates it on first launch.
            string s3ssDir = Path.Combine(FindSims3DocumentsFolder(), "S3SS");
            string obsoleteConfig = Path.Combine(s3ssDir, "config.toml");
            if (File.Exists(obsoleteConfig)
                && File.ReadAllText(obsoleteConfig).StartsWith(
                    "# Generated by Sims3ModernPatcher",
                    StringComparison.Ordinal))
            {
                File.Delete(obsoleteConfig);
                log("[+] Removed obsolete invalid S3SS config created by an earlier patcher build.");
            }

            log("[INFO] S3SS defaults: modern CPU optimization enabled, frame limit 60 FPS.");
        }

        private static async Task InstallDxvkAsync(
            string binFolder, string backupFolder, string cacheDir, Action<string> log)
        {
            log("[*] Downloading DXVK (32-bit d3d9.dll)...");
            string archivePath = Path.Combine(cacheDir, "dxvk-2.6.1.tar.gz");
            await DownloadFileAsync(DxvkUrl, archivePath, DxvkSha256, log);

            string target = Path.Combine(binFolder, "d3d9.dll");
            string marker = Path.Combine(binFolder, ".Sims3ModernPatcher.dxvk");
            string backup = Path.Combine(backupFolder, "d3d9.dll.bak");
            if (!IsManagedDxvk(target, marker))
            {
                if (File.Exists(target))
                    File.Copy(target, backup, overwrite: true);
                else if (File.Exists(backup))
                    File.Delete(backup);
            }

            SafeArchiveExtractor.ExtractTarGzEntry(archivePath, "/x32/d3d9.dll", target);
            File.WriteAllText(marker, FileIntegrity.ComputeSha256(target));
            log("[SUCCESS] Installed DXVK d3d9.dll for improved AMD/Intel compatibility.");
        }

        private static void RestoreNativeDirectX(string binFolder, string backupFolder, Action<string> log)
        {
            string target = Path.Combine(binFolder, "d3d9.dll");
            string marker = Path.Combine(binFolder, ".Sims3ModernPatcher.dxvk");
            if (!File.Exists(marker))
            {
                log("[INFO] Keeping native DirectX 9 renderer.");
                return;
            }

            if (!IsManagedDxvk(target, marker))
            {
                File.Delete(marker);
                log("[INFO] d3d9.dll was changed outside this patcher; it was left untouched.");
                return;
            }

            if (File.Exists(target))
                File.Delete(target);

            string backup = Path.Combine(backupFolder, "d3d9.dll.bak");
            if (File.Exists(backup))
                File.Copy(backup, target, overwrite: true);

            File.Delete(marker);
            log("[SUCCESS] Removed patcher-managed DXVK and restored the previous DirectX file, if any.");
        }

        private static bool IsManagedDxvk(string target, string marker)
        {
            if (!File.Exists(target) || !File.Exists(marker))
                return false;
            try
            {
                string expectedHash = File.ReadAllText(marker).Trim();
                if (expectedHash.Equals(
                        "DXVK 2.6.1 installed by Sims3ModernPatcher",
                        StringComparison.Ordinal))
                {
                    return true;
                }
                return expectedHash.Length == 64
                    && FileIntegrity.ComputeSha256(target).Equals(
                        expectedHash,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveLegacyConflictingFiles(string binFolder, Action<string> log)
        {
            string[] obsolete =
            {
                "TS3AlderLakePatch.ini",
                "TS3AlderLakePatch.dll",
                "TS3SmoothPatch.ini",
                "TS3SmoothPatch.asi",
                "SmoothPatch.asi"
            };

            foreach (var name in obsolete)
            {
                string path = Path.Combine(binFolder, name);
                if (!File.Exists(path)) continue;
                try
                {
                    string disabledDir = Path.Combine(binFolder, "Backup_Original_Win11", "DisabledConflicts");
                    Directory.CreateDirectory(disabledDir);
                    string destination = Path.Combine(disabledDir, name);
                    if (File.Exists(destination))
                        File.Delete(destination);
                    File.Move(path, destination);
                    log($"[+] Disabled conflicting legacy patch and preserved it in backups: {name}");
                }
                catch (Exception ex)
                {
                    throw new IOException($"Could not disable conflicting legacy patch {name}.", ex);
                }
            }
        }

        public static string BuildLauncherScript(
            string primaryExeName,
            GamePlatform platform = GamePlatform.DiscOrOther,
            string? sims3DocumentsPath = null)
        {
            string fallbackExeName = primaryExeName.Equals("TS3.exe", StringComparison.OrdinalIgnoreCase)
                ? "TS3W.exe"
                : "TS3.exe";
            string documentsSetup = string.IsNullOrWhiteSpace(sims3DocumentsPath)
                ? "set \"DOCS=%USERPROFILE%\\Documents\\Electronic Arts\\The Sims 3\"\r\n" +
                  "if not exist \"%DOCS%\" (\r\n" +
                  "  if exist \"%USERPROFILE%\\OneDrive\\Documents\\Electronic Arts\\The Sims 3\" (\r\n" +
                  "    set \"DOCS=%USERPROFILE%\\OneDrive\\Documents\\Electronic Arts\\The Sims 3\"\r\n" +
                  "  )\r\n" +
                  ")\r\n"
                : $"set \"DOCS={EscapeBatchValue(sims3DocumentsPath)}\"\r\n";
            string launchCommands = platform == GamePlatform.Steam
                ? "echo [+] Starting The Sims 3 through Steam\r\n" +
                  "start \"\" \"steam://run/47890\"\r\n"
                : $"if exist \"{primaryExeName}\" (\r\n" +
                  $"  echo [+] Starting {primaryExeName}\r\n" +
                  $"  start \"\" /abovenormal \"{primaryExeName}\"\r\n" +
                  $") else if exist \"{fallbackExeName}\" (\r\n" +
                  $"  echo [+] Starting {fallbackExeName}\r\n" +
                  $"  start \"\" /abovenormal \"{fallbackExeName}\"\r\n" +
                  ") else (\r\n" +
                  "  echo [X] Could not find TS3W.exe or TS3.exe\r\n" +
                  "  pause\r\n" +
                  "  exit /b 1\r\n" +
                  ")\r\n";
            string launchMessage = platform == GamePlatform.Steam
                ? "echo [*] Launching game through Steam...\r\n"
                : "echo [*] Launching game with Above Normal priority...\r\n";

            return
                "@echo off\r\n" +
                "title The Sims 3 Reliable Windows 11 Launcher\r\n" +
                "color 0B\r\n" +
                "echo ====================================================================\r\n" +
                "echo   THE SIMS 3 WINDOWS 11 RELIABLE PRE-LAUNCH MAINTENANCE & LAUNCHER\r\n" +
                "echo ====================================================================\r\n" +
                "echo.\r\n" +
                "tasklist /FI \"IMAGENAME eq TS3W.exe\" 2>nul | find /I \"TS3W.exe\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  echo [!] The Sims 3 is already running. It was left untouched.\r\n" +
                "  pause\r\n" +
                "  exit /b 2\r\n" +
                ")\r\n" +
                "tasklist /FI \"IMAGENAME eq TS3.exe\" 2>nul | find /I \"TS3.exe\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  echo [!] The Sims 3 is already running. It was left untouched.\r\n" +
                "  pause\r\n" +
                "  exit /b 2\r\n" +
                ")\r\n" +
                "echo [*] Purging safe-to-regenerate Sims 3 cache files...\r\n" +
                documentsSetup +
                "if exist \"%DOCS%\" (\r\n" +
                "  del /F /Q \"%DOCS%\\CASPartCache.package\" 2>nul\r\n" +
                "  del /F /Q \"%DOCS%\\compositorCache.package\" 2>nul\r\n" +
                "  del /F /Q \"%DOCS%\\scriptCache.package\" 2>nul\r\n" +
                "  del /F /Q \"%DOCS%\\simCompositorCache.package\" 2>nul\r\n" +
                "  del /F /Q \"%DOCS%\\socialCache.package\" 2>nul\r\n" +
                "  echo [OK] Caches purged.\r\n" +
                ") else (\r\n" +
                "  echo [!] Sims 3 Documents folder not found yet. Caches skipped.\r\n" +
                ")\r\n" +
                launchMessage +
                "cd /d \"%~dp0\"\r\n" +
                launchCommands +
                "timeout /t 2 >nul\r\n";
        }

        private static string EscapeBatchValue(string value)
        {
            return value.Replace("%", "%%", StringComparison.Ordinal);
        }

        private static string DeployReliableLauncher(
            string binFolder,
            string primaryExeName,
            GamePlatform platform,
            string sims3DocumentsPath,
            Action<string> log)
        {
            string launcherBatPath = Path.Combine(binFolder, "Sims3_Reliable_Launcher.bat");
            File.WriteAllText(
                launcherBatPath,
                BuildLauncherScript(primaryExeName, platform, sims3DocumentsPath));
            log("[SUCCESS] Deployed Sims3_Reliable_Launcher.bat (supports TS3W.exe and TS3.exe).");
            return launcherBatPath;
        }

        private static bool CreateDesktopShortcut(
            string launcherBatPath, string binFolder, string fullExePath, Action<string> log)
        {
            string? temporaryShortcut = null;
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutLocation = Path.Combine(desktopPath, "The Sims 3 (Reliable Win11).lnk");
                temporaryShortcut = Path.Combine(
                    desktopPath,
                    $"The Sims 3 (Reliable Win11).{Guid.NewGuid():N}.tmp.lnk");

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                    throw new InvalidOperationException("WScript.Shell COM object is unavailable.");

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(temporaryShortcut);
                shortcut.TargetPath = launcherBatPath;
                shortcut.WorkingDirectory = binFolder;
                shortcut.IconLocation = fullExePath + ",0";
                shortcut.Description = "Launch The Sims 3 with cache purge and modern Win11-friendly fixes";
                shortcut.Save();
                File.Move(temporaryShortcut, shortcutLocation, overwrite: true);
                log("[SUCCESS] Created desktop shortcut: The Sims 3 (Reliable Win11).");
                return true;
            }
            catch (Exception ex)
            {
                if (temporaryShortcut is not null && File.Exists(temporaryShortcut))
                    File.Delete(temporaryShortcut);
                log($"[!] Desktop shortcut warning: {ex.Message}");
                return false;
            }
        }

        private static async Task DownloadFileAsync(
            string url,
            string destination,
            string expectedSha256,
            Action<string> log)
        {
            if (File.Exists(destination) && new FileInfo(destination).Length > 0)
            {
                try
                {
                    FileIntegrity.VerifySha256(destination, expectedSha256);
                    log($"[INFO] Using verified cached download: {Path.GetFileName(destination)}");
                    return;
                }
                catch (InvalidDataException)
                {
                    File.Delete(destination);
                }
            }

            string temporary = destination + ".download";
            if (File.Exists(temporary))
                File.Delete(temporary);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (request.RequestUri?.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) == true)
                request.Headers.Accept.ParseAdd("application/octet-stream");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var remote = await response.Content.ReadAsStreamAsync();
            await using (var local = File.Create(temporary))
            {
                await remote.CopyToAsync(local);
            }

            FileIntegrity.VerifySha256(temporary, expectedSha256);
            File.Move(temporary, destination, overwrite: true);
            log($"[+] Downloaded {Path.GetFileName(destination)}");
        }

        private static void BackupFile(string source, string backupPath)
        {
            if (!File.Exists(source)) return;
            if (!File.Exists(backupPath))
                File.Copy(source, backupPath, false);
        }

    }
}
