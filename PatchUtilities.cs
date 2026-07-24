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

            if (Regex.IsMatch(
                    text,
                    $@"(?im)^\s*card\s+0x{Regex.Escape(deviceId)}\b"))
            {
                return text;
            }

            string escapedName = cardName.Replace("\"", "'");
            string vendorPattern =
                $@"(?im)^(?<vendor>\s*vendor\s+""[^""]+""[^\r\n]*\b0x{Regex.Escape(vendorId)}\b[^\r\n]*)$";
            Match vendor = Regex.Match(text, vendorPattern);
            if (!vendor.Success)
            {
                string vendorName = vendorId.ToLowerInvariant() switch
                {
                    "10de" => "NVIDIA",
                    "1002" => "ATI",
                    "8086" => "Intel",
                    _ => "Detected GPU Vendor"
                };
                string ending = text.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n";
                changed = true;
                return text
                    + ending
                    + $"vendor \"{vendorName}\" 0x{vendorId.ToLowerInvariant()}\n"
                    + $"    card 0x{deviceId.ToLowerInvariant()} \"{escapedName}\"\n";
            }

            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string cardLine = $"    card 0x{deviceId.ToLowerInvariant()} \"{escapedName}\"";
            string updated = text.Insert(vendor.Index + vendor.Length, newline + cardLine);
            changed = true;
            return updated;
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
