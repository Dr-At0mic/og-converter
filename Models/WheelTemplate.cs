using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OgConverter.Models
{
    public enum TemplateSource { BuiltIn, Community, Plugin }

    /// <summary>
    /// A wheel template: how the wheel looks, moves and sounds, and the matching palette for the rest of the app.
    /// Built-in templates are defined in code; community templates are a <c>template.json</c> with this shape
    /// (see docs/TEMPLATES.md). Every field is optional except <see cref="Id"/> and <see cref="Name"/>.
    /// </summary>
    public sealed class WheelTemplate
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0";

        /// <summary>Renderer that draws the wheel. Built in: ring, glass, orbit, neon. Plugins can add more.</summary>
        public string Renderer { get; set; } = "ring";

        public WheelColors Wheel { get; set; } = new();
        public AppColors App { get; set; } = new();
        public WheelShape Shape { get; set; } = new();
        public WheelMotion Motion { get; set; } = new();
        public WheelSounds Sounds { get; set; } = new();

        /// <summary>Free-form values read by plugin renderers.</summary>
        public Dictionary<string, string>? Options { get; set; }

        [JsonIgnore] public TemplateSource Source { get; set; } = TemplateSource.Community;

        /// <summary>Folder the template was loaded from (resolves relative sound paths). Null for built-ins.</summary>
        [JsonIgnore] public string? Directory { get; set; }

        /// <summary>Resolved colors and brushes, built on first use.</summary>
        [JsonIgnore] internal object? ResolvedCache { get; set; }

        /// <summary>Shown by combo boxes (the app's ComboBox template ignores DisplayMemberPath for the selected item).</summary>
        public override string ToString() => Name;
    }

    /// <summary>Wheel colors. Hex strings: #RRGGBB or #AARRGGBB.</summary>
    public sealed class WheelColors
    {
        public string Ring { get; set; } = "#F515171C";
        public string Hub { get; set; } = "#1B1D23";
        public string Text { get; set; } = "#ECEDEF";
        public string TextMuted { get; set; } = "#A1A6B0";
        public string TextFaint { get; set; } = "#7A7F8A";
        public string Edge { get; set; } = "#30FFFFFF";
        public string Divider { get; set; } = "#16FFFFFF";
        public string Shadow { get; set; } = "#70000000";

        /// <summary>Accent per wheel.</summary>
        public string Convert { get; set; } = "#FF6B35";
        public string Tools { get; set; } = "#3B9EFF";
        public string Actions { get; set; } = "#A78BFA";

        /// <summary>Text drawn on a filled (hovered) option. Null lets the renderer decide.</summary>
        public string? OnAccent { get; set; }
    }

    /// <summary>
    /// Palette for the main window, dialogs and toasts. Anything left out is derived from <see cref="Base"/>
    /// ("dark" or "light") and the wheel's Convert accent.
    /// </summary>
    public sealed class AppColors
    {
        public string Base { get; set; } = "dark";
        public string? Window { get; set; }
        public string? Surface { get; set; }
        public string? SurfaceRaised { get; set; }
        public string? SurfaceHover { get; set; }
        public string? SurfacePressed { get; set; }
        public string? Input { get; set; }
        public string? Border { get; set; }
        public string? BorderStrong { get; set; }
        public string? Divider { get; set; }
        public string? TextPrimary { get; set; }
        public string? TextSecondary { get; set; }
        public string? TextTertiary { get; set; }
        public string? Accent { get; set; }
        public string? Success { get; set; }
        public string? Warning { get; set; }
        public string? Danger { get; set; }
        public double? CardRadius { get; set; }
        public double? ControlRadius { get; set; }
    }

    public sealed class WheelShape
    {
        /// <summary>Hub size relative to the wheel (inner radius / outer radius), 0.25–0.6.</summary>
        public double InnerRatio { get; set; } = 0.404;

        /// <summary>Gap between options in pixels at 100% size (glass renderer).</summary>
        public double Gap { get; set; } = 0;

        /// <summary>Corner rounding of option segments in pixels at 100% size (glass renderer).</summary>
        public double CornerRadius { get; set; } = 0;

        /// <summary>Font for option labels. Null uses Segoe UI Variable.</summary>
        public string? Font { get; set; }

        public bool UppercaseLabels { get; set; }
    }

    public sealed class WheelMotion
    {
        /// <summary>How the wheel appears: pop, spring, spin, bloom, fade, none.</summary>
        public string Open { get; set; } = "pop";

        /// <summary>How options change when switching wheels: stagger, spin, fade, none.</summary>
        public string Switch { get; set; } = "stagger";

        public int DurationMs { get; set; } = 260;

        /// <summary>How quickly the hover highlight follows the cursor.</summary>
        public int HoverMs { get; set; } = 110;
    }

    public sealed class WheelSounds
    {
        /// <summary>Synthesized sound set: click, glass, bubble, synth, soft, none.</summary>
        public string Pack { get; set; } = "click";

        /// <summary>0–1.</summary>
        public double Volume { get; set; } = 1.0;

        /// <summary>Optional .wav files (relative to the template folder) that replace the pack's sounds.</summary>
        public string? Tick { get; set; }
        public string? Switch { get; set; }
        public string? Open { get; set; }
        public string? Select { get; set; }
    }
}
