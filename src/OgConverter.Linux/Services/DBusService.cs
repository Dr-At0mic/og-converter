using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OgConverter.Models;
using Tmds.DBus.Protocol;

namespace OgConverter.Services
{
    /// <summary>
    /// The app on the session bus. Owning <see cref="BusName"/> makes this the single running instance, receives the
    /// GNOME Shell extension's drag-and-tap gestures (<see cref="TriggerInterface"/>), and lets later launches hand over
    /// to it (<see cref="AppInterface"/>: <c>og-converter --wheel &lt;file&gt;</c> from file-manager menus or a shortcut,
    /// and a plain second launch). It also calls the extension's helpers (<c>MoveWindowByTitle</c>, <c>GetPointer</c>).
    /// </summary>
    /// <remarks>
    /// Events are raised on the D-Bus connection's thread; <see cref="WheelController"/> moves them to the UI thread.
    /// The contract with the extension is documented in linux/gnome-extension/README.md.
    /// </remarks>
    public sealed class DBusService : IDisposable
    {
        public const string BusName = "io.github.dr_at0mic.OgConverter";
        public const string ObjectPath = "/io/github/dr_at0mic/OgConverter";
        public const string TriggerInterface = "io.github.dr_at0mic.OgConverter.Trigger";
        public const string AppInterface = "io.github.dr_at0mic.OgConverter.App";

        private const string ShellBusName = "org.gnome.Shell";
        private const string ShellObjectPath = "/io/github/dr_at0mic/OgConverter/Shell";
        private const string ShellInterface = "io.github.dr_at0mic.OgConverter.Shell";

        private static readonly ReadOnlyMemory<byte> IntrospectionXmlBytes = Encoding.UTF8.GetBytes("""
            <interface name="io.github.dr_at0mic.OgConverter.Trigger">
              <method name="KeyTapped"><arg type="s" name="key" direction="in"/><arg type="i" name="x" direction="in"/><arg type="i" name="y" direction="in"/></method>
              <method name="KeyReleased"><arg type="s" name="key" direction="in"/></method>
              <method name="ButtonReleased"><arg type="i" name="x" direction="in"/><arg type="i" name="y" direction="in"/></method>
            </interface>
            <interface name="io.github.dr_at0mic.OgConverter.App">
              <method name="ShowWheel"><arg type="s" name="path" direction="in"/><arg type="s" name="mode" direction="in"/></method>
              <method name="Activate"/>
            </interface>

            """);

        private readonly DBusConnection _connection;

        private DBusService(DBusConnection connection)
        {
            _connection = connection;
        }

        /// <summary>This process's bus presence when it owns <see cref="BusName"/>; null without a session bus.</summary>
        public static DBusService? Primary { get; private set; }

        /// <summary>
        /// The app's one session-bus connection, for other services (notifications, the FileManager1 portal, settings)
        /// to share instead of opening their own. Null when there is no session bus.
        /// </summary>
        public static DBusConnection? Connection => Primary?._connection;

        /// <summary>The wheel asked for on this process's command line (<c>--wheel &lt;file&gt; [--mode m]</c>).</summary>
        public static (string Path, WheelMode Mode)? StartupWheel { get; private set; }

        /// <summary>The extension saw a modifier tapped during a drag: key ("Shift", "Control", "Alt", "Super") and stage position.</summary>
        public event Action<string, int, int>? KeyTapped;

        /// <summary>A tapped key was let go while the button is still held.</summary>
        public event Action<string>? KeyReleased;

        /// <summary>The mouse button went up (only after a tap): the drop point, in stage coordinates.</summary>
        public event Action<int, int>? ButtonReleased;

        /// <summary>Another launch asked for the wheel on a file.</summary>
        public event Action<string, WheelMode>? ShowWheelRequested;

        /// <summary>Another launch asked to bring the app forward.</summary>
        public event Action? ActivateRequested;

        // ------------------------------------------------------------------ startup

