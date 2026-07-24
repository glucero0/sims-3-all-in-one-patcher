using System;
using System.Collections.Generic;
using System.Linq;

namespace Sims3ModernPatcher
{
    public static class PatchCatalog
    {
        public const string ChoiceInstall = "install";
        public const string ChoiceGraphicsApi = "graphics_api";

        public const string OptInstallPrefix = "install:";
        public const string OptDxNative = "dx_native";
        public const string OptDxvk = "dxvk";

        public static List<ConflictChoice> BuildConflicts(
            IReadOnlyList<GameInstall> installs,
            HardwareInfo hardware)
        {
            var conflicts = new List<ConflictChoice>();

            if (installs.Count > 1)
            {
                var options = installs.Select((install, index) => new ConflictOption
                {
                    Id = OptInstallPrefix + index,
                    Label = install.PlatformLabel + " install",
                    Description = "Found at:\n" + install.Path +
                                  "\n\nPick the copy of The Sims 3 you actually play. Only that folder will be patched.",
                    IsRecommended = false
                }).ToList();

                conflicts.Add(new ConflictChoice
                {
                    Id = ChoiceInstall,
                    Title = "Which Sims 3 install should we patch?",
                    Explanation =
                        "This PC has more than one Sims 3 folder (for example Steam and EA App). " +
                        "They are separate installs, so we need you to choose which one to update.",
                    Options = options,
                    SelectedOptionId = string.Empty
                });
            }

            // DXVK replaces d3d9.dll and conflicts with staying on native DirectX 9.
            // Only ask when the benefit is uncertain (AMD/Intel). NVIDIA defaults to native.
            if (hardware.GpuVendor is "AMD" or "Intel" or "Unknown")
            {
                conflicts.Add(new ConflictChoice
                {
                    Id = ChoiceGraphicsApi,
                    Title = "Which graphics path should the game use?",
                    Explanation =
                        "The Sims 3 talks to your graphics card through old DirectX 9. " +
                        "DXVK is an alternate translator that can help some AMD/Intel systems, but it adds another compatibility layer. For maximum reliability, try built-in DirectX first.",
                    Options = new List<ConflictOption>
                    {
                        new()
                        {
                            Id = OptDxNative,
                            Label = "Keep built-in DirectX (recommended)",
                            Description =
                                "Uses the game’s original graphics path and the fewest extra components. " +
                                "Choose this for the safest day-to-day setup.",
                            IsRecommended = true
                        },
                        new()
                        {
                            Id = OptDxvk,
                            Label = "Install DXVK",
                            Description =
                                "Can fix graphics-related crashes or stuttering on some AMD/Intel systems. " +
                                "Try it only if built-in DirectX is unstable; you can re-run this tool to remove it.",
                            IsRecommended = false
                        }
                    },
                    SelectedOptionId = OptDxNative
                });
            }

            return conflicts;
        }

        public static GameInstall ResolveInstall(
            IReadOnlyList<GameInstall> installs,
            IReadOnlyDictionary<string, string> choices)
        {
            if (installs.Count == 0)
                return new GameInstall();

            if (installs.Count == 1)
                return installs[0];

            if (choices.TryGetValue(ChoiceInstall, out string? selected)
                && selected.StartsWith(OptInstallPrefix)
                && int.TryParse(selected[OptInstallPrefix.Length..], out int index)
                && index >= 0 && index < installs.Count)
            {
                return installs[index];
            }

            throw new InvalidOperationException(
                "More than one Sims 3 installation was found. Choose the installation you actually play.");
        }
    }
}
