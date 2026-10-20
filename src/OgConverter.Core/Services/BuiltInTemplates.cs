using System.Collections.Generic;
using OgConverter.Models;

namespace OgConverter.Services
{
    /// <summary>Templates that ship with the app. The first one is the default.</summary>
    internal static class BuiltInTemplates
    {
        public const string DefaultId = "og-classic";

        public static List<WheelTemplate> Create() => new()
        {
            new WheelTemplate
            {
                Id = DefaultId,
                Name = "OG Classic",
                Author = "OG Converter",
                Description = "The original graphite ring with warm orange, blue and violet wheels.",
                Renderer = "ring",
                Wheel = new WheelColors(),
                App = new AppColors { Base = "dark" },
                Motion = new WheelMotion { Open = "pop", Switch = "stagger", DurationMs = 240 },
                Sounds = new WheelSounds { Pack = "click" }
            },
            new WheelTemplate
            {
                Id = "aqua",
                Name = "Aqua",
                Author = "OG Converter",
                Description = "Light frosted tiles with macOS system colors and glassy taps.",
                Renderer = "glass",
                Wheel = new WheelColors
                {
                    Ring = "#F2F6F6F8", Hub = "#FBFBFD", Text = "#1D1D1F", TextMuted = "#5E5E66", TextFaint = "#8E8E93",
                    Edge = "#26000000", Divider = "#14000000", Shadow = "#4A000000",
                    Convert = "#007AFF", Tools = "#FF9500", Actions = "#AF52DE", OnAccent = "#FFFFFF"
                },
                App = new AppColors { Base = "light", Accent = "#007AFF", CardRadius = 12, ControlRadius = 7 },
                Shape = new WheelShape { InnerRatio = 0.40, Gap = 6, CornerRadius = 12 },
                Motion = new WheelMotion { Open = "spring", Switch = "stagger", DurationMs = 320, HoverMs = 120 },
                Sounds = new WheelSounds { Pack = "glass" }
            },
            new WheelTemplate
            {
                Id = "graphite",
                Name = "Graphite",
                Author = "OG Converter",
                Description = "macOS dark mode: smoky glass tiles, vivid system accents.",
                Renderer = "glass",
                Wheel = new WheelColors
                {
                    Ring = "#F22C2C2E", Hub = "#232325", Text = "#F5F5F7", TextMuted = "#A1A1A6", TextFaint = "#7C7C80",
                    Edge = "#2AFFFFFF", Divider = "#14FFFFFF", Shadow = "#90000000",
                    Convert = "#0A84FF", Tools = "#FF9F0A", Actions = "#BF5AF2", OnAccent = "#FFFFFF"
                },
                App = new AppColors
                {
                    Base = "dark",
                    Window = "#1C1C1E", Surface = "#242426", SurfaceRaised = "#2C2C2E", SurfaceHover = "#363638", SurfacePressed = "#3D3D40",
                    Input = "#1A1A1C", Border = "#323234", BorderStrong = "#424246", Divider = "#2A2A2C",
                    TextPrimary = "#F5F5F7", TextSecondary = "#A1A1A6", TextTertiary = "#6E6E73",
                    Accent = "#0A84FF", CardRadius = 12, ControlRadius = 7
                },
                Shape = new WheelShape { InnerRatio = 0.40, Gap = 6, CornerRadius = 12 },
                Motion = new WheelMotion { Open = "spring", Switch = "stagger", DurationMs = 320, HoverMs = 120 },
                Sounds = new WheelSounds { Pack = "glass" }
            },
            new WheelTemplate
            {
                Id = "orbit",
                Name = "Orbit",
                Author = "OG Converter",
                Description = "Dock-style bubbles that magnify under the cursor, on a midnight disc.",
                Renderer = "orbit",
                Wheel = new WheelColors
                {
                    Ring = "#EB0E1426", Hub = "#141B30", Text = "#E6ECFF", TextMuted = "#94A0C4", TextFaint = "#66729A",
                    Edge = "#338EA2FF", Divider = "#508EA2FF", Shadow = "#90000000",
                    Convert = "#2DD4BF", Tools = "#60A5FA", Actions = "#F472B6"
                },
                App = new AppColors
                {
                    Base = "dark",
                    Window = "#0A0F1C", Surface = "#111829", SurfaceRaised = "#172036", SurfaceHover = "#1D2742", SurfacePressed = "#232F4E",
                    Input = "#0C1220", Border = "#1E2840", BorderStrong = "#2A3656", Divider = "#18213A",
                    TextPrimary = "#E6ECFF", TextSecondary = "#9AA6C8", TextTertiary = "#66729A",
                    Accent = "#2DD4BF", CardRadius = 14, ControlRadius = 8
                },
                Shape = new WheelShape { InnerRatio = 0.36 },
                Motion = new WheelMotion { Open = "bloom", Switch = "spin", DurationMs = 340, HoverMs = 130 },
                Sounds = new WheelSounds { Pack = "bubble" }
            },
            new WheelTemplate
            {
                Id = "neon-night",
                Name = "Neon Night",
                Author = "OG Converter",
                Description = "Synthwave HUD: glowing magenta, cyan and lime, mono labels, synth blips.",
                Renderer = "neon",
                Wheel = new WheelColors
                {
                    Ring = "#F20A0814", Hub = "#0D0A1A", Text = "#F1EAFF", TextMuted = "#A99BD6", TextFaint = "#7A6CA8",
                    Edge = "#40FF2BD6", Divider = "#3AB9A7FF", Shadow = "#A0000000",
                    Convert = "#FF2BD6", Tools = "#22E4FF", Actions = "#B6FF3B"
                },
                App = new AppColors
                {
                    Base = "dark",
                    Window = "#07060D", Surface = "#0F0C1C", SurfaceRaised = "#171329", SurfaceHover = "#1F1A36", SurfacePressed = "#272042",
                    Input = "#0A0814", Border = "#231D3D", BorderStrong = "#342A58", Divider = "#1A1530",
                    TextPrimary = "#F1EAFF", TextSecondary = "#A99BD6", TextTertiary = "#6E6296",
                    Accent = "#FF2BD6", CardRadius = 6, ControlRadius = 4
                },
                Shape = new WheelShape { InnerRatio = 0.42, Font = "Cascadia Mono, Consolas", UppercaseLabels = true },
                Motion = new WheelMotion { Open = "spin", Switch = "spin", DurationMs = 300, HoverMs = 90 },
                Sounds = new WheelSounds { Pack = "synth" }
            },
        };
    }
}
