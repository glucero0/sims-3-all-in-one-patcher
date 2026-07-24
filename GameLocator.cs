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

            foreach (var eaPath in FindEaRegistryInstalls())
                Add(eaPath, GamePlatform.EaApp);

            foreach (var uninstallPath in FindUninstallInstalls())
                Add(uninstallPath.path, uninstallPath.platform);

            foreach (var common in CommonFallbackPaths())
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
                || Directory.Exists(Path.Combine(installPath, "__Installer"))
                || File.Exists(Path.Combine(installPath, "EACore.ini")))
                return GamePlatform.EaApp;

            return GamePlatform.DiscOrOther;
        }

        private static IEnumerable<string> FindSteamLibraryRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var steamPath in FindSteamInstallPaths())
            {
                roots.Add(steamPath);

                string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf))
                    continue;

                foreach (Match m in Regex.Matches(
                             File.ReadAllText(vdf),
                             "\"path\"\\s+\"([^\"]+)\"",
                             RegexOptions.IgnoreCase))
                {
                    string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(lib))
                        roots.Add(lib);
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

            string[] defaults =
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam"
            };
            foreach (var d in defaults)
            {
                if (Directory.Exists(d))
                    paths.Add(d);
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

        private static IEnumerable<string> FindEaRegistryInstalls()
        {
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\EA Games\The Sims 3",
                @"SOFTWARE\EA Games\The Sims 3",
                @"SOFTWARE\WOW6432Node\Electronic Arts\The Sims 3",
                @"SOFTWARE\Electronic Arts\The Sims 3"
            };

            foreach (var subKey in keys)
            {
                string? dir = null;
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(subKey);
                    dir = key?.GetValue("Install Dir") as string
                        ?? key?.GetValue("InstallDir") as string
                        ?? key?.GetValue("InstallPath") as string;
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(dir))
                    yield return dir;
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
                            if (!display.Contains("Sims 3", StringComparison.OrdinalIgnoreCase)
                                || display.Contains("Expansion", StringComparison.OrdinalIgnoreCase)
                                || display.Contains("Stuff Pack", StringComparison.OrdinalIgnoreCase)
                                || display.Contains("Update", StringComparison.OrdinalIgnoreCase))
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
                                || loc.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
                                platform = GamePlatform.Steam;
                            else if (publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase)
                                     || publisher.Contains("EA ", StringComparison.OrdinalIgnoreCase))
                                platform = GamePlatform.EaApp;

                            results.Add((loc, platform));
                        }
                        catch { }
                    }
                }
            }

            return results;
        }

        private static IEnumerable<(string path, GamePlatform platform)> CommonFallbackPaths()
        {
            yield return (@"C:\Program Files (x86)\Steam\steamapps\common\The Sims 3", GamePlatform.Steam);
            yield return (@"C:\Program Files\Steam\steamapps\common\The Sims 3", GamePlatform.Steam);
            yield return (@"C:\Program Files\EA Games\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files (x86)\EA Games\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files (x86)\Origin Games\The Sims 3", GamePlatform.EaApp);
            yield return (@"C:\Program Files\Origin Games\The Sims 3", GamePlatform.EaApp);
            yield return (@"D:\SteamLibrary\steamapps\common\The Sims 3", GamePlatform.Steam);
            yield return (@"E:\SteamLibrary\steamapps\common\The Sims 3", GamePlatform.Steam);
            yield return (@"D:\Program Files\EA Games\The Sims 3", GamePlatform.EaApp);
        }
    }
}
