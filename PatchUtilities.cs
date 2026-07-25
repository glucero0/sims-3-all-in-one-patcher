using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Sims3ModernPatcher
{
    public static class GameVersionReader
    {
        public static string? Read(string installPath)
        {
            string skuPath = Path.Combine(installPath, "Game", "Bin", "skuversion.txt");
            if (!File.Exists(skuPath))
            {
                string bin = Path.Combine(installPath, "Game", "Bin");
                foreach (string executable in new[] { "TS3.exe", "TS3W.exe" })
                {
                    string path = Path.Combine(bin, executable);
                    if (!File.Exists(path))
                        continue;
                    string? fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
                    if (!string.IsNullOrWhiteSpace(fileVersion))
                        return fileVersion;
                }
                return null;
            }

            foreach (string line in File.ReadLines(skuPath))
            {
                Match match = Regex.Match(
                    line,
                    @"GameVersion\s*=\s*([0-9.]+)",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }
    }

    public static class GameExecutableSelector
    {
        public static IReadOnlyList<string> FindExisting(string binFolder)
        {
            string[] candidates = { "TS3W.exe", "TS3.exe" };
            return candidates
                .Where(name => File.Exists(Path.Combine(binFolder, name)))
                .ToArray();
        }

        public static string SelectPrimary(
            IReadOnlyCollection<string> existing,
            GamePlatform platform,
            string? gameVersion)
        {
            if (existing.Count == 0)
                throw new InvalidOperationException("No Sims 3 game executable was found.");

            bool usesTs3 = platform == GamePlatform.EaApp
                || gameVersion?.StartsWith("1.69", StringComparison.Ordinal) == true
                || gameVersion?.StartsWith("1.70", StringComparison.Ordinal) == true;

            string preferred = usesTs3 ? "TS3.exe" : "TS3W.exe";
            return existing.FirstOrDefault(
                       name => name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                   ?? existing.First();
        }
    }

    public static class PeLargeAddressAware
    {
        private const ushort LargeAddressAwareFlag = 0x0020;

        public static bool Apply(string executablePath, string backupPath)
        {
            byte[] bytes = File.ReadAllBytes(executablePath);
            if (bytes.Length < 0x40 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                throw new InvalidDataException($"{Path.GetFileName(executablePath)} is not a valid PE executable.");

            int peOffset = BitConverter.ToInt32(bytes, 0x3C);
            int characteristicsOffset = checked(peOffset + 22);
            if (peOffset < 0
                || characteristicsOffset + sizeof(ushort) > bytes.Length
                || peOffset + 4 > bytes.Length
                || bytes[peOffset] != (byte)'P'
                || bytes[peOffset + 1] != (byte)'E'
                || bytes[peOffset + 2] != 0
                || bytes[peOffset + 3] != 0)
            {
                throw new InvalidDataException($"{Path.GetFileName(executablePath)} has an invalid PE header.");
            }

            ushort characteristics = BitConverter.ToUInt16(bytes, characteristicsOffset);
            if ((characteristics & LargeAddressAwareFlag) != 0)
                return false;

            if (!File.Exists(backupPath))
                File.Copy(executablePath, backupPath);

            byte[] updated = BitConverter.GetBytes((ushort)(characteristics | LargeAddressAwareFlag));
            bytes[characteristicsOffset] = updated[0];
            bytes[characteristicsOffset + 1] = updated[1];
            File.WriteAllBytes(executablePath, bytes);
            return true;
        }
    }

    public static class GraphicsRulesEditor
    {
        public static string ApplyTextureMemoryFallback(string text, out bool changed)
        {
            string updated = Regex.Replace(
                text,
                @"(?m)^(\s*)seti\s+textureMemory\s+\d+\s*$",
                "$1seti textureMemory 1024",
                RegexOptions.IgnoreCase);

            updated = Regex.Replace(
                updated,
                @"(?m)^(\s*)setb\s+textureMemorySizeOK\s+false\s*$",
                "$1# setb textureMemorySizeOK false",
                RegexOptions.IgnoreCase);

            changed = !string.Equals(text, updated, StringComparison.Ordinal);
            return updated;
        }

        /// <summary>
        /// True when the unrecognized-GPU path uses 1024 MB and no longer marks VRAM as invalid.
        /// </summary>
        public static bool IsTextureMemoryFallbackApplied(string text)
        {
            bool has1024 = Regex.IsMatch(
                text,
                @"(?im)^\s*seti\s+textureMemory\s+1024\s*$");
            bool hasStock32 = Regex.IsMatch(
                text,
                @"(?im)^\s*seti\s+textureMemory\s+32\s*$");
            bool hasUncommentedInvalidFlag = Regex.IsMatch(
                text,
                @"(?im)^\s*setb\s+textureMemorySizeOK\s+false\s*$");
            return has1024 && !hasStock32 && !hasUncommentedInvalidFlag;
        }
    }

    public static class DxvkDetector
    {
        public static bool LooksLikeDxvk(string d3d9Path)
        {
            if (string.IsNullOrWhiteSpace(d3d9Path) || !File.Exists(d3d9Path))
                return false;

            try
            {
                var info = FileVersionInfo.GetVersionInfo(d3d9Path);
                if (ContainsDxvk(info.CompanyName)
                    || ContainsDxvk(info.ProductName)
                    || ContainsDxvk(info.FileDescription))
                {
                    return true;
                }
            }
            catch
            {
                // Fall through to size heuristic.
            }

            // System d3d9.dll is typically well under 1 MB; DXVK builds are multi-MB.
            try
            {
                return new FileInfo(d3d9Path).Length >= 1_000_000;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsDxvk(string? value)
            => !string.IsNullOrEmpty(value)
               && value.Contains("DXVK", StringComparison.OrdinalIgnoreCase);
    }

    public static class GraphicsCardsEditor
    {
        public static string AddDetectedCard(
            string text,
            string vendorId,
            string deviceId,
            string cardName,
            out bool changed)
        {
            changed = false;
            if (!Regex.IsMatch(vendorId, "^[0-9a-fA-F]{4}$")
                || !Regex.IsMatch(deviceId, "^[0-9a-fA-F]{4}$"))
            {
                return text;
            }

            string updated = text;

            // Stock Sims 3 lists GTX 580 as "card 1080" (no 0x). DXVK spoofs 0x1080, so normalize.
            string normalized1080 = Regex.Replace(
                updated,
                @"(?im)^(?<indent>\s*)card\s+1080\s+""(?<name>[^""]+)""\s*$",
                "${indent}card 0x1080 \"${name}\"");
            if (!string.Equals(normalized1080, updated, StringComparison.Ordinal))
            {
                updated = normalized1080;
                changed = true;
            }

            // Drop incomplete trailing vendor stubs previously appended without an "end".
            string withoutStub = Regex.Replace(
                updated,
                @"\r?\nvendor\s+""NVIDIA""\s+0x10de\s*\r?\n\s*card\s+0x[0-9a-fA-F]{4}\s+""[^""]+""\s*$",
                string.Empty,
                RegexOptions.IgnoreCase);
            if (!string.Equals(withoutStub, updated, StringComparison.Ordinal))
            {
                updated = withoutStub;
                changed = true;
            }

            if (!Regex.IsMatch(
                    updated,
                    $@"(?im)^\s*card\s+0x{Regex.Escape(deviceId)}\b"))
            {
                updated = InsertCardUnderVendor(updated, vendorId, deviceId, cardName, out bool inserted);
                if (inserted)
                    changed = true;
            }

            return updated;
        }

        /// <summary>
        /// DXVK's built-in Sims 3 profile spoofs NVIDIA device 0x1080 (GTX 580).
        /// Ensure that ID exists so the game can resolve graphics hardware under DXVK.
        /// </summary>
        public static string EnsureDxvkSpoofCard(string text, out bool changed)
        {
            string updated = AddDetectedCard(
                text,
                "10de",
                "1080",
                "GeForce GTX 580",
                out bool added);
            // AddDetectedCard also normalizes bare "card 1080".
            string withExplicit = InsertCardUnderVendor(
                updated,
                "10de",
                "1080",
                "GeForce GTX 580",
                out bool inserted);
            // InsertCardUnderVendor no-ops if already present.
            changed = added || inserted
                      || !string.Equals(text, withExplicit, StringComparison.Ordinal);
            return withExplicit;
        }

        private static string InsertCardUnderVendor(
            string text,
            string vendorId,
            string deviceId,
            string cardName,
            out bool changed)
        {
            changed = false;
            if (Regex.IsMatch(
                    text,
                    $@"(?im)^\s*card\s+0x{Regex.Escape(deviceId)}\b"))
            {
                return text;
            }

            string escapedName = cardName.Replace("\"", "'");
            string vendorPattern =
                $@"(?im)^(?<vendor>\s*vendor\s+""[^""]+""[^\r\n]*\b0x{Regex.Escape(vendorId)}\b[^\r\n]*)$";
            MatchCollection vendors = Regex.Matches(text, vendorPattern);
            // Prefer the longest vendor line (stock NVIDIA lists several IDs on one line).
            Match? vendor = vendors
                .Cast<Match>()
                .OrderByDescending(m => m.Length)
                .FirstOrDefault();

            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string cardLine = $"    card 0x{deviceId.ToLowerInvariant()} \"{escapedName}\"";

            if (vendor is null || !vendor.Success)
            {
                string vendorName = vendorId.ToLowerInvariant() switch
                {
                    "10de" => "NVIDIA",
                    "1002" => "ATI",
                    "8086" => "Intel",
                    _ => "Detected GPU Vendor"
                };
                string ending = text.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : newline;
                changed = true;
                return text
                    + ending
                    + $"vendor \"{vendorName}\" 0x{vendorId.ToLowerInvariant()}"
                    + newline
                    + cardLine
                    + newline
                    + "end"
                    + newline;
            }

            changed = true;
            return text.Insert(vendor.Index + vendor.Length, newline + cardLine);
        }
    }

    public static class SafeArchiveExtractor
    {
        public static void ExtractZipEntry(
            string archivePath,
            string expectedFileName,
            string destinationPath)
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                candidate => candidate.Name.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidDataException($"{expectedFileName} was not found in {Path.GetFileName(archivePath)}.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        public static void ExtractTarGzEntry(
            string archivePath,
            string expectedSuffix,
            string destinationPath)
        {
            using FileStream file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);

            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                string normalized = entry.Name.Replace('\\', '/');
                if (!normalized.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.DataStream is null)
                    throw new InvalidDataException($"{entry.Name} has no file data.");

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                using FileStream output = File.Create(destinationPath);
                entry.DataStream.CopyTo(output);
                return;
            }

            throw new InvalidDataException(
                $"{expectedSuffix} was not found in {Path.GetFileName(archivePath)}.");
        }
    }

    public static class FileIntegrity
    {
        public static string ComputeSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        public static void VerifySha256(string path, string expectedSha256)
        {
            string actual = ComputeSha256(path);
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Integrity check failed for {Path.GetFileName(path)}. Expected {expectedSha256}, got {actual}.");
            }
        }
    }
}
