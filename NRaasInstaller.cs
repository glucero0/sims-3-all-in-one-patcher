using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Sims3ModernPatcher
{
    /// <summary>
    /// Downloads NRaas stability mods from nraas.net into Mods\Packages.
    /// ErrorTrap is a core mod and must match Steam vs EA/Disc patch builds.
    /// </summary>
    public static class NRaasInstaller
    {
        private const string CommunityBase = "https://www.nraas.net/community/";

        private static readonly HttpClient Http = CreateClient();

        public static async Task InstallStabilityModsAsync(
            GameInstall install,
            string packagesDir,
            string cacheDir,
            Action<string> log)
        {
            Directory.CreateDirectory(packagesDir);
            Directory.CreateDirectory(cacheDir);

            foreach (var (page, zipName, packageName, sha256, label) in GetDownloads(install, log))
            {
                log($"[*] Downloading NRaas {label}...");
                string zipPath = Path.Combine(cacheDir, zipName);
                await EnsureZipDownloadedAsync(page, zipName, zipPath, sha256, log);
                string destination = Path.Combine(packagesDir, packageName);
                SafeArchiveExtractor.ExtractZipEntry(zipPath, packageName, destination);
                log($"[SUCCESS] Installed NRaas {label}: {packageName}.");
            }

            log("[+] NRaas stability set ready: ErrorTrap, Overwatch, Traveler, Saver.");
        }

        public static async Task PreflightDownloadsAsync(
            GameInstall install,
            string cacheDir,
            Action<string> log)
        {
            Directory.CreateDirectory(cacheDir);
            foreach (var (page, zipName, _, sha256, label) in GetDownloads(install, log))
            {
                log($"[*] Preflight: NRaas {label}...");
                string zipPath = Path.Combine(cacheDir, zipName);
                await EnsureZipDownloadedAsync(page, zipName, zipPath, sha256, log);
            }
        }

        private static (string page, string zipName, string packageName, string sha256, string label)[]
            GetDownloads(GameInstall install, Action<string> log)
        {
            string errorTrapFile = SelectErrorTrapArchive(install, log);
            return
            [
                ("ErrorTrap", errorTrapFile, "NRaas_ErrorTrap.package", GetExpectedHash(errorTrapFile), "ErrorTrap"),
                ("Overwatch", "NRaas_Overwatch_V123.zip", "NRaas_Overwatch.package", "5602c93b436d7ad69d11098a5167598bbe168a2e7e40dd3b6d2020f572f2a537", "Overwatch"),
                ("Traveler", "NRaas_Traveler_V89.zip", "NRaas_Traveler.package", "fe9f56e732682e39e5cb9709b7044471fd26f7ba0ec55b49930bad80ba9e4371", "Traveler"),
                ("Saver", "NRaas_Saver_V21.zip", "NRaas_Saver.package", "7d11c205fff290a2a46f48444394e2364c85f04d6944879a146d802b7ff85a09", "Saver"),
            ];
        }

        private static async Task EnsureZipDownloadedAsync(
            string page,
            string zipName,
            string zipPath,
            string sha256,
            Action<string> log)
        {
            if (!IsVerifiedZip(zipPath, sha256))
                await EnsureCommunitySessionAsync(page);

            string url = CommunityBase + "download/" + zipName;
            await DownloadZipAsync(url, zipPath, sha256, log);
        }

        private static bool IsVerifiedZip(string path, string expectedSha256)
        {
            if (!IsZipFile(path))
                return false;
            try
            {
                FileIntegrity.VerifySha256(path, expectedSha256);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        private static string GetExpectedHash(string zipName)
        {
            return zipName switch
            {
                "NRaas_ErrorTrap_P167_V100_Steam.zip" => "b0fb849f4d587be38756b12f7c67c6f73aeac6b70d4d96fa60256adbef5199e0",
                "NRaas_ErrorTrap_P167_V100.zip" => "6784e5db3de97c406e8b5ebd10698f34915d5b5b084be0a700ea1f859deb336d",
                "NRaas_ErrorTrap_P169_V100.zip" => "5b778b2d0498a8350a6263a6167d8e9037ee7ec5991ebd8409b2f21dff9d9134",
                _ => throw new InvalidOperationException($"No integrity hash is registered for {zipName}.")
            };
        }

        internal static string SelectErrorTrapArchive(GameInstall install, Action<string> log)
        {
            string? version = GameVersionReader.Read(install.Path);
            if (!string.IsNullOrWhiteSpace(version))
                log($"[+] Detected Sims 3 GameVersion: {version}");
            else
                log("[!] skuversion.txt not found — picking ErrorTrap from storefront.");

            // Steam stays on the 1.67 Steam core build.
            if (install.Platform == GamePlatform.Steam)
            {
                log("[+] Selecting ErrorTrap variant: Patch 1.67 Steam.");
                return "NRaas_ErrorTrap_P167_V100_Steam.zip";
            }

            if (!string.IsNullOrWhiteSpace(version))
            {
                if (version.StartsWith("1.69", StringComparison.Ordinal))
                {
                    log("[+] Selecting ErrorTrap variant: Patch 1.69 (EA App).");
                    return "NRaas_ErrorTrap_P169_V100.zip";
                }

                if (!version.StartsWith("1.67", StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"Sims 3 version {version} is not a supported Windows ErrorTrap build. " +
                        "Supported versions are Steam/Retail 1.67 and EA App 1.69.");
                }
            }
            else if (install.Platform == GamePlatform.EaApp)
            {
                // EA digital installs are usually 1.69+ when skuversion is missing.
                log("[+] Selecting ErrorTrap variant: Patch 1.69 (EA App default).");
                return "NRaas_ErrorTrap_P169_V100.zip";
            }
            else if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException(
                    "The Sims 3 patch version could not be detected. ErrorTrap cannot be installed safely. " +
                    "Repair or update the game so Game\\Bin\\skuversion.txt is available, then try again.");
            }

            log("[+] Selecting ErrorTrap variant: Patch 1.67 (non-Steam).");
            return "NRaas_ErrorTrap_P167_V100.zip";
        }

        private static async Task EnsureCommunitySessionAsync(string pageName)
        {
            // nraas.net gates /community/download/* behind a session established by visiting a community page.
            using var response = await Http.GetAsync(CommunityBase + pageName);
            response.EnsureSuccessStatusCode();
            _ = await response.Content.ReadAsByteArrayAsync();
        }

        private static async Task DownloadZipAsync(
            string url,
            string destination,
            string expectedSha256,
            Action<string> log)
        {
            if (File.Exists(destination) && IsZipFile(destination))
            {
                try
                {
                    FileIntegrity.VerifySha256(destination, expectedSha256);
                    log($"[INFO] Using verified cached download: {Path.GetFileName(destination)}");
                    return;
                }
                catch (InvalidDataException)
                {
                    File.Delete(destination);
                }
            }

            string temporary = destination + ".download";
            if (File.Exists(temporary))
                File.Delete(temporary);

            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            string? contentType = response.Content.Headers.ContentType?.MediaType;
            await using var remote = await response.Content.ReadAsStreamAsync();
            await using (var local = File.Create(temporary))
            {
                await remote.CopyToAsync(local);
                await local.FlushAsync();
            }

            if (!IsZipFile(temporary))
            {
                File.Delete(temporary);
                throw new InvalidOperationException(
                    $"Download did not return a ZIP ({contentType ?? "unknown type"}): {url}");
            }

            FileIntegrity.VerifySha256(temporary, expectedSha256);
            File.Move(temporary, destination, overwrite: true);
            log($"[+] Downloaded {Path.GetFileName(destination)}");
        }

        private static bool IsZipFile(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length < 4)
                    return false;
                using var fs = File.OpenRead(path);
                return fs.ReadByte() == 0x50 && fs.ReadByte() == 0x4B;
            }
            catch
            {
                return false;
            }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            };

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            return client;
        }
    }
}
