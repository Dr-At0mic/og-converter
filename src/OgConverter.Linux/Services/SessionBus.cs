using System;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace OgConverter.Services
{
    /// <summary>Small helpers for the session D-Bus (shared connection; every call fails soft when there's no bus).</summary>
    public static class SessionBus
    {
        /// <summary>
        /// The shared session connection: the one <see cref="DBusService"/> owns the app's bus name on, or the library's
        /// default session connection when this process has none (snapshot mode, a second launch). Null when there's no
        /// session bus (a bare SSH login, a container).
        /// </summary>
        public static DBusConnection? Connection
        {
            get
            {
                if (DBusService.Connection is { } owned) return owned;
                string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                bool hasBus = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
                              || (!string.IsNullOrEmpty(runtime) && File.Exists(Path.Combine(runtime, "bus")));
                if (!hasBus || !OperatingSystem.IsLinux()) return null;
                try { return DBusConnection.Session; }
                catch (Exception ex) when (ex is DBusConnectionException or InvalidOperationException or NotSupportedException) { return null; }
            }
        }

        /// <summary>True when someone owns <paramref name="name"/> on the session bus (a service is running).</summary>
        public static async Task<bool> NameHasOwnerAsync(string name)
        {
            var connection = Connection;
            if (connection == null) return false;
            try
            {
                MessageBuffer message;
                using (var writer = connection.GetMessageWriter())
                {
                    writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s", MessageFlags.None);
                    writer.WriteString(name);
                    message = writer.CreateMessage();
                }
                return await connection.CallMethodAsync(message, (m, _) => m.GetBodyReader().ReadBool(), null);
            }
            catch (Exception ex) when (ex is DBusExceptionBase or InvalidOperationException or ObjectDisposedException)
            {
                return false;
            }
        }
    }
}
