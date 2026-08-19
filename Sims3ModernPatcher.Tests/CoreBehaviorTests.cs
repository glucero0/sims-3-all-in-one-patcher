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
    public void GameLocator_RecognizesElectronicArtsFolderAsEaApp()
    {
        GamePlatform platform = GameLocator.InferPlatform(@"D:\Electronic Arts\The Sims 3");
        Assert.Equal(GamePlatform.EaApp, platform);
    }

    [Fact]
    public void GameLocator_ParsesModernAndLegacySteamLibraryFolders()
    {
        const string modern =
            """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """;

        const string legacy =
            """
            "LibraryFolders"
            {
            	"1"		"E:\\Games\\Steam"
            	"2"		"F:\\SteamLibrary"
            }
            """;

        string[] modernLibs = GameLocator.ParseSteamLibraryFoldersVdf(modern).ToArray();
        string[] legacyLibs = GameLocator.ParseSteamLibraryFoldersVdf(legacy).ToArray();

        Assert.Contains(@"C:\Program Files (x86)\Steam", modernLibs);
        Assert.Contains(@"D:\SteamLibrary", modernLibs);
        Assert.Contains(@"E:\Games\Steam", legacyLibs);
        Assert.Contains(@"F:\SteamLibrary", legacyLibs);
    }

    [Theory]
    [InlineData("The Sims 3", true)]
    [InlineData("The Sims™ 3", true)]
    [InlineData("The Sims 3 World Adventures", false)]
    [InlineData("The Sims 3 Pets", false)]
    [InlineData("Steam App 47890", false)]
    public void GameLocator_IdentifiesBaseGameDisplayNames(string displayName, bool expected)
    {
        Assert.Equal(expected, GameLocator.IsBaseSims3DisplayName(displayName));
    }

    [Fact]
    public void GameLocator_StandardCandidatesIncludeSteamAndEaDefaults()
    {
        var candidates = GameLocator.EnumerateStandardInstallCandidates().ToArray();

        Assert.Contains(
            candidates,
            c => c.platform == GamePlatform.Steam
                 && c.path.Contains(@"steamapps\common\The Sims 3", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            candidates,
            c => c.platform == GamePlatform.EaApp
                 && c.path.Contains(@"EA Games\The Sims 3", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            candidates,
            c => c.platform == GamePlatform.EaApp
                 && c.path.Contains(@"Electronic Arts\The Sims 3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PatchCatalog_DoesNotPutDxvkInConflictCards()
    {
        var conflicts = PatchCatalog.BuildConflicts(
            new[] { new GameInstall { Path = @"C:\Games\The Sims 3", Platform = GamePlatform.EaApp } },
            new HardwareInfo { GpuVendor = "AMD", GpuName = "Radeon" });

        Assert.Empty(conflicts);
    }

    [Fact]
    public void PatchCatalog_PrefersDxvkForAllNvidia()
    {
        Assert.True(PatchCatalog.PrefersDxvk(new HardwareInfo
        {
            GpuVendor = "NVIDIA",
            GpuName = "NVIDIA GeForce RTX 5050"
        }));
        Assert.True(PatchCatalog.PrefersDxvk(new HardwareInfo
        {
            GpuVendor = "NVIDIA",
            GpuName = "NVIDIA GeForce GTX 1060"
        }));
        Assert.False(PatchCatalog.PrefersDxvk(new HardwareInfo
        {
            GpuVendor = "AMD",
            GpuName = "Radeon RX 7800 XT"
        }));
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
        Assert.Contains("CASPartCache.package", script);
        Assert.Contains("CurrentGame.sims3", script);
        Assert.Contains("rd /S /Q \"%DOCS%\\CurrentGame.sims3\"", script);
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
    public void ZipExtractor_PrefersFirstCandidateName()
    {
        using var temp = new TemporaryDirectory();
        string archivePath = Path.Combine(temp.Path, "loader.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "dinput8.dll", "fallback");
            WriteZipEntry(archive, "wininet.dll", "preferred");
        }

        string destination = Path.Combine(temp.Path, "out", "wininet.dll");
        SafeArchiveExtractor.ExtractZipEntry(
            archivePath,
            new[] { "wininet.dll", "dinput8.dll" },
            destination);

        Assert.Equal("preferred", File.ReadAllText(destination));
    }

    [Fact]
    public void ZipExtractor_FallsBackToAlternateLoaderName()
    {
        using var temp = new TemporaryDirectory();
        string archivePath = Path.Combine(temp.Path, "loader.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "dinput8.dll", "combined");
        }

        string destination = Path.Combine(temp.Path, "out", "wininet.dll");
        SafeArchiveExtractor.ExtractZipEntry(
            archivePath,
            new[] { "wininet.dll", "dinput8.dll" },
            destination);

        Assert.Equal("combined", File.ReadAllText(destination));
    }

    [Fact]
    public void GitHubReleaseAssets_FindsNamedAssetAndSha256Digest()
    {
        const string json =
            """
            {
              "tag_name": "Win32-latest",
              "assets": [
                {
                  "name": "dinput8-Win32.zip",
                  "url": "https://api.github.com/repos/ThirteenAG/Ultimate-ASI-Loader/releases/assets/1",
                  "browser_download_url": "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/download/Win32-latest/dinput8-Win32.zip",
                  "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                },
                {
                  "name": "wininet-Win32.zip",
                  "url": "https://api.github.com/repos/ThirteenAG/Ultimate-ASI-Loader/releases/assets/516768648",
                  "browser_download_url": "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/download/Win32-latest/wininet-Win32.zip",
                  "digest": "sha256:2C4B26C316755A67CE14661131ED196398CCA242F7F6363105947A934B09112C"
                }
              ]
            }
            """;

        GitHubReleaseAsset? asset = GitHubReleaseAssets.FindByName(json, "wininet-Win32.zip");

        Assert.NotNull(asset);
        Assert.Equal("wininet-Win32.zip", asset!.Name);
        Assert.Equal(
            "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/download/Win32-latest/wininet-Win32.zip",
            asset.BrowserDownloadUrl);
        Assert.Equal(
            "https://api.github.com/repos/ThirteenAG/Ultimate-ASI-Loader/releases/assets/516768648",
            asset.ApiDownloadUrl);
        Assert.Equal("2c4b26c316755a67ce14661131ed196398cca242f7f6363105947a934b09112c", asset.Sha256);
    }

    [Fact]
    public void GitHubReleaseAssets_ReturnsNullWhenAssetIsMissing()
    {
        const string json = """{ "assets": [ { "name": "dinput8-Win32.zip" } ] }""";

        Assert.Null(GitHubReleaseAssets.FindByName(json, "wininet-Win32.zip"));
    }

    [Theory]
    [InlineData("sha256:2c4b26c316755a67ce14661131ed196398cca242f7f6363105947a934b09112c", "2c4b26c316755a67ce14661131ed196398cca242f7f6363105947a934b09112c")]
    [InlineData("SHA256:2C4B26C316755A67CE14661131ED196398CCA242F7F6363105947A934B09112C", "2c4b26c316755a67ce14661131ed196398cca242f7f6363105947a934b09112c")]
    [InlineData("md5:not-a-sha", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GitHubReleaseAssets_ParsesSha256Digest(string? digest, string? expected)
    {
        Assert.Equal(expected, GitHubReleaseAssets.ParseSha256Digest(digest));
    }

    [Fact]
    public void AsiLoaderManualInstructions_TellUserWhereToPlaceTheZipAndRetry()
    {
        string cacheDir = Path.Combine("C:\\", "Users", "example", "AppData", "Local", "Sims3ModernPatcher", "cache");

        string text = AsiLoaderCache.BuildManualInstructions(
            cacheDir,
            new[] { "Download failed (404 Not Found): https://example.invalid/wininet-Win32.zip" });

        Assert.Contains("Could not download Ultimate ASI Loader automatically.", text);
        Assert.Contains("Auto download error: Download failed (404 Not Found)", text);
        Assert.Contains(AsiLoaderCache.GitHubRepoUrl, text);
        Assert.Contains(AsiLoaderCache.NamedZipUrl, text);
        Assert.Contains(AsiLoaderCache.CombinedZipUrl, text);
        Assert.Contains(AsiLoaderCache.NamedZipFileName, text);
        Assert.Contains(AsiLoaderCache.CombinedZipFileName, text);
        Assert.Contains(Path.GetFullPath(cacheDir), text);
        Assert.Contains(Path.GetFullPath(AsiLoaderCache.GetCanonicalZipPath(cacheDir)), text);
        Assert.Contains("retry the patching process", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("press GO again", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AsiLoaderCache_PersistsManualRequestAndUsesLocalZipFirst()
    {
        using var temp = new TemporaryDirectory();
        string cacheDir = Path.Combine(temp.Path, "cache");
        Directory.CreateDirectory(cacheDir);
        var log = new List<string>();

        Assert.False(AsiLoaderCache.TryUseManualLocalArchive(cacheDir, log.Add));

        AsiLoaderCache.MarkManualDownloadRequested(cacheDir);
        Assert.True(AsiLoaderCache.ManualDownloadWasRequested(cacheDir));
        Assert.True(File.Exists(AsiLoaderCache.GetMarkerPath(cacheDir)));
        Assert.False(AsiLoaderCache.TryUseManualLocalArchive(cacheDir, log.Add));
        Assert.Contains(log, line => line.Contains("Manual ASI Loader download was requested", StringComparison.Ordinal));

        string combined = Path.Combine(cacheDir, AsiLoaderCache.CombinedZipFileName);
        using (ZipArchive archive = ZipFile.Open(combined, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("dinput8.dll");
            using StreamWriter writer = new(entry.Open());
            writer.Write("loader");
        }

        log.Clear();
        Assert.True(AsiLoaderCache.TryUseManualLocalArchive(cacheDir, log.Add));
        Assert.False(AsiLoaderCache.ManualDownloadWasRequested(cacheDir));
        Assert.True(AsiLoaderCache.IsUsableArchive(AsiLoaderCache.GetCanonicalZipPath(cacheDir)));
        Assert.Contains(log, line => line.Contains("skipping network", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AsiLoaderCache_FindsNamedWininetZip()
    {
        using var temp = new TemporaryDirectory();
        string named = Path.Combine(temp.Path, AsiLoaderCache.NamedZipFileName);
        using (ZipArchive archive = ZipFile.Open(named, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("wininet.dll");
            using StreamWriter writer = new(entry.Open());
            writer.Write("named");
        }

        Assert.Equal(named, AsiLoaderCache.FindUsableArchive(temp.Path));
        Assert.False(AsiLoaderCache.TryUseManualLocalArchive(temp.Path, _ => { }));
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

    [Fact]
    public void PatcherSessionLog_WritesTimestampedFileUnderLocalApplicationData()
    {
        string root = PatcherSessionLog.GetLogRoot();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, root, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("Sims3ModernPatcher", "Logs"),
            root,
            StringComparison.OrdinalIgnoreCase);

        string filePath;
        using (PatcherSessionLog session = PatcherSessionLog.StartNew("unit-test"))
        {
            session.WriteLine("[+] unit test line");
            filePath = session.FilePath;
            Assert.True(File.Exists(filePath));
        }

        string contents = File.ReadAllText(filePath);
        Assert.Contains("unit-test", contents, StringComparison.Ordinal);
        Assert.Contains("unit test line", contents, StringComparison.Ordinal);
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
