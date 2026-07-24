using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sims3ModernPatcher
{
    public sealed class FileRollbackSession : IDisposable
    {
        private readonly Action<string> _log;
        private readonly string _snapshotRoot;
        private readonly Dictionary<string, string?> _snapshots =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _committed;
        private bool _disposed;

        public FileRollbackSession(Action<string> log)
        {
            _log = log;
            _snapshotRoot = Path.Combine(
                Path.GetTempPath(),
                "Sims3ModernPatcher",
                "Rollback",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_snapshotRoot);
        }

        public void Capture(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (_snapshots.ContainsKey(fullPath))
                return;

            if (!File.Exists(fullPath))
            {
                _snapshots[fullPath] = null;
                return;
            }

            string snapshot = Path.Combine(
                _snapshotRoot,
                _snapshots.Count.ToString("D4") + ".snapshot");
            File.Copy(fullPath, snapshot);
            _snapshots[fullPath] = snapshot;
        }

        public void Commit()
        {
            _committed = true;
            DeleteSnapshotDirectory();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (!_committed)
                Restore();

            DeleteSnapshotDirectory();
        }

        private void Restore()
        {
            _log("[!] A patch step failed. Restoring every file changed in this run...");
            var failures = new List<Exception>();

            foreach (var pair in _snapshots.Reverse())
            {
                try
                {
                    string target = pair.Key;
                    string? snapshot = pair.Value;
                    if (snapshot is null)
                    {
                        if (File.Exists(target))
                            File.Delete(target);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(snapshot, target, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new IOException($"Could not restore {pair.Key}.", ex));
                }
            }

            if (failures.Count == 0)
            {
                _log("[SUCCESS] Rollback completed; the pre-run file state was restored.");
                return;
            }

            _log($"[X] Rollback had {failures.Count} failure(s). Backup snapshots remain in {_snapshotRoot}.");
            throw new AggregateException("Patching failed and rollback was incomplete.", failures);
        }

        private void DeleteSnapshotDirectory()
        {
            if (Directory.Exists(_snapshotRoot))
                Directory.Delete(_snapshotRoot, recursive: true);
        }
    }
}
