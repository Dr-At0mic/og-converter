using System.Collections.Generic;

namespace OgConverter.Services
{
    /// <summary>
    /// The drawing side of wheel templates: which renderers exist and loading plugin assemblies that add more.
    /// <see cref="TemplateManager"/> owns the templates themselves.
    /// </summary>
    public interface IWheelRendererHost
    {
        bool HasRenderer(string rendererId);

        /// <summary>Forgets plugin renderers and unloads their assemblies, so their files can be replaced or deleted.</summary>
        void UnloadPlugins();

        /// <summary>
        /// Loads a plugin .dll and registers its renderers. Returns the id and display name of each one added; renderers
        /// that are skipped are explained in <paramref name="errors"/>. Throws <see cref="TemplateException"/> for an unusable file.
        /// </summary>
        IReadOnlyList<(string Id, string DisplayName)> LoadPlugin(string dllPath, ICollection<string> errors);
    }
}
