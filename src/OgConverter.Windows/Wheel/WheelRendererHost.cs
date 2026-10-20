using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Wheel.Renderers;

namespace OgConverter.Wheel
{
    /// <summary>
    /// The WPF wheel renderers: the built-ins, and those plugin .dll files add (loaded into a collectible context from
    /// memory, so their files are never locked). <see cref="TemplateManager"/> drives it through <see cref="IWheelRendererHost"/>.
    /// </summary>
    public sealed class WheelRendererHost : IWheelRendererHost
    {
        public static WheelRendererHost Instance { get; } = new();

        private const long MaxPluginBytes = 20 * 1024 * 1024;

        private readonly Dictionary<string, IWheelRenderer> _builtInRenderers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IWheelRenderer> _renderers = new(StringComparer.OrdinalIgnoreCase);
        private AssemblyLoadContext? _pluginContext;

        private WheelRendererHost()
        {
            foreach (var r in new IWheelRenderer[] { new RingRenderer(), new GlassRenderer(), new OrbitRenderer(), new NeonRenderer() })
            {
                _builtInRenderers[r.Id] = r;
            }
            foreach (var kv in _builtInRenderers) _renderers[kv.Key] = kv.Value;
        }

        public IWheelRenderer RendererFor(WheelTemplate template) =>
            _renderers.TryGetValue(template.Renderer ?? string.Empty, out var r) ? r : _builtInRenderers["ring"];

        public bool HasRenderer(string rendererId) => _renderers.ContainsKey(rendererId);

        public void UnloadPlugins()
        {
            _renderers.Clear();
            foreach (var kv in _builtInRenderers) _renderers[kv.Key] = kv.Value;

            // Fresh collectible context each time
            _pluginContext?.Unload();
            _pluginContext = null;
        }

        public IReadOnlyList<(string Id, string DisplayName)> LoadPlugin(string dll, ICollection<string> errors)
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

            var result = new List<(string Id, string DisplayName)>();
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
                    errors.Add($"{info.Name}: renderer id \"{renderer.Id}\" is reserved, skipped");
                    continue;
                }
                _renderers[renderer.Id] = renderer;
                result.Add((renderer.Id, renderer.DisplayName));
            }
            return result;
        }
    }

    public static class TemplateManagerRenderers
    {
        /// <summary>The renderer that draws <paramref name="template"/>; the ring when its renderer isn't installed.</summary>
        public static IWheelRenderer RendererFor(this TemplateManager manager, WheelTemplate template) =>
            WheelRendererHost.Instance.RendererFor(template);
    }
}
