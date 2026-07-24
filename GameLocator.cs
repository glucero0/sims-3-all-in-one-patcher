using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Sims3ModernPatcher
{
    public static class GameLocator
    {
        // Steam AppID for The Sims 3 base game.
        private const string Sims3SteamAppId = "47890";

        public static List<GameInstall> FindAllInstallations()
        {
            var found = new Dictionary<string, GameInstall>(StringComparer.OrdinalIgnoreCase);

            void Add(string? path, GamePlatform hint)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string full = NormalizeInstallRoot(path);
                if (full.Length == 0 || !IsValidSims3Install(full)) return;

                GamePlatform platform = hint != GamePlatform.Unknown
                    ? hint
                    : InferPlatform(full);

                if (found.TryGetValue(full, out var existing))
                {
                    if (existing.Platform == GamePlatform.Unknown && platform != GamePlatform.Unknown)
                        found[full] = new GameInstall { Path = full, Platform = platform };
                    return;
                }

                found[full] = new GameInstall { Path = full, Platform = platform };
            }

            foreach (var steamRoot in FindSteamLibraryRoots())
            {
                Add(Path.Combine(steamRoot, "steamapps", "common", "The Sims 3"), GamePlatform.Steam);

                // Confirm via appmanifest when present (handles renamed folders).
                string manifest = Path.Combine(steamRoot, "steamapps", $"appmanifest_{Sims3SteamAppId}.acf");
                if (File.Exists(manifest))
                {
                    string? installdir = ReadAcfInstallDir(manifest);
                    if (!string.IsNullOrWhiteSpace(installdir))
                        Add(Path.Combine(steamRoot, "steamapps", "common", installdir), GamePlatform.Steam);
                }
            }

            foreach (var eaPath in FindEaAndSimsRegistryInstalls())
                Add(eaPath.path, eaPath.platform);

            foreach (var uninstallPath in FindUninstallInstalls())
                Add(uninstallPath.path, uninstallPath.platform);

            foreach (var common in EnumerateStandardInstallCandidates())
                Add(common.path, common.platform);

            return found.Values
                .OrderBy(i => i.Platform == GamePlatform.Steam ? 0 : i.Platform == GamePlatform.EaApp ? 1 : 2)
                .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static GameInstall? FindPrimaryInstallation()
        {
            return FindAllInstallations().FirstOrDefault();
        }

        public static bool IsValidSims3Install(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            string bin = Path.Combine(path, "Game", "Bin");
            return File.Exists(Path.Combine(bin, "TS3W.exe"))
                || File.Exists(Path.Combine(bin, "TS3.exe"));
        }

        public static string NormalizeInstallRoot(string path)
        {
            try
            {
                string full = Path.GetFullPath(path.Trim().TrimEnd('\\', '/'));
                if (File.Exists(full))
                    full = Path.GetDirectoryName(full) ?? full;

                // Registry entries and users may point at the root, Game, Game\Bin,
                // a launcher folder, or an executable. Walk upward to the first valid root.
                DirectoryInfo? current = new(full);
                while (current is not null)
                {
                    if (IsValidSims3Install(current.FullName))
                        return current.FullName;
                    current = current.Parent;
                }

                return full;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static GamePlatform InferPlatform(string installPath)
        {
            string p = installPath.Replace('/', '\\');
            if (p.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)
                || File.Exists(Path.Combine(installPath, "steam_api.dll"))
                || File.Exists(Path.Combine(installPath, "Game", "Bin", "steam_api.dll")))
                return GamePlatform.Steam;

            if (p.Contains(@"\Origin Games\", StringComparison.OrdinalIgnoreCase)
                || p.Contains(@"\EA Games\", StringComparison.OrdinalIgnoreCase)
                || p.Contains(@"\Electronic Arts\", StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(Path.Combine(installPath, "__Installer"))
                || File.Exists(Path.Combine(installPath, "EACore.ini")))
                return GamePlatform.EaApp;

            return GamePlatform.DiscOrOther;
        }

        /// <summary>
        /// Standard Steam / EA App folder patterns checked on every ready fixed drive.
        /// Exposed for tests.
        /// </summary>
        internal static IEnumerable<(string path, GamePlatform platform)> EnumerateStandardInstallCandidates()
        {
            foreach (var candidate in BuiltInFallbackPaths())
                yield return candidate;

            foreach (DriveInfo drive in SafeFixedDrives())
            {
                string root = drive.RootDirectory.FullName;
                foreach (string relative in SteamGameRelativePaths())
                    yield return (Path.Combine(root, relative), GamePlatform.Steam);

                foreach (string relative in EaGameRelativePaths())
                    yield return (Path.Combine(root, relative), GamePlatform.EaApp);
            }

            foreach (string eaRoot in FindEaPreferredDownloadRoots())
            {
                yield return (Path.Combine(eaRoot, "The Sims 3"), GamePlatform.EaApp);
                yield return (Path.Combine(eaRoot, "EA Games", "The Sims 3"), GamePlatform.EaApp);
            }
        }

        internal static IEnumerable<string> ParseSteamLibraryFoldersVdf(string vdfText)
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in Regex.Matches(
                         vdfText,
                         "\"path\"\\s+\"([^\"]+)\"",
                         RegexOptions.IgnoreCase))
            {
                libraries.Add(UnescapeVdfPath(m.Groups[1].Value));
            }

            // Legacy Steam format: "1"  "D:\\SteamLibrary"
            foreach (Match m in Regex.Matches(
                         vdfText,
                         "^\\s*\"\\d+\"\\s+\"([^\"]+)\"",
                         RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                string value = UnescapeVdfPath(m.Groups[1].Value);
                // Skip non-path metadata values from modern VDFs (counts, etc.).
                if (value.IndexOf(':') >= 0 || value.Contains('\\') || value.Contains('/'))
                    libraries.Add(value);
            }

            return libraries;
        }

        private static string UnescapeVdfPath(string value)
            => value.Replace(@"\\", @"\").Replace('/', '\\').Trim();

        private static IEnumerable<string> FindSteamLibraryRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var steamPath in FindSteamInstallPaths())
            {
                roots.Add(steamPath);

                string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf))
                    continue;

                try
                {
                    foreach (string lib in ParseSteamLibraryFoldersVdf(File.ReadAllText(vdf)))
                    {
                        if (Directory.Exists(lib))
                            roots.Add(lib);
                    }
                }
                catch { }
            }

            // Extra libraries that may not be registered to a Steam client we found yet.
            foreach (DriveInfo drive in SafeFixedDrives())
            {
                foreach (string relative in new[]
                         {
                             @"SteamLibrary",
                             @"Steam",
                             @"Program Files (x86)\Steam",
                             @"Program Files\Steam",
                             @"Games\Steam",
                             @"Games\SteamLibrary"
                         })
                {
                    string candidate = Path.Combine(drive.RootDirectory.FullName, relative);
                    if (Directory.Exists(Path.Combine(candidate, "steamapps")))
                        roots.Add(candidate);
                }
            }

            return roots;
        }

        private static IEnumerable<string> FindSteamInstallPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryReg(RegistryKey root, string subKey)
            {
                try
                {
                    using var key = root.OpenSubKey(subKey);
                    if (key?.GetValue("InstallPath") is string install && Directory.Exists(install))
                        paths.Add(install);
                    if (key?.GetValue("SteamPath") is string steamPath)
                    {
                        string normalized = steamPath.Replace('/', '\\');
                        if (Directory.Exists(normalized))
                            paths.Add(normalized);
                    }
                }
                catch { }
            }

            TryReg(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam");
            TryReg(Registry.LocalMachine, @"SOFTWARE\Valve\Steam");
            TryReg(Registry.CurrentUser, @"SOFTWARE\Valve\Steam");

            foreach (DriveInfo drive in SafeFixedDrives())
            {
                foreach (string relative in new[]
                         {
                             @"Program Files (x86)\Steam",
                             @"Program Files\Steam",
                             @"Steam"
                         })
                {
                    string candidate = Path.Combine(drive.RootDirectory.FullName, relative);
                    if (File.Exists(Path.Combine(candidate, "steam.exe"))
                        || Directory.Exists(Path.Combine(candidate, "steamapps")))
                    {
                        paths.Add(candidate);
                    }
                }
            }

            return paths;
        }

        private static string? ReadAcfInstallDir(string manifestPath)
        {
            try
            {
                Match m = Regex.Match(
                    File.ReadAllText(manifestPath),
                    "\"installdir\"\\s+\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<(string path, GamePlatform platform)> FindEaAndSimsRegistryInstalls()
        {
            // Keys used by retail / EA App / Origin / Steam registry writers and by NRaas tools.
            (string subKey, GamePlatform platform)[] keys =
            {
                (@"SOFTWARE\WOW6432Node\Sims\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\Sims\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\WOW6432Node\Sims(Steam)\The Sims 3", GamePlatform.Steam),
                (@"SOFTWARE\Sims(Steam)\The Sims 3", GamePlatform.Steam),
                (@"SOFTWARE\WOW6432Node\Electronic Arts\Sims\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\Electronic Arts\Sims\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\WOW6432Node\EA Games\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\EA Games\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\WOW6432Node\Electronic Arts\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\Electronic Arts\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\WOW6432Node\Origin Games\The Sims 3", GamePlatform.EaApp),
                (@"SOFTWARE\Origin Games\The Sims 3", GamePlatform.EaApp),
            };

            foreach (var (subKey, platform) in keys)
            {
                foreach (string? dir in ReadInstallPathsFromKey(Registry.LocalMachine, subKey))
                    yield return (dir, platform);
                foreach (string? dir in ReadInstallPathsFromKey(Registry.CurrentUser, subKey))
                    yield return (dir, platform);
            }

            // EA Core "Installed Games" entries sometimes hold only a display path.
            foreach (string? dir in ReadInstallPathsFromKey(
                         Registry.LocalMachine,
                         @"SOFTWARE\WOW6432Node\Electronic Arts\EA Core\Installed Games\The Sims 3"))
            {
                yield return (dir, GamePlatform.EaApp);
            }
        }

        private static IEnumerable<string> ReadInstallPathsFromKey(RegistryKey hive, string subKey)
        {
            RegistryKey? key = null;
            try { key = hive.OpenSubKey(subKey); }
            catch { }

            if (key is null)
                yield break;

            using (key)
            {
                string[] valueNames =
                {
                    "Install Dir",
                    "InstallDir",
                    "InstallPath",
                    "InstallLocation",
                    "Path",
                    "ExePath"
                };

                foreach (string name in valueNames)
                {
                    if (key.GetValue(name) is not string raw || string.IsNullOrWhiteSpace(raw))
                        continue;

                    string cleaned = raw.Trim().Trim('"');
                    if (name.Equals("ExePath", StringComparison.OrdinalIgnoreCase)
                        || cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            cleaned = Path.GetDirectoryName(cleaned) ?? cleaned;
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    yield return cleaned;
                }
            }
        }

        private static IEnumerable<(string path, GamePlatform platform)> FindUninstallInstalls()
        {
            var results = new List<(string path, GamePlatform platform)>();
            string[] uninstallRoots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in uninstallRoots)
            {
                RegistryKey? uninstall = null;
                try { uninstall = Registry.LocalMachine.OpenSubKey(root); }
                catch { }

                if (uninstall is null) continue;

                using (uninstall)
                {
                    foreach (var subName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var sub = uninstall.OpenSubKey(subName);
                            if (sub is null) continue;

                            string display = (sub.GetValue("DisplayName") as string) ?? string.Empty;
                            if (!IsBaseSims3DisplayName(display))
                                continue;

                            string? loc = sub.GetValue("InstallLocation") as string;
                            if (string.IsNullOrWhiteSpace(loc))
                            {
                                if (sub.GetValue("DisplayIcon") as string is string icon)
                                {
                                    try
                                    {
                                        string iconPath = icon.Split(',')[0].Trim('"');
                                        if (File.Exists(iconPath))
                                            loc = Path.GetDirectoryName(iconPath);
                                    }
                                    catch { }
                                }
                            }

                            if (string.IsNullOrWhiteSpace(loc)) continue;

                            string publisher = (sub.GetValue("Publisher") as string) ?? string.Empty;
                            GamePlatform platform = GamePlatform.DiscOrOther;
                            if (publisher.Contains("Valve", StringComparison.OrdinalIgnoreCase)
                                || loc.Contains("steamapps", StringComparison.OrdinalIgnoreCase)
                                || subName.Contains("Steam App 47890", StringComparison.OrdinalIgnoreCase))
                                platform = GamePlatform.Steam;
                            else if (publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase)
                                     || publisher.Contains("EA ", StringComparison.OrdinalIgnoreCase)
                                     || publisher.Equals("EA", StringComparison.OrdinalIgnoreCase))
                                platform = GamePlatform.EaApp;

                            results.Add((loc, platform));
                        }
                        catch { }
                    }
                }
            }

            return results;
        }

        internal static bool IsBaseSims3DisplayName(string display)
        {
            if (string.IsNullOrWhiteSpace(display))
                return false;

            // Match "The Sims 3" / "The Sims™ 3" but skip expansions and stuff packs.
            if (!Regex.IsMatch(display, @"Sims\s*™?\s*3", RegexOptions.IgnoreCase))
                return false;

            string[] excluded =
            {
                "Expansion", "Stuff Pack", "Stuff Packs", "Update", "Launcher",
                "World Adventures", "Ambitions", "Late Night", "Generations",
                "Pets", "Showtime", "Supernatural", "Seasons", "University Life",
                "Island Paradise", "Into the Future"
            };

            return excluded.All(token =>
                display.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static IEnumerable<(string path, GamePlatform platform)> BuiltInFallbackPaths()
        {
            yield return (@"C:\Program Files (x86)\Steam\steamapps\common\The Sims 3", GamePlatform.Steam);
            yield return (@"C:\Program Files\Steam\steamapps\common\The Sims 3", GamePlatform.Steam);
            yield return (@"C:\Program Files\EA Games\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files (x86)\EA Games\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files\Electronic Arts\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files (x86)\Electronic Arts\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files (x86)\Origin Games\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files\Origin Games\The Sims 3", GamePlatform.EaApp);
        }

        private static IEnumerable<string> SteamGameRelativePaths()
        {
            yield return @"Program Files (x86)\Steam\steamapps\common\The Sims 3";
            yield return @"Program Files\Steam\steamapps\common\The Sims 3";
            yield return @"Steam\steamapps\common\The Sims 3";
            yield return @"SteamLibrary\steamapps\common\The Sims 3";
            yield return @"Games\Steam\steamapps\common\The Sims 3";
            yield return @"Games\SteamLibrary\steamapps\common\The Sims 3";
        }

        private static IEnumerable<string> EaGameRelativePaths()
        {
            yield return @"Program Files\EA Games\The Sims 3";
            yield return @"Program Files (x86)\EA Games\The Sims 3";
            yield return @"Program Files\Electronic Arts\The Sims 3";
            yield return @"Program Files (x86)\Electronic Arts\The Sims 3";
            yield return @"Program Files\Origin Games\The Sims 3";
            yield return @"Program Files (x86)\Origin Games\The Sims 3";
            yield return @"EA Games\The Sims 3";
            yield return @"Electronic Arts\The Sims 3";
            yield return @"Origin Games\The Sims 3";
            yield return @"Games\EA Games\The Sims 3";
            yield return @"Games\Electronic Arts\The Sims 3";
            yield return @"Games\The Sims 3";
        }

        private static IEnumerable<string> FindEaPreferredDownloadRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // EA Desktop / Origin often remember the last games install root here.
            string[] configDirs =
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Electronic Arts", "EA Desktop"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "EA Desktop"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Origin"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Origin")
            };

            foreach (string dir in configDirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (string file in EnumerateConfigFilesShallow(dir, maxFiles: 40))
                {
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch { continue; }

                    foreach (Match m in Regex.Matches(
                                 text,
                                 @"(?i)(?:Install(?:Path|Dir|Location)|Download(?:Path|Dir)|contentPath|gamePath)\s*[""':=]\s*[""']?([A-Za-z]:\\[^""'<\r\n]+)",
                                 RegexOptions.IgnoreCase))
                    {
                        string candidate = m.Groups[1].Value.Trim().TrimEnd('\\', '/', '"', '\'');
                        if (Directory.Exists(candidate))
                            roots.Add(candidate);
                    }
                }
            }

            return roots;
        }

        private static IEnumerable<string> EnumerateConfigFilesShallow(string root, int maxFiles)
        {
            var results = new List<string>();
            var pending = new Queue<(string path, int depth)>();
            pending.Enqueue((root, 0));

            while (pending.Count > 0 && results.Count < maxFiles)
            {
                var (path, depth) = pending.Dequeue();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(path))
                    {
                        string ext = Path.GetExtension(file);
                        if (ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(file);
                            if (results.Count >= maxFiles)
                                return results;
                        }
                    }

                    if (depth >= 2)
                        continue;

                    foreach (string child in Directory.EnumerateDirectories(path))
                        pending.Enqueue((child, depth + 1));
                }
                catch
                {
                    // Skip locked/inaccessible EA/Origin config folders.
                }
            }

            return results;
        }

        private static IEnumerable<DriveInfo> SafeFixedDrives()
        {
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch { yield break; }

            foreach (DriveInfo drive in drives)
            {
                bool ready;
                try { ready = drive.IsReady && drive.DriveType == DriveType.Fixed; }
                catch { continue; }

                if (ready)
                    yield return drive;
            }
        }
    }
}
