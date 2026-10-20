using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OgConverter.Services;

namespace OgConverter
{
    public partial class App : Application
    {
        /// <summary>Opens the wheel from the GNOME extension, <c>--wheel</c> and other launches. Null until startup completes.</summary>
        public static WheelController? Wheel { get; private set; }

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // TODO(merge): the dashboard replaces this placeholder window and handles Wheel.FileActionRequested
                var window = new Window { Title = "OG Converter", Width = 900, Height = 640 };
                desktop.MainWindow = window;

                Wheel = new WheelController(new SettingsService());
                Wheel.ActivateRequested += () =>
                {
                    window.Show();
                    window.Activate();
                };
                Wheel.Start();
                desktop.Exit += (_, _) => Wheel?.Dispose();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
