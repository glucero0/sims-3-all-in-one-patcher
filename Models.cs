using System.Collections.Generic;

namespace Sims3ModernPatcher
{
    public enum GamePlatform
    {
        Unknown,
        Steam,
        EaApp,
        DiscOrOther
    }

    public sealed class HardwareInfo
    {
        public string CpuName { get; init; } = "Unknown processor";
        public int LogicalProcessors { get; init; }
        public bool IsHybridIntelCpu { get; init; }
        public string GpuName { get; init; } = "Unknown graphics card";
        public long GpuVramBytes { get; init; }
        public string GpuVendor { get; init; } = "Unknown";
        public string GpuVendorId { get; init; } = string.Empty;
        public string GpuDeviceId { get; init; } = string.Empty;
        public string OsName { get; init; } = "Windows";
        public bool IsWindows11OrNewer { get; init; }

        public string CpuDisplay => LogicalProcessors > 0
            ? $"{CpuName} ({LogicalProcessors} threads)"
            : CpuName;

        public string GpuDisplay
        {
            get
            {
                if (GpuVramBytes <= 0) return GpuName;
                double gb = GpuVramBytes / (1024.0 * 1024.0 * 1024.0);
                return gb >= 1
                    ? $"{GpuName} ({gb:0.#} GB VRAM)"
                    : $"{GpuName} ({GpuVramBytes / (1024 * 1024)} MB VRAM)";
            }
        }
    }

    public sealed class GameInstall
    {
        public string Path { get; init; } = string.Empty;
        public GamePlatform Platform { get; init; }
        public string DisplayName => $"{PlatformLabel}: {Path}";

        public string PlatformLabel => Platform switch
        {
            GamePlatform.Steam => "Steam",
            GamePlatform.EaApp => "EA App",
            GamePlatform.DiscOrOther => "Disc / Other",
            _ => "Unknown"
        };
    }

    public sealed class ConflictChoice
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Explanation { get; init; } = string.Empty;
        public List<ConflictOption> Options { get; init; } = new();
        public string SelectedOptionId { get; set; } = string.Empty;
    }

    public sealed class ConflictOption
    {
        public string Id { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool IsRecommended { get; init; }
    }

    public sealed class PatchPlan
    {
        public GameInstall Install { get; init; } = new();
        public HardwareInfo Hardware { get; init; } = new();
        public Dictionary<string, string> Choices { get; init; } = new();
        public bool CreateDesktopShortcut { get; init; } = true;
    }

    public sealed class PatchResult
    {
        public bool DesktopShortcutCreated { get; init; }
        public string? SaveBackupPath { get; init; }
    }
}
