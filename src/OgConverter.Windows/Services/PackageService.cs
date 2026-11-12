using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace OgConverter.Services
{
    /// <summary>
    /// The Microsoft Store (MSIX) build. When the app runs with package identity, Windows owns installing,
    /// updating and uninstalling it, so the self-installer and GitHub self-update stay out of the way, and
    /// "start with Windows" goes through the package's startup task instead of the registry Run key.
    /// </summary>
    public static class PackageService
    {
        public const string StoreProductId = "9PFHZ91JDJF6";
        /// <summary>Must match the TaskId of the windows.startupTask extension in packaging/msix/AppxManifest.xml.</summary>
        public const string StartupTaskId = "OGConverterStartup";
        public static string StorePageUri => $"ms-windows-store://pdp/?productid={StoreProductId}";

        private const int AppModelErrorNoPackage = 15700;

        /// <summary>True when this process runs from the installed MSIX package.</summary>
        public static bool IsPackaged { get; } = DetectPackage();

        /// <summary>True when Windows started the app at sign-in through the startup task (the packaged --background).</summary>
        public static bool LaunchedByStartupTask
        {
            get
            {
                if (!IsPackaged) return false;
                try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Whether the startup task is on. Off by policy or by the user in Task Manager also reads as off.</summary>
        public static async Task<bool> IsStartupEnabledAsync()
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        /// <summary>
        /// Turns the startup task on or off and returns its new state. Windows won't let an app re-enable a task the
        /// user turned off in Task Manager › Startup apps (<see cref="StartupTaskState.DisabledByUser"/>).
        /// </summary>
        public static async Task<StartupTaskState> SetStartupAsync(bool enabled)
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (enabled) return await task.RequestEnableAsync();
            task.Disable();
            return task.State;
        }

        private static bool DetectPackage()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(8)) return false;
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
    }
}