        /// <summary>
        /// Called by Program before the UI starts. Takes the bus name, or hands the request on this command line to the
        /// instance that has it: true means it was handed over and this process should exit. Without a session bus the
        /// app just runs on its own.
        /// </summary>
        public static bool ClaimOrForward(string[] args)
        {
            (string Path, WheelMode Mode)? wheel = ParseWheelArgs(args);
            StartupWheel = wheel;

            string? address = DBusAddress.Session;
            if (string.IsNullOrEmpty(address)) return false;

            DBusConnection? connection = null;
            try
            {
                connection = new DBusConnection(address);
                var conn = connection;
                Task.Run(async () => await conn.ConnectAsync()).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                var service = new DBusService(connection);
                connection.AddMethodHandler(new Handler(service));
                bool owned = Task.Run(async () => await conn.TryRequestNameAsync(BusName, RequestNameOptions.None))
                    .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                if (owned)
                {
                    Primary = service;
                    return false;
                }

                // Someone else is the app: pass this launch on to it
                Task.Run(async () =>
                {
                    if (wheel is { } w) await CallAppAsync(conn, "ShowWheel", w.Path, ModeName(w.Mode));
                    else await CallAppAsync(conn, "Activate", null, null);
                }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                connection.Dispose();
                return true;
            }
            catch (Exception ex) when (ex is DBusExceptionBase or IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException)
            {
                Console.Error.WriteLine($"[OG Converter] Session bus unavailable, running without single-instance and wheel triggers: {ex.Message}");
                connection?.Dispose();
                Primary = null;
                return false;
            }
        }

        /// <summary>"--wheel &lt;path&gt; [--mode convert|tools|actions]" → an absolute path and a wheel; null when absent.</summary>
        private static (string, WheelMode)? ParseWheelArgs(string[] args)
        {
            int i = Array.IndexOf(args, "--wheel");
            if (i < 0 || i + 1 >= args.Length) return null;
            string path = DroppedPaths.ToLocalPath(args[i + 1]) ?? Path.GetFullPath(args[i + 1]);
            int m = Array.IndexOf(args, "--mode");
            WheelMode mode = m >= 0 && m + 1 < args.Length ? ParseMode(args[m + 1]) : WheelMode.Convert;
            return (path, mode);
        }

        public static WheelMode ParseMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
        {
            "tools" or "tool" => WheelMode.Tools,
            "actions" or "action" => WheelMode.Actions,
            _ => WheelMode.Convert
        };

        public static string ModeName(WheelMode mode) => mode.ToString().ToLowerInvariant();

        private static async Task CallAppAsync(DBusConnection connection, string member, string? path, string? mode)
        {
            MessageBuffer message;
            {
                var writer = connection.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: AppInterface,
                    member: member, signature: path != null ? "ss" : null, flags: MessageFlags.None);
                if (path != null)
                {
                    writer.WriteString(path);
                    writer.WriteString(mode ?? "convert");
                }
                message = writer.CreateMessage();
            }
            await connection.CallMethodAsync(message);
        }

        // ------------------------------------------------------------------ the extension's helpers

