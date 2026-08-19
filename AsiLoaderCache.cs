using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sims3ModernPatcher
{
    /// <summary>
    /// Cache and last-resort manual download helpers for Ultimate ASI Loader.
    /// Automatic GitHub downloads remain the default; a sidecar marker is written
    /// only after every automatic source fails, so the next run uses a local zip first.
    /// </summary>
    public static class AsiLoaderCache
    {
        public const string NamedZipFileName = "wininet-Win32.zip";
        public const string CombinedZipFileName = "Ultimate-ASI-Loader.zip";
        public const string ManualRequestMarkerFileName = "wininet-Win32.manual-download";
        public const string GitHubRepoUrl = "https://github.com/ThirteenAG/Ultimate-ASI-Loader";
        public const string NamedZipUrl =
            "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/download/Win32-latest/wininet-Win32.zip";
        public const string CombinedZipUrl =
            "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/latest/download/Ultimate-ASI-Loader.zip";
        public const string LatestReleaseUrl =
            "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/latest";
        public const string Win32LatestReleaseUrl =
            "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/tag/Win32-latest";

        private static readonly string[] CandidateZipFileNames =
        {
            NamedZipFileName,
            CombinedZipFileName
        };

        private static readonly string[] LoaderEntryNames =
        {
            "wininet.dll",
            "dinput8.dll"
        };

        public static string GetMarkerPath(string cacheDir)
            => Path.Combine(cacheDir, ManualRequestMarkerFileName);

        public static string GetCanonicalZipPath(string cacheDir)
            => Path.Combine(cacheDir, NamedZipFileName);

        public static bool ManualDownloadWasRequested(string cacheDir)
            => File.Exists(GetMarkerPath(cacheDir));

        public static void MarkManualDownloadRequested(string cacheDir)
        {
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(
                GetMarkerPath(cacheDir),
                "Automatic Ultimate ASI Loader download failed." + Environment.NewLine +
                "Place " + NamedZipFileName + " or " + CombinedZipFileName +
                " in this folder, then retry patching.");
        }

        public static void ClearManualDownloadRequest(string cacheDir)
        {
            string markerPath = GetMarkerPath(cacheDir);
            if (File.Exists(markerPath))
                File.Delete(markerPath);
        }

        public static string? FindUsableArchive(string cacheDir)
        {
            if (string.IsNullOrWhiteSpace(cacheDir) || !Directory.Exists(cacheDir))
                return null;

            foreach (string fileName in CandidateZipFileNames)
            {
                string path = Path.Combine(cacheDir, fileName);
                if (IsUsableArchive(path))
                    return path;
            }

            return null;
        }

        public static bool IsUsableArchive(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath)
                || !File.Exists(zipPath)
                || new FileInfo(zipPath).Length == 0)
            {
                return false;
            }

            string probe = zipPath + ".probe.dll";
            try
            {
                SafeArchiveExtractor.ExtractZipEntry(zipPath, LoaderEntryNames, probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (File.Exists(probe))
                    File.Delete(probe);
            }
        }

        public static bool TryUseManualLocalArchive(string cacheDir, Action<string> log)
        {
            if (!ManualDownloadWasRequested(cacheDir))
                return false;

            string? found = FindUsableArchive(cacheDir);
            if (found is null)
            {
                log("[INFO] Manual ASI Loader download was requested earlier. " +
                    $"Looking for {NamedZipFileName} or {CombinedZipFileName} in: {cacheDir}");
                return false;
            }

            MaterializeCanonicalArchive(cacheDir, found);
            log($"[INFO] Using manually downloaded ASI Loader ({Path.GetFileName(found)}); skipping network.");
            ClearManualDownloadRequest(cacheDir);
            return true;
        }

        public static void MaterializeCanonicalArchive(string cacheDir, string sourceZipPath)
        {
            string canonical = GetCanonicalZipPath(cacheDir);
            if (sourceZipPath.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                return;

            Directory.CreateDirectory(cacheDir);
            File.Copy(sourceZipPath, canonical, overwrite: true);
        }

        public static string BuildManualInstructions(string cacheDir, IReadOnlyList<string>? errors = null)
        {
            string directory = Path.GetFullPath(cacheDir);
            string namedPath = Path.GetFullPath(GetCanonicalZipPath(cacheDir));
            string combinedPath = Path.GetFullPath(Path.Combine(cacheDir, CombinedZipFileName));

            var text = new StringBuilder();
            text.AppendLine("Could not download Ultimate ASI Loader automatically.");
            if (errors is not null)
            {
                foreach (string error in errors.Where(e => !string.IsNullOrWhiteSpace(e)))
                    text.AppendLine("Auto download error: " + error);
            }

            text.AppendLine();
            text.AppendLine("Open the GitHub repository:");
            text.AppendLine(GitHubRepoUrl);
            text.AppendLine();
            text.AppendLine("Download one of these files (do not rename them):");
            text.AppendLine("  " + NamedZipFileName + "  (preferred, from Win32-latest)");
            text.AppendLine("  " + CombinedZipFileName + "  (combined archive from the latest release)");
            text.AppendLine();
            text.AppendLine("Direct links:");
            text.AppendLine(NamedZipUrl);
            text.AppendLine(CombinedZipUrl);
            text.AppendLine();
            text.AppendLine("Save the file into this folder (create it if needed):");
            text.AppendLine(directory);
            text.AppendLine();
            text.AppendLine("Expected full paths:");
            text.AppendLine(namedPath);
            text.AppendLine(combinedPath);
            text.AppendLine();
            text.Append("Then retry the patching process (press GO again). The patcher will use the local file first.");
            return text.ToString();
        }
    }
}
