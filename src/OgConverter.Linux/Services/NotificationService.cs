using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OgConverter.Models;
using Tmds.DBus.Protocol;

namespace OgConverter.Services
{
    /// <summary>
    /// Job progress and results as desktop notifications (<c>org.freedesktop.Notifications</c>). Windows shows its own
    /// toast window in the corner; Wayland doesn't let apps place windows, so the desktop's notifications take that role.
    /// One notification per job: the progress one is replaced by the result (<c>replaces_id</c>), with Open and Show in
    /// folder actions.
    /// </summary>
    public sealed class NotificationService
    {
        private const string Service = "org.freedesktop.Notifications";
        private const string ObjectPath = "/org/freedesktop/Notifications";
        public const string DesktopEntryId = "io.github.dr_at0mic.OgConverter";

        /// <summary>Result file of each notification that has actions, by notification id.</summary>
        private readonly ConcurrentDictionary<uint, string> _targets = new();
        private int _subscribed;

        /// <summary>Shows the result. Pass the progress notification of the same job to turn it into the result.</summary>
        public void ShowCompleted(ConversionJob job, ProgressNotification? progress = null)
        {
            string title, body;
            var actions = new List<string>();
            if (job.IsSuccess)
            {
                title = job.Option.Mode switch
                {
                    WheelMode.Convert => $"Converted to {job.Option.Title}",
                    WheelMode.Tools => $"{job.Option.Title} finished",
                    _ => job.Option.Title
                };
                string files = job.Option.Mode == WheelMode.Actions || job.ResultText != null
                    ? Path.GetFileName(job.SourcePath)
                    : $"{Path.GetFileName(job.SourcePath)}  →  {Path.GetFileName(job.TargetPath)}";
                string detail = job.DetailText;
                body = files + "\n" + (string.IsNullOrEmpty(detail) ? job.DurationText : $"{detail}  ·  {job.DurationText}");

                // Nothing new to open when the result went to the clipboard
                bool hasFile = job.ResultText == null && (File.Exists(job.TargetPath) || Directory.Exists(job.TargetPath));
                if (hasFile)
                {
                    actions.AddRange(new[] { "default", "Open", "open", "Open", "reveal", "Show in folder" });
                }
            }
            else
            {
                title = $"{job.Option.Title} failed";
                body = (job.ErrorMessage ?? "Unknown error") + "\n" + Path.GetFileName(job.SourcePath);
            }

            _ = ReplaceAsync(progress, title, body, actions, actions.Count > 0 ? job.TargetPath : null);
        }

        private async Task ReplaceAsync(ProgressNotification? progress, string title, string body, List<string> actions, string? target)
        {
            uint replaces = progress != null ? await progress.FinishAsync() : 0;
            await SendAsync(replaces, title, body, actions, transient: false, progressValue: null, expireMs: -1, target: target);
        }

        /// <summary>A "working" notification for long-running work; update it with <see cref="ProgressNotification.Report"/>.</summary>
        public ProgressNotification ShowProgress(string title, string sourcePath)
        {
            var handle = new ProgressNotification(this, title, Path.GetFileName(sourcePath));
            handle.Start();
            return handle;
        }

        /// <summary>A one-off notice ("still running in the tray").</summary>
        public void ShowInfo(string title, string body) =>
            _ = SendAsync(0, title, body, Array.Empty<string>(), transient: true, progressValue: null, expireMs: -1, target: null);

        internal async Task<uint> SendAsync(uint replacesId, string summary, string body, IReadOnlyList<string> actions, bool transient,
                                            int? progressValue, int expireMs, string? target)
        {
            var connection = SessionBus.Connection;
            if (connection == null) return 0;
            try
            {
                await SubscribeAsync(connection);

                MessageBuffer message;
                using (var w = connection.GetMessageWriter())
                {
                    w.WriteMethodCallHeader(Service, ObjectPath, Service, "Notify", "susssasa{sv}i", MessageFlags.None);
                    w.WriteString("OG Converter");
                    w.WriteUInt32(replacesId);
                    w.WriteString(AppIconPath.Value ?? string.Empty);
                    w.WriteString(summary);
                    w.WriteString(EscapeMarkup(body));
                    w.WriteArray(actions is string[] array ? array : new List<string>(actions).ToArray());

                    var hints = w.WriteDictionaryStart();
                    w.WriteDictionaryEntryStart();
                    w.WriteString("desktop-entry");
                    w.WriteVariantString(DesktopEntryId);
                    if (transient)
                    {
                        w.WriteDictionaryEntryStart();
                        w.WriteString("transient");
                        w.WriteVariantBool(true);
                    }
                    if (progressValue is int value)
                    {
                        // KDE and some others draw a bar from this; GNOME ignores it and shows the body text
                        w.WriteDictionaryEntryStart();
                        w.WriteString("value");
                        w.WriteVariantInt32(Math.Clamp(value, 0, 100));
                    }
                    w.WriteDictionaryEnd(hints);
                    w.WriteInt32(expireMs);
                    message = w.CreateMessage();
                }

                uint id = await connection.CallMethodAsync(message, (m, _) => m.GetBodyReader().ReadUInt32(), null);
                if (target != null) _targets[id] = target;
                else _targets.TryRemove(id, out _);
                return id;
            }
            catch (Exception ex) when (ex is DBusExceptionBase or InvalidOperationException or ObjectDisposedException)
            {
                // No notification daemon: results are still in Recent activity
                return 0;
            }
        }

