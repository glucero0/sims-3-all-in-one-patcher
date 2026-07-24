using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Sims3ModernPatcher
{
    /// <summary>
    /// Writes the on-screen progress log to LocalAppData so a run can be inspected after the UI closes.
    /// </summary>
    public sealed class PatcherSessionLog : IDisposable
    {
        private const int LogsToKeep = 30;
        private readonly object _sync = new();
        private readonly StreamWriter _writer;
        private bool _disposed;

        public string FilePath { get; }

        private PatcherSessionLog(string filePath, StreamWriter writer)
        {
            FilePath = filePath;
            _writer = writer;
        }

        public static string GetLogRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sims3ModernPatcher",
                "Logs");
        }

        public static PatcherSessionLog StartNew(string reason)
        {
            string root = GetLogRoot();
            Directory.CreateDirectory(root);
            string filePath = Path.Combine(
                root,
                $"patcher-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var stream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            var writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true
            };

            var session = new PatcherSessionLog(filePath, writer);
            session.WriteLine($"[*] Sims 3 Modern Patcher log started ({reason}).");
            session.WriteLine($"[+] Log file: {filePath}");
            PruneOldLogs(root, filePath);
            return session;
        }

        public void WriteLine(string message)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _writer.Dispose();
            }
        }

        private static void PruneOldLogs(string root, string newestLog)
        {
            try
            {
                foreach (FileInfo oldLog in new DirectoryInfo(root)
                             .GetFiles("patcher-*.log")
                             .OrderByDescending(file => file.LastWriteTimeUtc)
                             .Skip(LogsToKeep))
                {
                    if (string.Equals(oldLog.FullName, newestLog, StringComparison.OrdinalIgnoreCase))
                        continue;
                    oldLog.Delete();
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
