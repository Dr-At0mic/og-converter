using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OgConverter.Models;
using OgConverter.Wheel;
using OgConverter.Wheel.Renderers;

namespace OgConverter.Services
{
    public sealed class TemplateException : Exception
    {
        public TemplateException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Owns the wheel templates: the built-ins, community templates in %AppData%\OGConverter\Templates (one folder
    /// each, with a template.json and optional sounds and plugin .dll files), and the renderers plugins add.
    /// </summary>
    public sealed class TemplateManager
    {
        // Lazy: the constructor scans TemplatesDirectory, which must be initialized first
        private static readonly Lazy<TemplateManager> _instance = new(() => new TemplateManager());
        public static TemplateManager Instance => _instance.Value;

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public const string TemplateFileName = "template.json";
        private const long MaxPluginBytes = 20 * 1024 * 1024;

        /// <summary>%AppData%\OGConverter\Templates, or OGCONVERTER_TEMPLATES_DIR when set (portable setups, testing).</summary>
        public static string TemplatesDirectory { get; } =
            Environment.GetEnvironmentVariable("OGCONVERTER_TEMPLATES_DIR") is { Length: > 0 } custom
                ? Path.GetFullPath(custom)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OGConverter", "Templates");

        private readonly Dictionary<string, IWheelRenderer> _builtInRenderers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IWheelRenderer> _renderers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<WheelTemplate> _builtIns = BuiltInTemplates.Create();
        private readonly List<WheelTemplate> _templates = new();
        private readonly List<string> _loadErrors = new();
        private AssemblyLoadContext? _pluginContext;

        /// <summary>Raised after <see cref="Current"/> changes.</summary>
        public event Action? CurrentChanged;

        /// <summary>Raised after templates are added, removed or reloaded.</summary>
        public event Action? TemplatesChanged;

        public IReadOnlyList<WheelTemplate> Templates => _templates;
        public IReadOnlyList<string> LoadErrors => _loadErrors;
        public WheelTemplate Current { get; private set; }

        private TemplateManager()
        {
            foreach (var r in new IWheelRenderer[] { new RingRenderer(), new GlassRenderer(), new OrbitRenderer(), new NeonRenderer() })
            {
                _builtInRenderers[r.Id] = r;
            }
            foreach (var t in _builtIns) t.Source = TemplateSource.BuiltIn;
            Current = _builtIns[0];
            Reload();
        }

        public IWheelRenderer RendererFor(WheelTemplate template) =>
            _renderers.TryGetValue(template.Renderer ?? string.Empty, out var r) ? r : _builtInRenderers["ring"];

        public bool HasRenderer(string id) => _renderers.ContainsKey(id);

        public WheelTemplate? Find(string? id) =>
            string.IsNullOrEmpty(id) ? null : _templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Makes <paramref name="id"/> the active template. Unknown ids fall back to the default.</summary>
        public void Apply(string? id)
        {
            var template = Find(id) ?? _builtIns[0];
            if (ReferenceEquals(template, Current)) return;
            Current = template;
            CurrentChanged?.Invoke();
        }

        /// <summary>Rescans the templates folder and reloads plugins. Keeps the current template if it still exists.</summary>
        public void Reload()
        {
            string currentId = Current.Id;
            _loadErrors.Clear();
            _templates.Clear();
            _templates.AddRange(_builtIns);
            _renderers.Clear();
            foreach (var kv in _builtInRenderers) _renderers[kv.Key] = kv.Value;

            // Fresh collectible context each time; plugins are read from memory so their files stay deletable
            _pluginContext?.Unload();
            _pluginContext = null;

            if (Directory.Exists(TemplatesDirectory))
            {
                foreach (string dir in Directory.EnumerateDirectories(TemplatesDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        LoadFolder(dir);
                    }
                    catch (Exception ex)
                    {
                        _loadErrors.Add($"{Path.GetFileName(dir)}: {ex.Message}");
                    }
                }
            }

            var keep = Find(currentId);
            bool currentChanged = keep == null;
            Current = keep ?? _builtIns[0];
            TemplatesChanged?.Invoke();
            if (currentChanged) CurrentChanged?.Invoke();
        }

        private void LoadFolder(string dir)
        {
            var pluginRenderers = new List<IWheelRenderer>();
            foreach (string dll in Directory.EnumerateFiles(dir, "*.dll"))
            {
                pluginRenderers.AddRange(LoadPlugin(dll));
            }

            string json = Path.Combine(dir, TemplateFileName);
            var found = new List<WheelTemplate>();
            if (File.Exists(json))
            {
                found.AddRange(ParseTemplates(File.ReadAllText(json), Path.GetFileName(dir)));
            }
            else if (pluginRenderers.Count > 0)
            {
                // A bare plugin: offer each renderer it ships with the default colors
                foreach (var r in pluginRenderers)
                {
                    found.Add(new WheelTemplate { Id = Slug(r.Id), Name = r.DisplayName, Author = "Plugin", Renderer = r.Id, Description = $"Renderer from {Path.GetFileName(dir)}" });
                }
            }
            else
            {
                return;
            }

            foreach (var t in found)
            {
                t.Directory = dir;
                t.Source = pluginRenderers.Count > 0 ? TemplateSource.Plugin : TemplateSource.Community;
                if (Find(t.Id) != null)
                {
                    _loadErrors.Add($"{t.Name}: a template with id \"{t.Id}\" is already loaded, skipped");
                    continue;
                }
                if (!_renderers.ContainsKey(t.Renderer))
                {
                    _loadErrors.Add($"{t.Name}: renderer \"{t.Renderer}\" isn't installed, using the ring renderer");
                }
                _templates.Add(t);
            }
        }

        private IEnumerable<IWheelRenderer> LoadPlugin(string dll)
        {
            var info = new FileInfo(dll);
            if (info.Length > MaxPluginBytes) throw new TemplateException($"{info.Name} is larger than 20 MB");

            _pluginContext ??= new AssemblyLoadContext("OgConverter.Templates", isCollectible: true);
            Assembly asm;
            // Loaded from memory so the file is never locked; the .pdb beside it (if any) lets debuggers hit breakpoints
            string pdb = Path.ChangeExtension(dll, ".pdb");
            using (var fs = File.OpenRead(dll))
            using (var symbols = File.Exists(pdb) ? File.OpenRead(pdb) : null)
            {
                asm = _pluginContext.LoadFromStream(fs, symbols);
            }

            var result = new List<IWheelRenderer>();
            Type[] types;
            try
            {
                types = asm.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                throw new TemplateException($"{info.Name} was built against an incompatible version of OG Converter", ex);
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(IWheelRenderer).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
                var renderer = (IWheelRenderer)Activator.CreateInstance(type)!;
                if (string.IsNullOrWhiteSpace(renderer.Id)) continue;
                if (_builtInRenderers.ContainsKey(renderer.Id))
                {
                    _loadErrors.Add($"{info.Name}: renderer id \"{renderer.Id}\" is reserved, skipped");
                    continue;
                }
                _renderers[renderer.Id] = renderer;
                result.Add(renderer);
            }
            return result;
        }

        /// <summary>A template.json holds one template object, or an array of them.</summary>
        private List<WheelTemplate> ParseTemplates(string json, string fallbackName)
        {
            List<WheelTemplate> list;
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                list = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.Deserialize<List<WheelTemplate>>(JsonOptions) ?? new()
                    : new List<WheelTemplate> { doc.RootElement.Deserialize<WheelTemplate>(JsonOptions) ?? new() };
            }
            catch (JsonException ex)
            {
                throw new TemplateException($"template.json isn't valid JSON (line {ex.LineNumber + 1}): {ex.Message}", ex);
            }

            foreach (var t in list) Normalize(t, fallbackName);
            return list;
        }

        private void Normalize(WheelTemplate t, string fallbackName)
        {
            t.Name = string.IsNullOrWhiteSpace(t.Name) ? fallbackName : t.Name.Trim();
            t.Id = Slug(string.IsNullOrWhiteSpace(t.Id) ? t.Name : t.Id);
            if (_builtIns.Any(b => string.Equals(b.Id, t.Id, StringComparison.OrdinalIgnoreCase))) t.Id = "community-" + t.Id;
            t.Renderer = string.IsNullOrWhiteSpace(t.Renderer) ? "ring" : t.Renderer.Trim();
            t.Wheel ??= new WheelColors();
            t.App ??= new AppColors();
            t.Shape ??= new WheelShape();
            t.Motion ??= new WheelMotion();
            t.Sounds ??= new WheelSounds();
            t.Shape.InnerRatio = Math.Clamp(t.Shape.InnerRatio <= 0 ? 0.404 : t.Shape.InnerRatio, 0.25, 0.6);
            t.Shape.Gap = Math.Clamp(t.Shape.Gap, 0, 24);
            t.Shape.CornerRadius = Math.Clamp(t.Shape.CornerRadius, 0, 40);
            t.Motion.DurationMs = Math.Clamp(t.Motion.DurationMs <= 0 ? 260 : t.Motion.DurationMs, 60, 1200);
            t.Motion.HoverMs = Math.Clamp(t.Motion.HoverMs <= 0 ? 110 : t.Motion.HoverMs, 20, 600);
            t.Sounds.Volume = Math.Clamp(t.Sounds.Volume, 0, 1);
        }

        private static string Slug(string value)
        {
            string s = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return s.Length == 0 ? "template" : s.Length > 48 ? s[..48] : s;
        }

        // ------------------------------------------------------------------ import / remove / export

        /// <summary>True for paths <see cref="Import"/> accepts.</summary>
        public static bool IsImportable(string path) =>
            Directory.Exists(path) || Path.GetExtension(path).ToLowerInvariant() is ".json" or ".zip" or ".ogtemplate" or ".dll";

        /// <summary>True if importing <paramref name="path"/> would load compiled code.</summary>
        public static bool ContainsCode(string path)
        {
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".dll") return true;
                if (Directory.Exists(path)) return Directory.EnumerateFiles(path, "*.dll").Any();
                if (ext is ".zip" or ".ogtemplate")
                {
                    using var zip = ZipFile.OpenRead(path);
                    return zip.Entries.Any(e => e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (IOException) { }
            catch (InvalidDataException) { }
            return false;
        }

        /// <summary>
        /// Copies a template into the templates folder and loads it. Accepts a template.json (its referenced sounds
        /// are copied along), a folder, a .zip/.ogtemplate package, or a plugin .dll. Returns the templates it added.
        /// </summary>
        public IReadOnlyList<WheelTemplate> Import(string path)
        {
            path = Path.GetFullPath(path);
            string staging = Path.Combine(Path.GetTempPath(), "og-template-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string name;
                if (Directory.Exists(path))
                {
                    CopyDirectory(path, staging);
                    name = Path.GetFileName(path.TrimEnd('\\', '/'));
                }
                else if (ext is ".zip" or ".ogtemplate")
                {
                    ZipFile.ExtractToDirectory(path, staging); // rejects entries that would land outside the folder
                    name = Path.GetFileNameWithoutExtension(path);
                    // Zips often wrap everything in one folder
                    var dirs = Directory.GetDirectories(staging);
                    if (!File.Exists(Path.Combine(staging, TemplateFileName)) && Directory.GetFiles(staging).Length == 0 && dirs.Length == 1)
                    {
                        name = Path.GetFileName(dirs[0]);
                        string inner = dirs[0];
                        string flat = staging + "-flat";
                        Directory.Move(inner, flat);
                        Directory.Delete(staging, true);
                        Directory.Move(flat, staging);
                    }
                }
                else if (ext == ".json")
                {
                    var parsed = ParseTemplates(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
                    File.Copy(path, Path.Combine(staging, TemplateFileName));
                    CopyReferencedSounds(Path.GetDirectoryName(path)!, staging, parsed);
                    name = parsed[0].Id;
                }
                else if (ext == ".dll")
                {
                    string src = Path.GetDirectoryName(path)!;
                    File.Copy(path, Path.Combine(staging, Path.GetFileName(path)));
                    string symbols = Path.ChangeExtension(path, ".pdb");
                    if (File.Exists(symbols)) File.Copy(symbols, Path.Combine(staging, Path.GetFileName(symbols)));
                    string sibling = Path.Combine(src, TemplateFileName);
                    if (File.Exists(sibling))
                    {
                        File.Copy(sibling, Path.Combine(staging, TemplateFileName));
                        CopyReferencedSounds(src, staging, ParseTemplates(File.ReadAllText(sibling), Path.GetFileNameWithoutExtension(path)));
                    }
                    name = Path.GetFileNameWithoutExtension(path);
                }
                else
                {
                    throw new TemplateException("Choose a template.json, a template folder, a .zip/.ogtemplate package or a plugin .dll");
                }

                bool hasJson = File.Exists(Path.Combine(staging, TemplateFileName));
                if (!hasJson && !Directory.EnumerateFiles(staging, "*.dll").Any())
                    throw new TemplateException("No template.json or plugin .dll found");
                var incoming = hasJson
                    ? ParseTemplates(File.ReadAllText(Path.Combine(staging, TemplateFileName)), name) // validates before installing
                    : new List<WheelTemplate>();

                Directory.CreateDirectory(TemplatesDirectory);
                string target = Path.Combine(TemplatesDirectory, Slug(name));

                // Re-importing replaces the old copy: the same folder name, or any installed folder with a template of the
                // same id (e.g. added once as a folder and later as its template.json)
                var replace = _templates
                    .Where(t => t.Source != TemplateSource.BuiltIn && t.Directory != null
                                && incoming.Any(i => string.Equals(i.Id, t.Id, StringComparison.OrdinalIgnoreCase)))
                    .Select(t => t.Directory!)
                    .Append(target)
                    .Where(Directory.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (replace.Count > 0)
                {
                    // Their plugins must be released first, so drop all loaded plugins (Reload brings the rest back)
                    _pluginContext?.Unload();
                    _pluginContext = null;
                    foreach (string dir in replace) Directory.Delete(dir, true);
                }
                Directory.Move(staging, target);

                Reload();
                var added = _templates.Where(t => string.Equals(t.Directory, target, StringComparison.OrdinalIgnoreCase)).ToList();
                if (added.Count == 0)
                {
                    string why = _loadErrors.FirstOrDefault(e => e.StartsWith(Path.GetFileName(target), StringComparison.OrdinalIgnoreCase)) ?? "it contains no usable template";
                    throw new TemplateException($"The template was copied but couldn't be loaded: {why}");
                }
                return added;
            }
            catch (TemplateException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException)
            {
                throw new TemplateException(ex.Message, ex);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
            }
        }

        /// <summary>Deletes a community template's folder (and every template that came from it).</summary>
        public void Remove(WheelTemplate template)
        {
            if (template.Source == TemplateSource.BuiltIn || string.IsNullOrEmpty(template.Directory)) return;
            string dir = Path.GetFullPath(template.Directory);
            if (!dir.StartsWith(Path.GetFullPath(TemplatesDirectory), StringComparison.OrdinalIgnoreCase)) return;

            _pluginContext?.Unload();
            _pluginContext = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Reload();
        }

        /// <summary>Writes a template as JSON, a starting point for making a new one.</summary>
        public static void Export(WheelTemplate template, string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
        }

        /// <summary>Resolves a sound path from a template to a file inside its folder, or null.</summary>
        public static string? ResolveAsset(WheelTemplate template, string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || string.IsNullOrEmpty(template.Directory)) return null;
            string root = Path.GetFullPath(template.Directory);
            string full = Path.GetFullPath(Path.Combine(root, relative));
            // No escaping the template folder with ..\ or absolute paths
            if (!full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(full) ? full : null;
        }

        private static void CopyReferencedSounds(string fromDir, string toDir, IEnumerable<WheelTemplate> templates)
        {
            string root = Path.GetFullPath(fromDir);
            foreach (var t in templates)
            {
                foreach (string? rel in new[] { t.Sounds?.Tick, t.Sounds?.Switch, t.Sounds?.Open, t.Sounds?.Select })
                {
                    if (string.IsNullOrWhiteSpace(rel)) continue;
                    string src = Path.GetFullPath(Path.Combine(root, rel));
                    if (!src.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(src)) continue;
                    string dst = Path.Combine(toDir, Path.GetRelativePath(root, src));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, true);
                }
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            foreach (string dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
        }
    }
}