        /// <summary>Listens for the Open / Show in folder buttons once per connection.</summary>
        private async Task SubscribeAsync(DBusConnection connection)
        {
            if (Interlocked.Exchange(ref _subscribed, 1) == 1) return;
            try
            {
                await connection.WatchSignalAsync(Service, ObjectPath, Service, "ActionInvoked",
                    (m, _) =>
                    {
                        var r = m.GetBodyReader();
                        return (Id: r.ReadUInt32(), Action: r.ReadString());
                    },
                    (Exception? ex, (uint Id, string Action) e) =>
                    {
                        if (ex != null || !_targets.TryGetValue(e.Id, out string? path)) return;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (e.Action == "reveal") DesktopActions.RevealInFileManager(path);
                            else DesktopActions.OpenFile(path);
                        });
                    },
                    null, false, ObserverFlags.None);

                await connection.WatchSignalAsync(Service, ObjectPath, Service, "NotificationClosed",
                    (m, _) => m.GetBodyReader().ReadUInt32(),
                    (Exception? ex, uint id) =>
                    {
                        if (ex == null) _targets.TryRemove(id, out _);
                    },
                    null, false, ObserverFlags.None);
            }
            catch (Exception ex) when (ex is DBusExceptionBase or InvalidOperationException or ObjectDisposedException)
            {
                Interlocked.Exchange(ref _subscribed, 0);
            }
        }

        /// <summary>Notification bodies may be read as simple markup; file names with &amp; or &lt; must not break it.</summary>
        private static string EscapeMarkup(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        /// <summary>The installed icon by name, else a PNG of the bundled icon in the cache folder.</summary>
        private static readonly Lazy<string?> AppIconPath = new(() =>
        {
            if (LinuxInstallService.IsIconInstalled) return DesktopEntryId;
            try { return AppIcon.ExtractPng(256, Path.Combine(LinuxInstallService.CacheDirectory, "og-converter-256.png")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
        });
    }

    /// <summary>The live notification of one running job.</summary>
    public sealed class ProgressNotification
    {
        private readonly NotificationService _owner;
        private readonly string _title;
        private readonly string _file;
        private readonly object _gate = new();
        private Task<uint>? _shown;
        private int _lastPercent = -1;
        private DateTime _lastSent = DateTime.MinValue;
        private bool _finished;

        internal ProgressNotification(NotificationService owner, string title, string file)
        {
            _owner = owner;
            _title = title;
            _file = file;
        }

        internal void Start() => _shown = _owner.SendAsync(0, _title, $"{_file}\nWorking…", Array.Empty<string>(), transient: true, progressValue: null, expireMs: 0, target: null);

        /// <summary>0–1. Updates the notification at most about once a second so the desktop isn't flooded.</summary>
        public void Report(double fraction)
        {
            int percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
            lock (_gate)
            {
                if (_finished || percent == _lastPercent || DateTime.UtcNow - _lastSent < TimeSpan.FromSeconds(1)) return;
                _lastPercent = percent;
                _lastSent = DateTime.UtcNow;
            }
            var previous = _shown;
            _shown = UpdateAsync(previous, percent);
        }

        private async Task<uint> UpdateAsync(Task<uint>? previous, int percent)
        {
            uint id = previous == null ? 0 : await previous;
            if (_finished) return id;
            return await _owner.SendAsync(id, _title, $"{_file}\n{percent}%", Array.Empty<string>(), transient: true, progressValue: percent, expireMs: 0, target: null);
        }

        /// <summary>Stops updates and returns the id to replace (0 when there's no notification daemon).</summary>
        internal async Task<uint> FinishAsync()
        {
            lock (_gate) _finished = true;
            return _shown == null ? 0 : await _shown;
        }
    }
}
