using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Sims3ModernPatcher
{
    public static class SaveBackupManager
    {
        private const int BackupsToKeep = 10;

        public static string GetBackupRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sims3ModernPatcher",
                "SaveBackups");
        }

        public static string? CreateSnapshot(
            string sims3DocumentsPath,
            string backupRoot,
            Action<string> log)
        {
            string savesPath = Path.Combine(sims3DocumentsPath, "Saves");
            if (!Directory.Exists(savesPath)
                || !Directory.EnumerateFileSystemEntries(savesPath).Any())
            {
                log("[INFO] No existing Sims 3 saves found to back up.");
                return null;
            }

            Directory.CreateDirectory(backupRoot);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string archivePath = Path.Combine(backupRoot, $"Sims3-Saves-{timestamp}.zip");
            int suffix = 1;
            while (File.Exists(archivePath))
            {
                archivePath = Path.Combine(
                    backupRoot,
                    $"Sims3-Saves-{timestamp}-{suffix++}.zip");
            }

            ZipFile.CreateFromDirectory(
                savesPath,
                archivePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: true);
            log($"[SUCCESS] Backed up saves to: {archivePath}");

            PruneOldBackups(backupRoot, archivePath, log);
            return archivePath;
        }

        private static void PruneOldBackups(
            string backupRoot,
            string newestBackup,
            Action<string> log)
        {
            FileInfo[] backups = new DirectoryInfo(backupRoot)
                .GetFiles("Sims3-Saves-*.zip")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (FileInfo oldBackup in backups.Skip(BackupsToKeep))
            {
                if (oldBackup.FullName.Equals(newestBackup, StringComparison.OrdinalIgnoreCase))
                    continue;
                oldBackup.Delete();
                log($"[+] Pruned old save backup: {oldBackup.Name}");
            }
        }
    }
}
