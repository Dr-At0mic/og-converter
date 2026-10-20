using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OgConverter.Services
{
    /// <summary>A published GitHub release: version, its "What's new" notes and the Windows exe to download.</summary>
    public sealed record ReleaseInfo(string Version, string Tag, string Notes, string PageUrl, string? ExeUrl, string? ShaUrl, long ExeSize);

    /// <summary>
    /// In-app updates from GitHub Releases. Only contacts GitHub when the user asks (Settings › System › Check for updates).
    /// The running exe can't be overwritten but can be renamed, so an update downloads the new exe beside it, verifies its
    /// SHA-256, swaps the files, and restarts. The old copy is deleted on the next start.
    /// </summary>
    public static class UpdateService
    {
        public const string RepoUrl = "https://github.com/Dr-At0mic/og-converter";
        public const string ReleasesUrl = RepoUrl + "/releases";
        private const string LatestApi = "https://api.github.com/repos/Dr-At0mic/og-converter/releases/latest";
        private const string NewSuffix = ".new";
        private const string OldSuffix = ".old";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // downloads are ~170 MB; requests use their own tokens
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"OG-Converter/{Platform.AppVersion}"); // GitHub rejects requests without one
            return client;
        }

        /// <summary>Release builds are one self-contained exe that can replace itself; dev builds only link to the release page.</summary>
        public static bool CanSelfUpdate => Platform.IsSingleFileBuild && CurrentExePath != null;

        /// <summary>The file to replace: the exe, or on Linux the AppImage the app runs from (its runtime sets APPIMAGE).</summary>
        private static string? CurrentExePath =>
            !OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage ? appImage : Environment.ProcessPath;

        public static string ReleasePageFor(string version) => $"{ReleasesUrl}/tag/v{version}";

        public static async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("No releases have been published yet.");
            if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429) throw new InvalidOperationException("GitHub is limiting requests right now. Try again in a few minutes.");
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidOperationException("The release has no tag.");
            string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;
            string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesUrl : ReleasesUrl;

            string? exeUrl = null, shaUrl = null;
            long exeSize = 0;
            // This platform's download ("-win-x64.exe"); none means there's nothing to update from
            if (Platform.UpdateAssetSuffix is { } exeSuffix && root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? string.Empty;
                    string url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                    if (name.EndsWith(exeSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        exeUrl = url;
                        exeSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    }
                    else if (name.EndsWith(exeSuffix + ".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        shaUrl = url;
                    }
                }
            }

            return new ReleaseInfo(tag.TrimStart('v', 'V'), tag, ExtractWhatsNew(body), page, exeUrl, shaUrl, exeSize);
        }

        /// <summary>True when <paramref name="latest"/> is a higher x.y.z than <paramref name="current"/>.</summary>
        public static bool IsNewer(string latest, string current) =>
            Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) && l > c;

        /// <summary>
        /// Downloads the release exe next to the running one and checks it against the published SHA-256.
        /// Returns the path of the verified file.
        /// </summary>
        public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
        {
            if (release.ExeUrl == null || release.ShaUrl == null) throw new InvalidOperationException(OperatingSystem.IsWindows()
                ? "This release has no Windows download. Get it from the release page."
                : "This release has no download for this system. Get it from the release page.");
            string exe = CurrentExePath ?? throw new InvalidOperationException("Can't find this program's file.");
            string target = exe + NewSuffix;

            string expected = (await Http.GetStringAsync(release.ShaUrl, ct)).Trim().Split(' ', '\t')[0].ToLowerInvariant();
            if (!Regex.IsMatch(expected, "^[0-9a-f]{64}$")) throw new InvalidOperationException("The release's checksum file is unreadable.");

            try
            {
                using (var response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? release.ExeSize;
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                    var buffer = new byte[1 << 16];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        if (total > 0) progress.Report((double)done / total);
                    }
                }

                string actual;
                await using (var check = File.OpenRead(target))
                {
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
                }
                if (actual != expected) throw new InvalidOperationException("The download doesn't match the published checksum, so it wasn't installed. Try again.");
                // Downloads aren't executable on Linux until marked so
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute);
                return target;
            }
            catch
            {
                TryDelete(target);
                throw;
            }
        }

        /// <summary>Puts the downloaded exe in place of the running one and starts it. The caller then exits.</summary>
        public static void ApplyAndRestart(string downloaded)
        {
            string exe = CurrentExePath ?? throw new InvalidOperationException("Can't find this program's file.");
            string old = exe + OldSuffix;
            TryDelete(old);
            if (File.Exists(old)) old = $"{exe}.{DateTime.Now:yyyyMMddHHmmss}{OldSuffix}"; // still locked by a previous copy

            File.Move(exe, old); // allowed while running
            try
            {
                File.Move(downloaded, exe);
            }
            catch
            {
                File.Move(old, exe);
                throw;
            }

            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        /// <summary>At startup: removes the copy left behind by the last update and a half-finished download.</summary>
        public static void CleanupAfterUpdate()
        {
            string? exe = CurrentExePath;
            string? dir = Path.GetDirectoryName(exe);
            if (exe == null || dir == null) return;

            TryDelete(exe + NewSuffix);
            try
            {
                foreach (string old in Directory.EnumerateFiles(dir, Path.GetFileName(exe) + "*" + OldSuffix)) TryDelete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Keeps the "What's new" section of the release notes (written from CHANGELOG.md by the release workflow) as
        /// plain text. Falls back to the whole body when there's no such section.
        /// </summary>
        internal static string ExtractWhatsNew(string body)
        {
            string text = body.Replace("\r\n", "\n");
            var match = Regex.Match(text, @"^##+\s*What'?s new[^\n]*\n(?<notes>.*?)(?=^##\s|\z)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success) text = match.Groups["notes"].Value;

            var lines = text.Split('\n')
                .Select(line => line.TrimEnd())
                .Select(line => Regex.Replace(line, @"^(\s*)[-*]\s+", "$1• "))
                .Select(line => Regex.Replace(line, @"^#+\s*", string.Empty))
                .Select(line => Regex.Replace(line, @"\*\*(.+?)\*\*|`(.+?)`", m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
                .Select(line => Regex.Replace(line, @"\[(.+?)\]\((.+?)\)", "$1"));
            string result = string.Join("\n", lines).Trim();
            return result.Length > 0 ? result : "No notes were published for this release.";
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
