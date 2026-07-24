using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using Sims3ModernPatcher;
using Xunit;

namespace Sims3ModernPatcher.Tests;

public sealed class CoreBehaviorTests
{
    [Fact]
    public void HardwareAndInstallDetection_RunWithoutHardcodedValues()
    {
        HardwareInfo hardware = HardwareDetector.Detect();
        List<GameInstall> installs = GameLocator.FindAllInstallations();

        Assert.False(string.IsNullOrWhiteSpace(hardware.CpuName));
        Assert.False(string.IsNullOrWhiteSpace(hardware.GpuName));
        Assert.False(string.IsNullOrWhiteSpace(hardware.OsName));
        Assert.NotNull(installs);
    }

    [Theory]
    [InlineData(GamePlatform.Steam, "1.67.2.024037", "TS3W.exe")]
    [InlineData(GamePlatform.DiscOrOther, "1.67.2.024001", "TS3W.exe")]
    [InlineData(GamePlatform.EaApp, "1.69.47.024017", "TS3.exe")]
    [InlineData(GamePlatform.EaApp, "1.70.0.000000", "TS3.exe")]
    public void ExecutableSelector_UsesCorrectStorefrontBinary(
        GamePlatform platform,
        string version,
        string expected)
    {
        string[] existing = { "TS3W.exe", "TS3.exe" };

        string actual = GameExecutableSelector.SelectPrimary(existing, platform, version);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ExecutableSelector_FallsBackWhenPreferredBinaryIsMissing()
    {
        string actual = GameExecutableSelector.SelectPrimary(
            new[] { "TS3.exe" },
            GamePlatform.Steam,
            "1.67.2.024037");

        Assert.Equal("TS3.exe", actual);
    }

    [Fact]
    public void GraphicsRulesEditor_ChangesActualTextureFallbackKeys()
    {
        const string input =
            "if ($textureMemory == 0)\n" +
            "  seti textureMemory       32\n" +
            "  setb textureMemorySizeOK false\n" +
            "endif\n";

        string output = GraphicsRulesEditor.ApplyTextureMemoryFallback(input, out bool changed);

        Assert.True(changed);
        Assert.Contains("seti textureMemory 1024", output);
        Assert.Contains("# setb textureMemorySizeOK false", output);
        Assert.DoesNotContain("seti textureMemory       32", output);
    }

    [Fact]
    public void GraphicsRulesEditor_IsIdempotent()
    {
        const string input =
            "  seti textureMemory 1024\n" +
            "  # setb textureMemorySizeOK false\n";

        string output = GraphicsRulesEditor.ApplyTextureMemoryFallback(input, out bool changed);

        Assert.False(changed);
        Assert.Equal(input, output);
    }

    [Fact]
    public void GraphicsCardsEditor_AddsExactDetectedPciDeviceIdOnce()
    {
        const string input =
            "vendor \"NVIDIA\" 0x10b4 0x12d2 0x10de\n" +
            "    card 0x0fd1 \"GeForce GT 650M\"\n" +
            "vendor \"ATI\" 0x1002\n";

        string first = GraphicsCardsEditor.AddDetectedCard(
            input,
            "10de",
            "2704",
            "NVIDIA GeForce RTX 4080",
            out bool firstChanged);
        string second = GraphicsCardsEditor.AddDetectedCard(
            first,
            "10de",
            "2704",
            "NVIDIA GeForce RTX 4080",
            out bool secondChanged);

        Assert.True(firstChanged);
        Assert.False(secondChanged);
        Assert.Contains("card 0x2704 \"NVIDIA GeForce RTX 4080\"", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GraphicsCardsEditor_AddsMissingIntelVendorSection()
    {
        const string input = "vendor \"NVIDIA\" 0x10de\n";

        string output = GraphicsCardsEditor.AddDetectedCard(
            input,
            "8086",
            "56a0",
            "Intel Arc A770",
            out bool changed);

        Assert.True(changed);
        Assert.Contains("vendor \"Intel\" 0x8086", output);
        Assert.Contains("card 0x56a0 \"Intel Arc A770\"", output);
    }

    [Fact]
    public void GameVersionReader_ReadsSkuVersion()
    {
        using var temp = new TemporaryDirectory();
        string bin = Path.Combine(temp.Path, "Game", "Bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(
            Path.Combine(bin, "skuversion.txt"),
            "GameVersion = 1.69.47.024017\nCode:1.0.0.18\n");

        string? version = GameVersionReader.Read(temp.Path);

        Assert.Equal("1.69.47.024017", version);
    }

    [Theory]
    [InlineData("TS3.exe")]
    [InlineData("TS3W.exe")]
    public void GameLocator_NormalizesGameBinToInstallRoot(string executableName)
    {
        using var temp = new TemporaryDirectory();
        string bin = Path.Combine(temp.Path, "Game", "Bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, executableName), Array.Empty<byte>());

        string normalized = GameLocator.NormalizeInstallRoot(bin);

        Assert.Equal(Path.GetFullPath(temp.Path), normalized);
        Assert.True(GameLocator.IsValidSims3Install(normalized));
    }

    [Theory]
    [InlineData("Game")]
    [InlineData("Game\\Bin")]
    [InlineData("Game\\Bin\\TS3.exe")]
    public void GameLocator_NormalizesNestedGamePaths(string selectedRelativePath)
    {
        using var temp = new TemporaryDirectory();
        string bin = Path.Combine(temp.Path, "Game", "Bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "TS3.exe"), Array.Empty<byte>());

        string normalized = GameLocator.NormalizeInstallRoot(
            Path.Combine(temp.Path, selectedRelativePath));

        Assert.Equal(Path.GetFullPath(temp.Path), normalized);
    }

    [Fact]
    public void GameLocator_RecognizesSteamInstallOutsideDefaultSteamPath()
    {
        using var temp = new TemporaryDirectory();
        string bin = Path.Combine(temp.Path, "Game", "Bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "TS3W.exe"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(bin, "steam_api.dll"), Array.Empty<byte>());

        GamePlatform platform = GameLocator.InferPlatform(temp.Path);

        Assert.Equal(GamePlatform.Steam, platform);
    }

    [Fact]
    public void PatchCatalog_DefaultsToNativeDirectXForReliability()
    {
        var conflicts = PatchCatalog.BuildConflicts(
            new[] { new GameInstall { Path = @"C:\Games\The Sims 3", Platform = GamePlatform.EaApp } },
            new HardwareInfo { GpuVendor = "AMD" });

        ConflictChoice graphics = Assert.Single(conflicts);
        Assert.Equal(PatchCatalog.ChoiceGraphicsApi, graphics.Id);
        Assert.Equal(PatchCatalog.OptDxNative, graphics.SelectedOptionId);
        Assert.True(graphics.Options.Single(o => o.Id == PatchCatalog.OptDxNative).IsRecommended);
    }

    [Theory]
    [InlineData(GamePlatform.Steam, "1.67.2.024037", "NRaas_ErrorTrap_P167_V100_Steam.zip")]
    [InlineData(GamePlatform.DiscOrOther, "1.67.2.024001", "NRaas_ErrorTrap_P167_V100.zip")]
    [InlineData(GamePlatform.EaApp, "1.69.47.024017", "NRaas_ErrorTrap_P169_V100.zip")]
    public void ErrorTrapSelection_MatchesStorefrontAndGameVersion(
        GamePlatform platform,
        string version,
        string expectedArchive)
    {
        using var temp = new TemporaryDirectory();
        string bin = Path.Combine(temp.Path, "Game", "Bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "skuversion.txt"), $"GameVersion = {version}\n");
        var install = new GameInstall { Path = temp.Path, Platform = platform };

        string archive = NRaasInstaller.SelectErrorTrapArchive(install, _ => { });

        Assert.Equal(expectedArchive, archive);
    }

    [Fact]
    public void ErrorTrapSelection_RejectsUnsupportedWindowsVersion()
    {
        using var temp = new TemporaryDirectory();
        string bin = Path.Combine(temp.Path, "Game", "Bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "skuversion.txt"), "GameVersion = 1.70.0.000000\n");
        var install = new GameInstall { Path = temp.Path, Platform = GamePlatform.EaApp };

        Assert.Throws<NotSupportedException>(
            () => NRaasInstaller.SelectErrorTrapArchive(install, _ => { }));
    }

    [Fact]
    public void PatchCatalog_ResolvesSelectedInstall()
    {
        var installs = new[]
        {
            new GameInstall { Path = "steam", Platform = GamePlatform.Steam },
            new GameInstall { Path = "ea", Platform = GamePlatform.EaApp }
        };
        var choices = new Dictionary<string, string>
        {
            [PatchCatalog.ChoiceInstall] = PatchCatalog.OptInstallPrefix + "1"
        };

        GameInstall selected = PatchCatalog.ResolveInstall(installs, choices);

        Assert.Equal("ea", selected.Path);
    }

    [Fact]
    public void PatchCatalog_RequiresExplicitChoiceForMultipleInstalls()
    {
        var installs = new[]
        {
            new GameInstall { Path = "steam", Platform = GamePlatform.Steam },
            new GameInstall { Path = "ea", Platform = GamePlatform.EaApp }
        };

        Assert.Throws<InvalidOperationException>(
            () => PatchCatalog.ResolveInstall(
                installs,
                new Dictionary<string, string>()));
    }

    [Fact]
    public void PatchCatalog_DoesNotPreselectAStorefrontWhenMultipleInstallsExist()
    {
        var installs = new[]
        {
            new GameInstall { Path = "steam", Platform = GamePlatform.Steam },
            new GameInstall { Path = "ea", Platform = GamePlatform.EaApp }
        };

        ConflictChoice installChoice = PatchCatalog.BuildConflicts(
                installs,
                new HardwareInfo { GpuVendor = "NVIDIA" })
            .Single(choice => choice.Id == PatchCatalog.ChoiceInstall);

        Assert.Empty(installChoice.SelectedOptionId);
        Assert.All(installChoice.Options, option => Assert.False(option.IsRecommended));
    }

    [Fact]
    public void Launcher_UsesRequestedBinaryAndNeverKillsRunningGame()
    {
        string script = PatcherEngine.BuildLauncherScript("TS3.exe");

        Assert.Contains("if exist \"TS3.exe\"", script);
        Assert.Contains("Starting TS3.exe", script);
        Assert.Contains("already running. It was left untouched", script);
        Assert.DoesNotContain("taskkill", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/affinity", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Launcher_UsesSteamProtocolForSteamInstall()
    {
        string script = PatcherEngine.BuildLauncherScript(
            "TS3W.exe",
            GamePlatform.Steam);

        Assert.Contains("steam://run/47890", script);
        Assert.DoesNotContain("start \"\" /abovenormal \"TS3W.exe\"", script);
    }

    [Fact]
    public void ZipExtractor_ExtractsOnlyRequestedPackage()
    {
        using var temp = new TemporaryDirectory();
        string archivePath = Path.Combine(temp.Path, "mods.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "nested/NRaas_Overwatch.package", "expected");
            WriteZipEntry(archive, "../../unrelated.exe", "bad");
        }

        string destination = Path.Combine(temp.Path, "out", "NRaas_Overwatch.package");
        SafeArchiveExtractor.ExtractZipEntry(
            archivePath,
            "NRaas_Overwatch.package",
            destination);

        Assert.Equal("expected", File.ReadAllText(destination));
        Assert.False(File.Exists(Path.Combine(temp.Path, "unrelated.exe")));
    }

    [Fact]
    public void TarGzExtractor_ExtractsOnlyExactArchitectureSuffix()
    {
        using var temp = new TemporaryDirectory();
        string archivePath = Path.Combine(temp.Path, "dxvk.tar.gz");
        using (FileStream file = File.Create(archivePath))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var writer = new TarWriter(gzip))
        {
            WriteTarEntry(writer, "dxvk/x64/d3d9.dll", "wrong");
            WriteTarEntry(writer, "dxvk/x32/d3d9.dll", "correct");
        }

        string destination = Path.Combine(temp.Path, "out", "d3d9.dll");
        SafeArchiveExtractor.ExtractTarGzEntry(
            archivePath,
            "/x32/d3d9.dll",
            destination);

        Assert.Equal("correct", File.ReadAllText(destination));
    }

    [Fact]
    public void SaveBackup_CreatesTimestampedArchiveWithoutChangingSourceSave()
    {
        using var temp = new TemporaryDirectory();
        string docs = Path.Combine(temp.Path, "Documents", "Electronic Arts", "The Sims 3");
        string save = Path.Combine(docs, "Saves", "SunsetValley.sims3", "Meta.data");
        Directory.CreateDirectory(Path.GetDirectoryName(save)!);
        File.WriteAllText(save, "save data");
        string backupRoot = Path.Combine(temp.Path, "Backups");

        string? archivePath = SaveBackupManager.CreateSnapshot(docs, backupRoot, _ => { });

        Assert.NotNull(archivePath);
        Assert.True(File.Exists(archivePath));
        Assert.Equal("save data", File.ReadAllText(save));
        using ZipArchive archive = ZipFile.OpenRead(archivePath!);
        Assert.Contains(
            archive.Entries,
            entry => entry.FullName.Replace('\\', '/')
                .EndsWith("Saves/SunsetValley.sims3/Meta.data", StringComparison.Ordinal));
    }

    [Fact]
    public void SaveBackup_ReturnsNullWhenNoSavesExist()
    {
        using var temp = new TemporaryDirectory();
        string docs = Path.Combine(temp.Path, "Documents", "Electronic Arts", "The Sims 3");
        Directory.CreateDirectory(docs);

        string? archivePath = SaveBackupManager.CreateSnapshot(
            docs,
            Path.Combine(temp.Path, "Backups"),
            _ => { });

        Assert.Null(archivePath);
    }

    [Fact]
    public void SaveBackupRoot_IsUnderLocalApplicationData()
    {
        string root = SaveBackupManager.GetBackupRoot();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, root, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("Sims3ModernPatcher", "SaveBackups"),
            root,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteZipEntry(ZipArchive archive, string name, string contents)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using StreamWriter writer = new(entry.Open());
        writer.Write(contents);
    }

    private static void WriteTarEntry(TarWriter writer, string name, string contents)
    {
        byte[] data = System.Text.Encoding.UTF8.GetBytes(contents);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(data)
        };
        writer.WriteEntry(entry);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Sims3ModernPatcher.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
