using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace OgConverter
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[OG Converter Fatal Error]: {args.ExceptionObject}");
                Console.ResetColor();
            };

            DispatcherUnhandledException += (s, args) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[OG Converter UI Exception]: {args.Exception}");
                Console.ResetColor();
                MessageBox.Show($"Startup Error:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "OG Converter Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            if (e.Args.Length > 0 && e.Args[0] == "--test")
            {
                // Headless: report and exit with a code instead of showing an error dialog nobody can close
                int exitCode = 0;
                string templateTestDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "og-template-test-" + Guid.NewGuid().ToString("N")[..8]);
                Tests.TemplateVerification.IsolateTemplatesFolder(templateTestDir);
                try
                {
                    // "--test markdown,everyday" runs just those sections
                    string[]? only = e.Args.Length > 1 ? e.Args[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : null;
                    Task.Run(async () => await Tests.VerificationRunner.RunSelfTestAsync(only)).GetAwaiter().GetResult();
                    // Renders WPF visuals, so it runs here on the UI (STA) thread
                    if (only == null) Tests.TemplateVerification.Run(templateTestDir);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SELF-TEST FAILED: {ex}");
                    exitCode = 1;
                }
                Shutdown(exitCode);
                return;
            }

            if (e.Args.Length > 0 && e.Args[0] == "--convert")
            {
                // On the UI thread, like a job started from the wheel
                Dispatcher.InvokeAsync(async () => Shutdown(await CommandLine.ConvertAsync(e.Args)));
                return;
            }

            if (e.Args.Length > 1 && e.Args[0] == "--snapshot")
            {
                Tests.SnapshotRunner.Run(e.Args[1]);
                Shutdown();
                return;
            }

            // Replace a previous background instance of this same exe so the new one launches cleanly.
            // Other copies (a different build folder, a running self-test) are left alone.
            var currentProcess = Process.GetCurrentProcess();
            string? currentPath = Environment.ProcessPath;
            foreach (var p in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                try
                {
                    if (p.Id == currentProcess.Id) continue;
                    if (!string.Equals(p.MainModule?.FileName, currentPath, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill();
                    p.WaitForExit(1000);
                }
                catch { }
            }

            if (Array.IndexOf(e.Args, Services.InstallService.UninstallArg) >= 0)
            {
                RunUninstall(quiet: Array.IndexOf(e.Args, "--quiet") >= 0);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            var main = new MainWindow();
            MainWindow = main;
            if (Array.IndexOf(e.Args, Services.InstallService.BackgroundArg) >= 0)
            {
                // Started with Windows: stay in the tray. Hooks and the tray icon need the window handle, not a visible window.
                main.StartHidden();
            }
            else
            {
                main.Show();
            }
        }

        private static void RunUninstall(bool quiet)
        {
            if (!quiet)
            {
                var answer = MessageBox.Show(
                    "Remove OG Converter from this PC?\n\nYour settings and wheel templates in %AppData%\\OGConverter are kept.",
                    "Uninstall OG Converter", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK) return;
            }

            try
            {
                Services.InstallService.Uninstall();
                if (!quiet) MessageBox.Show("OG Converter has been removed.", "Uninstall OG Converter", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show($"Couldn't finish removing OG Converter:\n{ex.Message}", "Uninstall OG Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
