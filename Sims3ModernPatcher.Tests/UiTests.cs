using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Sims3ModernPatcher;
using Xunit;

namespace Sims3ModernPatcher.Tests;

public sealed class UiTests
{
    [Fact]
    public void MainWindow_LoadsRequiredControlsAndSafeDefaults()
    {
        RunOnStaThread(() =>
        {
            var window = new MainWindow();
            try
            {
                var go = Assert.IsType<Button>(window.FindName("BtnGo"));
                var browse = Assert.IsType<Button>(window.FindName("BtnBrowse"));
                var backups = Assert.IsType<Button>(window.FindName("BtnOpenSaveBackups"));
                var logs = Assert.IsType<Button>(window.FindName("BtnOpenLogs"));
                var shortcut = Assert.IsType<CheckBox>(window.FindName("ChkCreateShortcut"));
                var log = Assert.IsType<TextBox>(window.FindName("TxtLogOutput"));

                Assert.Equal("GO", go.Content);
                Assert.Contains("Browse", browse.Content?.ToString());
                Assert.Equal("Open Save Backups", backups.Content);
                Assert.Equal("Open Logs", logs.Content);
                Assert.True(shortcut.IsChecked);
                Assert.True(log.IsReadOnly);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ConflictCard_RendersPlainLanguageOptionsAndUpdatesSelection()
    {
        RunOnStaThread(() =>
        {
            var conflict = new ConflictChoice
            {
                Id = "graphics",
                Title = "Choose graphics mode",
                Explanation = "Use the safest option unless troubleshooting.",
                SelectedOptionId = "native",
                Options =
                {
                    new ConflictOption
                    {
                        Id = "native",
                        Label = "Built-in DirectX",
                        Description = "Safest default.",
                        IsRecommended = true
                    },
                    new ConflictOption
                    {
                        Id = "dxvk",
                        Label = "DXVK",
                        Description = "Use only when troubleshooting."
                    }
                }
            };

            UIElement card = MainWindow.BuildConflictCard(conflict);
            List<RadioButton> radios = FindLogicalChildren<RadioButton>(card).ToList();

            Assert.Equal(2, radios.Count);
            Assert.True(radios[0].IsChecked);
            Assert.Contains("recommended", FindLogicalChildren<TextBlock>(radios[0]).First().Text);

            radios[1].IsChecked = true;
            Assert.Equal("dxvk", conflict.SelectedOptionId);
        });
    }

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T match)
                yield return match;
            if (child is DependencyObject dependencyObject)
            {
                foreach (T descendant in FindLogicalChildren<T>(dependencyObject))
                    yield return descendant;
            }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
