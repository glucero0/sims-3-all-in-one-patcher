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

            // DXVK is offered via the always-visible checkbox in the main window, not as a conflict card.
            _ = hardware;
            return conflicts;
        }

        /// <summary>
        /// NVIDIA's modern DX9 path is a common source of Serious Errors when loading worlds/saves.
        /// </summary>
        public static bool PrefersDxvk(HardwareInfo hardware)
        {
            return string.Equals(hardware.GpuVendor, "NVIDIA", StringComparison.OrdinalIgnoreCase);
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
