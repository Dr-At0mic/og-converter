using System;
using System.Threading.Tasks;
using Avalonia;
using OgConverter.Platform.Linux;

namespace OgConverter
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Before anything else: the services, the UI and the command-line modes below all use the Linux implementations
            LinuxPlatform.Register();
            Wheel.LinuxWheel.Register();

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[OG Converter Fatal Error]: {e.ExceptionObject}");
                Console.ResetColor();
            };

            if (args.Length > 0 && args[0] == "--test")
            {
                // Headless: report and exit with a code
                try
                {
                    // "--test markdown,everyday" runs just those sections
                    string[]? only = args.Length > 1 ? args[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : null;
                    Task.Run(() => Tests.VerificationRunner.RunSelfTestAsync(only, LinuxPlatform.SelfTests)).GetAwaiter().GetResult();
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SELF-TEST FAILED: {ex}");
                    return 1;
                }
            }

            if (args.Length > 0 && args[0] == "--convert")
            {
                return Task.Run(() => CommandLine.ConvertAsync(args)).GetAwaiter().GetResult();
            }

            if (args.Length > 0 && args[0] == "--snapshot-wheel")
            {
                return Wheel.WheelSnapshot.Run(args.Length > 1 ? args[1] : "wheel-snapshots");
            }

            if (args.Length > 1 && args[0] == "--snapshot")
            {
                return Tests.SnapshotRunner.Run(args[1]);
            }

            // One app per session: a second launch hands "--wheel <file>" or "come forward" to the running one and exits
            if (Services.DBusService.ClaimOrForward(args)) return 0;

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        /// <summary>Also used by the Avalonia designer.</summary>
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
