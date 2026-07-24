using System;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Sims3ModernPatcher
{
    public static class HardwareDetector
    {
        public static HardwareInfo Detect()
        {
            string cpuName = ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString") ?? "Unknown processor";
            cpuName = Regex.Replace(cpuName.Trim(), @"\s+", " ");

            int logical = Environment.ProcessorCount;
            bool hybrid = IsHybridIntel(cpuName);

            var (gpuName, vram, vendor, vendorId, deviceId) = DetectPrimaryGpu();
            var (osName, isWin11) = DetectOperatingSystem();

            return new HardwareInfo
            {
                CpuName = cpuName,
                LogicalProcessors = logical,
                IsHybridIntelCpu = hybrid,
                GpuName = gpuName,
                GpuVramBytes = vram,
                GpuVendor = vendor,
                GpuVendorId = vendorId,
                GpuDeviceId = deviceId,
                OsName = osName,
                IsWindows11OrNewer = isWin11
            };
        }

        private static bool IsHybridIntel(string cpuName)
        {
            // Intel 12th gen (Alder Lake) and newer use P/E cores and break Sims 3 without a CPU patch.
            if (!cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                return false;

            Match m = Regex.Match(cpuName, @"\b([iI]\d|[uU]\d|[nN]\d)?-?(\d{2})\d{2,3}\b");
            if (m.Success && int.TryParse(m.Groups[2].Value, out int gen))
                return gen >= 12;

            return cpuName.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase)
                || cpuName.Contains("Alder Lake", StringComparison.OrdinalIgnoreCase)
                || cpuName.Contains("Raptor Lake", StringComparison.OrdinalIgnoreCase)
                || cpuName.Contains("Meteor Lake", StringComparison.OrdinalIgnoreCase)
                || cpuName.Contains("Arrow Lake", StringComparison.OrdinalIgnoreCase);
        }

        private static (string name, long vram, string vendor, string vendorId, string deviceId) DetectPrimaryGpu()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController");
                var controllers = searcher.Get().Cast<ManagementObject>()
                    .Select(mo => new
                    {
                        Name = (mo["Name"] as string)?.Trim() ?? string.Empty,
                        Pnp = (mo["PNPDeviceID"] as string) ?? string.Empty
                    })
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .Where(c => !c.Name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                    .Where(c => !c.Name.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (controllers.Count == 0)
                    return ("Unknown graphics card", 0, "Unknown", string.Empty, string.Empty);

                // Prefer discrete NVIDIA/AMD over Intel iGPU when both exist.
                var preferred = controllers
                    .OrderByDescending(c => VendorScore(c.Name, c.Pnp))
                    .First();

                string vendor = ClassifyVendor(preferred.Name, preferred.Pnp);
                string vendorId = ReadPnpId(preferred.Pnp, "VEN_");
                string deviceId = ReadPnpId(preferred.Pnp, "DEV_");
                // Win32_VideoController.AdapterRAM is a 32-bit value and commonly reports
                // modern 8–24 GB cards as 4 GB. Omitting it is more accurate than displaying it.
                return (preferred.Name, 0, vendor, vendorId, deviceId);
            }
            catch
            {
                return ("Unknown graphics card", 0, "Unknown", string.Empty, string.Empty);
            }
        }

        private static string ReadPnpId(string pnpDeviceId, string marker)
        {
            Match match = Regex.Match(
                pnpDeviceId,
                Regex.Escape(marker) + "([0-9A-Fa-f]{4})",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
        }

        private static int VendorScore(string name, string pnp)
        {
            string vendor = ClassifyVendor(name, pnp);
            return vendor switch
            {
                "NVIDIA" => 3,
                "AMD" => 2,
                "Intel" => 1,
                _ => 0
            };
        }

        private static string ClassifyVendor(string name, string pnp)
        {
            string hay = $"{name} {pnp}";
            if (hay.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ||
                hay.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                return "NVIDIA";
            if (hay.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ||
                hay.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                hay.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                return "AMD";
            if (hay.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ||
                hay.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                hay.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                return "Intel";
            return "Unknown";
        }

        private static (string name, bool isWin11) DetectOperatingSystem()
        {
            try
            {
                string product = ReadRegistryString(
                    Registry.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "ProductName") ?? "Windows";

                string displayVersion = ReadRegistryString(
                    Registry.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "DisplayVersion") ?? string.Empty;

                // ProductName still says "Windows 10" on many Win11 installs; build number is authoritative.
                int build = 0;
                string buildText = ReadRegistryString(
                    Registry.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuildNumber") ?? "0";
                int.TryParse(buildText, out build);

                bool isWin11 = build >= 22000;
                string edition = product;
                if (isWin11 && product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                    edition = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);

                string bitness = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
                string label = string.IsNullOrWhiteSpace(displayVersion)
                    ? $"{edition} {bitness}"
                    : $"{edition} ({displayVersion}) {bitness}";

                return (label.Trim(), isWin11);
            }
            catch
            {
                return ($"Windows {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}", false);
            }
        }

        private static string? ReadRegistryString(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                return key?.GetValue(valueName) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