        /// <summary>
        /// Moves the first window titled <paramref name="title"/> so its frame's top-left is at (x, y) in stage coordinates.
        /// False when the extension isn't there or no such window is mapped yet.
        /// </summary>
        public async Task<bool> MoveWindowByTitleAsync(string title, int x, int y)
        {
            try
            {
                MessageBuffer message;
                {
                    var writer = _connection.GetMessageWriter();
                    writer.WriteMethodCallHeader(destination: ShellBusName, path: ShellObjectPath, @interface: ShellInterface,
                        member: "MoveWindowByTitle", signature: "sii", flags: MessageFlags.NoAutoStart);
                    writer.WriteString(title);
                    writer.WriteInt32(x);
                    writer.WriteInt32(y);
                    message = writer.CreateMessage();
                }
                return await _connection.CallMethodAsync(message, static (Message m, object? _) => m.GetBodyReader().ReadBool(), null)
                    .WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>The pointer in stage coordinates from the extension, or null without it.</summary>
        public async Task<(int X, int Y)?> GetPointerAsync()
        {
            try
            {
                MessageBuffer message;
                {
                    var writer = _connection.GetMessageWriter();
                    writer.WriteMethodCallHeader(destination: ShellBusName, path: ShellObjectPath, @interface: ShellInterface,
                        member: "GetPointer", signature: null, flags: MessageFlags.NoAutoStart);
                    message = writer.CreateMessage();
                }
                return await _connection.CallMethodAsync<(int, int)?>(message, static (Message m, object? _) =>
                {
                    var reader = m.GetBodyReader();
                    int x = reader.ReadInt32();
                    int y = reader.ReadInt32();
                    return (x, y);
                }, null).WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException or OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (ReferenceEquals(Primary, this)) Primary = null;
            _connection.Dispose();
        }

        // ------------------------------------------------------------------ incoming calls

        private sealed class Handler : IPathMethodHandler
        {
            private readonly DBusService _service;
            public Handler(DBusService service) { _service = service; }

            public string Path => ObjectPath;
            public bool HandlesChildPaths => false;

            public ValueTask HandleMethodAsync(MethodContext context)
            {
                var request = context.Request;
                if (context.IsDBusIntrospectRequest)
                {
                    context.ReplyIntrospectXml(new[] { IntrospectionXmlBytes, IntrospectionXml.DBusIntrospectable, IntrospectionXml.DBusPeer }, Array.Empty<string>());
                    return ValueTask.CompletedTask;
                }

                string iface = request.InterfaceAsString ?? string.Empty;
                string member = request.MemberAsString ?? string.Empty;
                string signature = request.SignatureAsString ?? string.Empty;
                try
                {
                    switch (iface, member, signature)
                    {
                        case (TriggerInterface, "KeyTapped", "sii"):
                        {
                            var r = request.GetBodyReader();
                            string key = r.ReadString();
                            int x = r.ReadInt32();
                            int y = r.ReadInt32();
                            ReplyEmpty(context);
                            _service.KeyTapped?.Invoke(key, x, y);
                            break;
                        }
                        case (TriggerInterface, "KeyReleased", "s"):
                        {
                            string key = request.GetBodyReader().ReadString();
                            ReplyEmpty(context);
                            _service.KeyReleased?.Invoke(key);
                            break;
                        }
                        case (TriggerInterface, "ButtonReleased", "ii"):
                        {
                            var r = request.GetBodyReader();
                            int x = r.ReadInt32();
                            int y = r.ReadInt32();
                            ReplyEmpty(context);
                            _service.ButtonReleased?.Invoke(x, y);
                            break;
                        }
                        case (AppInterface, "ShowWheel", "ss"):
                        {
                            var r = request.GetBodyReader();
                            string path = r.ReadString();
                            string mode = r.ReadString();
                            ReplyEmpty(context);
                            _service.ShowWheelRequested?.Invoke(path, ParseMode(mode));
                            break;
                        }
                        case (AppInterface, "Activate", ""):
                            ReplyEmpty(context);
                            _service.ActivateRequested?.Invoke();
                            break;
                        default:
                            context.ReplyUnknownMethodError();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // A bad call must not take the connection (and with it the bus name) down
                    Console.Error.WriteLine($"[OG Converter] D-Bus {iface}.{member} failed: {ex.Message}");
                    if (!context.ReplySent && !context.NoReplyExpected) context.ReplyError("org.freedesktop.DBus.Error.Failed", ex.Message);
                }
                return ValueTask.CompletedTask;
            }

            private static void ReplyEmpty(MethodContext context)
            {
                if (context.NoReplyExpected) return;
                using var writer = context.CreateReplyWriter(null);
                context.Reply(writer.CreateMessage());
            }
        }
    }
}
