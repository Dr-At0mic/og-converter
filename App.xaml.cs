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
                try
                {
                    Task.Run(async () => await Tests.VerificationRunner.RunSelfTestAsync()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SELF-TEST FAILED: {ex}");
                    exitCode = 1;
                }
                Shutdown(exitCode);
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

            base.OnStartup(e);
        }
    }
}
