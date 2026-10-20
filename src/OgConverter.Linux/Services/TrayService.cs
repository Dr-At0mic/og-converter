using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace OgConverter.Services
{
    /// <summary>
    /// The tray icon (StatusNotifierItem through Avalonia's <see cref="TrayIcon"/>): click opens the dashboard; the menu
    /// opens it, previews the wheel or quits. GNOME shows it with the AppIndicator extension, which Ubuntu ships enabled;
    /// <see cref="IsHostAvailableAsync"/> tells the App whether closing the window may leave it running there.
    /// </summary>
    public sealed class TrayService : IDisposable
    {
        private TrayIcon? _icon;

        public event Action? OpenRequested;
        public event Action? PreviewWheelRequested;
        public event Action? QuitRequested;

        /// <summary>Adds the icon to the application. Call once, on the UI thread.</summary>
        public void Initialize(Application app)
        {
            var menu = new NativeMenu();
            menu.Add(Item("Open OG Converter", () => OpenRequested?.Invoke()));
            menu.Add(Item("Preview the wheel", () => PreviewWheelRequested?.Invoke()));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(Item("Quit", () => QuitRequested?.Invoke()));

            _icon = new TrayIcon { ToolTipText = "OG Converter", Menu = menu, IsVisible = true };
            try { _icon.Icon = AppIcon.CreateWindowIcon(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { }
            _icon.Clicked += (_, _) => OpenRequested?.Invoke();

            TrayIcon.SetIcons(app, new TrayIcons { _icon });
        }

        private static NativeMenuItem Item(string header, Action action)
        {
            var item = new NativeMenuItem(header);
            item.Click += (_, _) => action();
            return item;
        }

        /// <summary>
        /// A tray host is listening (org.kde.StatusNotifierWatcher: KDE, Cinnamon, XFCE, and GNOME with the AppIndicator
        /// extension). Without one the icon is invisible, so the window must not hide into it.
        /// </summary>
        public static Task<bool> IsHostAvailableAsync() => SessionBus.NameHasOwnerAsync("org.kde.StatusNotifierWatcher");

        public void Dispose()
        {
            if (_icon == null) return;
            _icon.IsVisible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
