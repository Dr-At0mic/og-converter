using System;
using System.Collections.Generic;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Wheel.Renderers;

namespace OgConverter.Wheel
{
    /// <summary>
    /// The Avalonia wheel renderers. Only the built-ins: renderer plugins are WPF assemblies, so templates that ship one
    /// load on Linux but draw with the ring. <see cref="TemplateManager"/> drives it through <see cref="IWheelRendererHost"/>.
    /// </summary>
    public sealed class WheelRendererHost : IWheelRendererHost
    {
        public static WheelRendererHost Instance { get; } = new();

        private readonly Dictionary<string, IWheelRenderer> _renderers = new(StringComparer.OrdinalIgnoreCase);

        private WheelRendererHost()
        {
            foreach (var r in new IWheelRenderer[] { new RingRenderer(), new GlassRenderer(), new OrbitRenderer(), new NeonRenderer() })
            {
                _renderers[r.Id] = r;
            }
        }

        public IWheelRenderer RendererFor(WheelTemplate template) =>
            _renderers.TryGetValue(template.Renderer ?? string.Empty, out var r) ? r : _renderers["ring"];

        public bool HasRenderer(string rendererId) => _renderers.ContainsKey(rendererId);

        public void UnloadPlugins() { }

        public IReadOnlyList<(string Id, string DisplayName)> LoadPlugin(string dllPath, ICollection<string> errors)
        {
            errors.Add($"{System.IO.Path.GetFileName(dllPath)}: renderer plugins are Windows-only for now, skipped");
            return Array.Empty<(string, string)>();
        }
    }

    /// <summary>Wires the wheel into the portable services.</summary>
    public static class LinuxWheel
    {
        /// <summary>
        /// Registers the wheel renderers with <see cref="Services.Platform.WheelRenderers"/>. Call at startup before
        /// <see cref="TemplateManager.Instance"/> is first used (it scans templates and asks which renderers exist).
        /// </summary>
        public static void Register()
        {
            Services.Platform.WheelRenderers ??= WheelRendererHost.Instance;
        }
    }

    public static class TemplateManagerRenderers
    {
        /// <summary>The renderer that draws <paramref name="template"/>; the ring when its renderer isn't installed.</summary>
        public static IWheelRenderer RendererFor(this TemplateManager manager, WheelTemplate template) =>
            WheelRendererHost.Instance.RendererFor(template);
    }
}
