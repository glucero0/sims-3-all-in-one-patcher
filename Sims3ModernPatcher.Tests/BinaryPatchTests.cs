using System.IO;
using Sims3ModernPatcher;
using Xunit;

namespace Sims3ModernPatcher.Tests;

public sealed class BinaryPatchTests
{
    [Fact]
    public void LargeAddressAware_SetsFlagCreatesBackupAndIsIdempotent()
    {
        using var temp = new TemporaryDirectory();
        string executable = Path.Combine(temp.Path, "TS3.exe");
        string backup = Path.Combine(temp.Path, "backup", "TS3.exe.bak");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.WriteAllBytes(executable, CreateMinimalPe());

        bool firstChanged = PeLargeAddressAware.Apply(executable, backup);
        bool secondChanged = PeLargeAddressAware.Apply(executable, backup);

        Assert.True(firstChanged);
        Assert.False(secondChanged);
        Assert.True(File.Exists(backup));
        Assert.False(HasLargeAddressAwareFlag(File.ReadAllBytes(backup)));
        Assert.True(HasLargeAddressAwareFlag(File.ReadAllBytes(executable)));
    }

    [Fact]
    public void LargeAddressAware_RejectsInvalidExecutableWithoutCreatingBackup()
    {
        using var temp = new TemporaryDirectory();
        string executable = Path.Combine(temp.Path, "TS3.exe");
        string backup = Path.Combine(temp.Path, "TS3.exe.bak");
        File.WriteAllText(executable, "not a PE file");

        Assert.Throws<InvalidDataException>(
            () => PeLargeAddressAware.Apply(executable, backup));
        Assert.False(File.Exists(backup));
    }

    [Fact]
    public void IntegrityVerifier_AcceptsMatchingHashAndRejectsMismatch()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "download.bin");
        File.WriteAllText(file, "known data");
        string hash = FileIntegrity.ComputeSha256(file);

        FileIntegrity.VerifySha256(file, hash);
        Assert.Throws<InvalidDataException>(
            () => FileIntegrity.VerifySha256(file, new string('0', 64)));
    }

    [Fact]
    public void RollbackSession_RestoresExistingFilesAndRemovesNewFiles()
    {
        using var temp = new TemporaryDirectory();
        string existing = Path.Combine(temp.Path, "existing.dll");
        string created = Path.Combine(temp.Path, "created.dll");
        File.WriteAllText(existing, "original");

        using (var rollback = new FileRollbackSession(_ => { }))
        {
            rollback.Capture(existing);
            rollback.Capture(created);
            File.WriteAllText(existing, "modified");
            File.WriteAllText(created, "new");
        }

        Assert.Equal("original", File.ReadAllText(existing));
        Assert.False(File.Exists(created));
    }

    [Fact]
    public void RollbackSession_CommitKeepsChanges()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "file.dll");
        File.WriteAllText(file, "original");

        using (var rollback = new FileRollbackSession(_ => { }))
        {
            rollback.Capture(file);
            File.WriteAllText(file, "modified");
            rollback.Commit();
        }

        Assert.Equal("modified", File.ReadAllText(file));
    }

    private static byte[] CreateMinimalPe()
    {
        const int peOffset = 0x80;
        byte[] bytes = new byte[peOffset + 24];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(bytes, 0x3C);
        bytes[peOffset] = (byte)'P';
        bytes[peOffset + 1] = (byte)'E';
        return bytes;
    }

    private static bool HasLargeAddressAwareFlag(byte[] bytes)
    {
        int peOffset = BitConverter.ToInt32(bytes, 0x3C);
        ushort characteristics = BitConverter.ToUInt16(bytes, peOffset + 22);
        return (characteristics & 0x0020) != 0;
    }
}
