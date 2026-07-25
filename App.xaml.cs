using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Sims3ModernPatcher
{
    public partial class App : Application
    {
        private async void App_Startup(object sender, StartupEventArgs e)
        {
            if (e.Args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int code = await HeadlessApplyAsync(e.Args);
                Shutdown(code);
                return;
            }

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }

        private static async Task<int> HeadlessApplyAsync(string[] args)
        {
            var engine = new PatcherEngine();
            PatcherSessionLog? session = null;
            try
            {
                session = PatcherSessionLog.StartNew("headless-apply");
                void CombinedLog(string line)
                {
                    Console.WriteLine(line);
                    session.WriteLine(line);
                }

                CombinedLog("[*] Headless --apply starting...");
                var hardware = engine.DetectHardware();
                var installs = engine.FindInstallations();
                if (installs.Count == 0)
                {
                    CombinedLog("[X] No Sims 3 installation was found.");
                    return 2;
                }

                if (installs.Count > 1)
                {
                    CombinedLog("[X] Multiple Sims 3 installs found. Use the UI to choose one, or leave only one install.");
                    foreach (var install in installs)
                        CombinedLog($"    - {install.PlatformLabel}: {install.Path}");
                    return 3;
                }

                bool useDxvk = !args.Any(a => a.Equals("--native-dx", StringComparison.OrdinalIgnoreCase));
                if (useDxvk && PatchCatalog.PrefersDxvk(hardware))
                    CombinedLog("[*] Using DXVK (recommended for this NVIDIA GPU). Pass --native-dx to force native DirectX.");
                else if (useDxvk)
                    CombinedLog("[*] Using DXVK (--apply default). Pass --native-dx to force native DirectX.");
                else
                    CombinedLog("[*] Using native DirectX 9 (--native-dx).");

                bool createShortcut = !args.Any(a => a.Equals("--no-shortcut", StringComparison.OrdinalIgnoreCase));
                var plan = new PatchPlan
                {
                    Install = installs[0],
                    Hardware = hardware,
                    Choices =
                    {
                        [PatchCatalog.ChoiceGraphicsApi] = useDxvk
                            ? PatchCatalog.OptDxvk
                            : PatchCatalog.OptDxNative
                    },
                    CreateDesktopShortcut = createShortcut
                };

                await engine.ApplyAsync(plan, CombinedLog);
                CombinedLog("[SUCCESS] Headless apply finished.");
                return 0;
            }
            catch (Exception ex)
            {
                string msg = "[X] Headless apply failed: " + ex.Message;
                Console.Error.WriteLine(msg);
                try { session?.WriteLine(msg); }
                catch { /* ignore */ }
                return 1;
            }
            finally
            {
                session?.Dispose();
            }
        }
    }
}
