using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Sims3ModernPatcher
{
    public partial class MainWindow : Window
    {
        private readonly PatcherEngine _engine = new();
        private HardwareInfo _hardware = new();
        private List<GameInstall> _installs = new();
        private List<ConflictChoice> _conflicts = new();
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => Rescan();
        }

        private void BtnRescan_Click(object sender, RoutedEventArgs e) => Rescan();

        private void BtnOpenSaveBackups_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string backupRoot = SaveBackupManager.GetBackupRoot();
                System.IO.Directory.CreateDirectory(backupRoot);
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add(backupRoot);
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Windows could not open the save-backup folder.\n\n" + ex.Message,
                    "Could not open backups",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select your The Sims 3 installation folder"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            string path = GameLocator.NormalizeInstallRoot(dialog.FolderName);
            if (!GameLocator.IsValidSims3Install(path))
            {
                MessageBox.Show(
                    this,
                    "That folder does not look like a Sims 3 install.\n\nChoose the folder that contains Game\\Bin\\TS3W.exe (or TS3.exe).",
                    "Install not recognized",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var manual = new GameInstall
            {
                Path = path,
                Platform = GameLocator.InferPlatform(path)
            };

            _installs = new List<GameInstall> { manual };
            RebuildConflicts();
            RefreshInstallLabel();
            TxtStatus.Text = string.Empty;
            Log($"[+] Using manually selected install ({manual.PlatformLabel}): {manual.Path}");
        }

        private async void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            if (_installs.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No Sims 3 installation was found.\n\nClick “Browse for Sims 3 folder…” and select the game folder, then press GO.",
                    "Sims 3 not found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var choices = CollectChoices();
            GameInstall install;
            try
            {
                install = PatchCatalog.ResolveInstall(_installs, choices);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Choose an installation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _busy = true;
            SetUiEnabled(false);
            TxtLogOutput.Clear();
            Log("[*] Starting modern compatibility patching...");

            try
            {
                var plan = new PatchPlan
                {
                    Install = install,
                    Hardware = _hardware,
                    Choices = choices,
                    CreateDesktopShortcut = ChkCreateShortcut.IsChecked == true
                };

                PatchResult patchResult = await Task.Run(() => _engine.ApplyAsync(plan, Log));

                Log("[SUCCESS] Done.");

                var result = MessageBox.Show(
                    this,
                    "All selected Sims 3 modern patches were applied successfully.\n\n" +
                    (patchResult.DesktopShortcutCreated
                        ? "A desktop shortcut named “TS3-Windows 11” was created.\n\n"
                        : ChkCreateShortcut.IsChecked == true
                            ? "The patches succeeded, but Windows could not create the desktop shortcut. See the progress log.\n\n"
                        : string.Empty) +
                    (patchResult.SaveBackupPath is not null
                        ? "Your existing saves were backed up and left unchanged.\n" +
                          patchResult.SaveBackupPath + "\n\n"
                        : string.Empty) +
                    "Please restart your PC so Windows fully releases any locked game files.\n\n" +
                    "Restart now?",
                    "Success — restart recommended",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    TryRestartPc();
            }
            catch (Exception ex)
            {
                Log("[X] " + ex.Message);
                MessageBox.Show(
                    this,
                    "Patching did not finish successfully.\n\n" + ex.Message +
                    "\n\nIf the game folder is protected, right-click this app and choose “Run as administrator”, then try again.",
                    "Patching failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
                SetUiEnabled(true);
            }
        }

        private void Rescan()
        {
            try
            {
                _hardware = _engine.DetectHardware();
                TxtCpuInfo.Text = _hardware.CpuDisplay;
                TxtGpuInfo.Text = _hardware.GpuDisplay;
                TxtOsInfo.Text = _hardware.OsName;

                _installs = _engine.FindInstallations();
                RebuildConflicts();
                RefreshInstallLabel();

                Log("[*] Environment scan complete.");
                Log($"[+] CPU: {_hardware.CpuDisplay}");
                Log($"[+] GPU: {_hardware.GpuDisplay}");
                Log($"[+] OS: {_hardware.OsName}");

                if (_installs.Count == 0)
                {
                    TxtStatus.Text = "Sims 3 not found automatically — use Browse.";
                    Log("[!] No Sims 3 install detected. Use Browse to select it.");
                }
                else
                {
                    TxtStatus.Text = string.Empty;
                    foreach (var install in _installs)
                        Log($"[+] Found {install.PlatformLabel} install: {install.Path}");
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Detection error — see progress log.";
                Log("[X] Detection failed: " + ex.Message);
            }
        }

        private void RebuildConflicts()
        {
            _conflicts = _installs.Count == 0
                ? new List<ConflictChoice>()
                : _engine.BuildConflicts(_installs, _hardware);

            ConflictPanel.Children.Clear();

            if (_conflicts.Count == 0)
            {
                ConflictPanel.Children.Add(new TextBlock
                {
                    Text = _installs.Count == 0
                        ? "No choices yet. Find or browse to a Sims 3 install first."
                        : "No conflicting choices needed — press GO.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13
                });
                return;
            }

            foreach (var conflict in _conflicts)
                ConflictPanel.Children.Add(BuildConflictCard(conflict));
        }

        internal static UIElement BuildConflictCard(ConflictChoice conflict)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = conflict.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Text = conflict.Explanation,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var group = new StackPanel();
            foreach (var option in conflict.Options)
            {
                var radio = new RadioButton
                {
                    GroupName = conflict.Id,
                    IsChecked = option.Id == conflict.SelectedOptionId,
                    Margin = new Thickness(0, 0, 0, 10),
                    Tag = option.Id,
                    Foreground = Brushes.White
                };

                var content = new StackPanel();
                content.Children.Add(new TextBlock
                {
                    Text = option.IsRecommended ? option.Label + "  ★ recommended" : option.Label,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                content.Children.Add(new TextBlock
                {
                    Text = option.Description,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                radio.Content = content;
                radio.Checked += (_, _) => conflict.SelectedOptionId = option.Id;
                group.Children.Add(radio);
            }

            stack.Children.Add(group);
            card.Child = stack;
            return card;
        }

        private Dictionary<string, string> CollectChoices()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var conflict in _conflicts)
            {
                if (!string.IsNullOrWhiteSpace(conflict.SelectedOptionId))
                    map[conflict.Id] = conflict.SelectedOptionId;
            }
            return map;
        }

        private void RefreshInstallLabel()
        {
            if (_installs.Count == 0)
            {
                TxtInstallInfo.Text = "Not found";
                return;
            }

            if (_installs.Count == 1)
            {
                var one = _installs[0];
                TxtInstallInfo.Text = $"{one.PlatformLabel}\n{one.Path}";
                return;
            }

            TxtInstallInfo.Text = $"{_installs.Count} installs found — choose one below.\n" +
                                  string.Join("\n", _installs.Select(i => "• " + i.PlatformLabel + ": " + i.Path));
        }

        private void SetUiEnabled(bool enabled)
        {
            BtnGo.IsEnabled = enabled;
            BtnRescan.IsEnabled = enabled;
            BtnBrowse.IsEnabled = enabled;
            BtnOpenSaveBackups.IsEnabled = enabled;
            ChkCreateShortcut.IsEnabled = enabled;
            ConflictPanel.IsEnabled = enabled;
        }

        private void Log(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }

            TxtLogOutput.AppendText(message + Environment.NewLine);
            TxtLogOutput.ScrollToEnd();
        }

        private static void TryRestartPc()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 60 /c \"Sims 3 Modernizer requested a restart so patched files unlock cleanly.\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });

                MessageBox.Show(
                    "Your PC will restart in 60 seconds.\n\nTo cancel, open Command Prompt and run:\nshutdown /a",
                    "Restart scheduled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not start an automatic restart.\nPlease restart Windows manually.\n\n" + ex.Message,
                    "Restart manually",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
