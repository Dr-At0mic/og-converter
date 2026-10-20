# Wheel templates

A template controls how the wheel **looks, moves and sounds**, and gives the rest of OG Converter (Home, Settings, dialogs, notifications) a matching palette. Switching templates restyles everything at once.

OG Converter ships with five:

| Template | Renderer | Feel |
|---|---|---|
| **OG Classic** | `ring` | The original graphite ring, warm orange / blue / violet |
| **Aqua** | `glass` | macOS light: frosted rounded tiles, system blue / orange / purple, glassy taps |
| **Graphite** | `glass` | macOS dark mode version of Aqua |
| **Orbit** | `orbit` | Dock-style bubbles that magnify under the cursor, bubbly sounds |
| **Neon Night** | `neon` | Synthwave HUD: glowing outlines, tick marks, mono labels, synth blips |

Pick one in **Settings › Wheel templates** (hover a card to preview its animation) or from the switcher on **Home**.

> 🐧 **Linux (beta):** the five built-ins and every `template.json` / `.ogtemplate` template work the same on Linux. Renderer plugins
> (`.dll`) are **Windows only for now**: they're built on WPF, and the Linux app can't load plugins yet, so a template that ships one
> is drawn with the `ring` renderer there. This will be sorted out soon; please [report](../../../issues) anything else that looks off.

> **New to this?** The step-by-step [beginner's guide](PLUGIN_GUIDE.md) explains every setting and walks through writing a plugin. This page is the short reference.

There are two ways to make your own:

1. **A `template.json`.** No code: colors, shape, motion and sounds, on top of any installed renderer.
2. **A renderer plugin.** A .NET class library that draws the wheel however you like, plus a `template.json` that uses it.

## Installing a template

- **Settings › Wheel templates › Add template…** and pick a `template.json`, a `.zip` / `.ogtemplate` package, or a plugin `.dll`.
- Or drag any of those, or a folder, onto the template gallery.
- Or copy a folder containing `template.json` into the templates folder (**Folder** button), then press **Reload**.

Templates live in `%AppData%\OGConverter\Templates\<name>\` (Linux: `~/.config/OGConverter/Templates/<name>/`), one folder each. Set the `OGCONVERTER_TEMPLATES_DIR` environment variable to use a different folder (handy for a portable setup or for testing).

> **Plugins are programs.** A `.dll` runs with the same access to your files as OG Converter. The app asks before adding one. Only install plugins from people you trust.

## `template.json`

Comments and trailing commas are allowed. Everything except `name` is optional: anything you leave out keeps the OG Classic value. A file may also hold an **array** of templates.

```jsonc
{
  "id": "sunset",                 // lower-case, a-z 0-9 and dashes; derived from the name if omitted
  "name": "Sunset",
  "author": "You",
  "description": "Shown as the card's tooltip",
  "renderer": "glass",            // ring | glass | orbit | neon | an id from a plugin

  "wheel": {                      // #RRGGBB or #AARRGGBB
    "ring": "#F2291A2E",          // wheel surface
    "hub": "#21142A",             // center disc
    "text": "#FFF1E8",
    "textMuted": "#D9B8B0",       // icons, descriptions
    "textFaint": "#A88A92",       // hints
    "edge": "#33FFC9A8",          // outlines
    "divider": "#18FFFFFF",
    "shadow": "#90100010",
    "convert": "#FF7A59",         // accent per wheel
    "tools": "#FFC145",
    "actions": "#C49BFF",
    "onAccent": "#FFFFFF"         // text on a filled selection (default: white or black by contrast)
  },

  "app": {                        // the rest of the app
    "base": "dark",               // dark | light: fills in anything you don't set
    "window": "#140C18",
    "surface": "#1D1222",         // cards
    "surfaceRaised": "#26182C",   // buttons, inputs, chips
    "surfaceHover": "#2F1F36",
    "surfacePressed": "#382640",
    "input": "#120A16",
    "border": "#33223A",
    "borderStrong": "#46304F",
    "divider": "#2A1C30",
    "textPrimary": "#FFF1E8",
    "textSecondary": "#D9B8B0",
    "textTertiary": "#94767E",
    "accent": "#FF7A59",          // default: the wheel's convert color
    "success": "#3DD68C",
    "warning": "#F5B83D",
    "danger": "#F2555A",
    "cardRadius": 14,             // 0–24
    "controlRadius": 8            // 0–16
  },

  "shape": {
    "innerRatio": 0.38,           // hub size / wheel size, 0.25–0.6
    "gap": 8,                     // px between options (glass)
    "cornerRadius": 16,           // px option rounding (glass)
    "font": "Bahnschrift",        // label font; default Segoe UI Variable
    "uppercaseLabels": false
  },

  "motion": {
    "open": "bloom",              // pop | spring | spin | bloom | fade | none
    "switch": "stagger",          // stagger | spin | fade | none  (Shift → Ctrl → Alt)
    "durationMs": 340,            // 60–1200
    "hoverMs": 130                // how quickly the highlight follows the cursor, 20–600
  },

  "sounds": {
    "pack": "glass",              // click | glass | bubble | synth | soft | none
    "volume": 0.8,                // 0–1, for the synthesized packs
    "tick": "sounds/tick.wav",    // optional .wav files, relative to the template folder,
    "switch": "sounds/switch.wav",//   each up to 2 MB; they replace the pack's sound
    "open": "sounds/open.wav",
    "select": "sounds/select.wav"
  },

  "options": { "anything": "free-form strings for plugin renderers" }
}
```

A complete example is in [`samples/templates/sunset/template.json`](../samples/templates/sunset/template.json). **Export** in the gallery saves the active template as JSON, a good starting point for your own.

Sounds play when you move onto an option (`tick`), switch wheels (`switch`), open the wheel (`open`), and drop a file on an option (`select`). Users can turn them all off in Settings › Wheel › Sound feedback.

To share a template, zip its folder and rename it to `.ogtemplate` (or leave it as `.zip`).

## Renderer plugins

A plugin is a class library that references OG Converter and implements `OgConverter.Wheel.IWheelRenderer`:

```csharp
public interface IWheelRenderer
{
    string Id { get; }            // what template.json's "renderer" refers to
    string DisplayName { get; }   // used when the plugin has no template.json
    void Render(DrawingContext dc, WheelFrame frame);
    int? HitTest(Point position, WheelFrame frame) => null;   // null = default angular slices
}
```

Every public, non-abstract class with a parameterless constructor that implements it is registered. The built-in ids (`ring`, `glass`, `orbit`, `neon`) are reserved.

`WheelFrame` gives you everything for one frame: center and radii, `Scale` and `TextScale`, `Density` (Full / Compact / Micro for small wheels), the options with their **animated** hover amounts (`HoverOf(i)`, 0–1), the current `Accent` (animated between wheels), the template's resolved `Palette`, the hub text, and the open/switch animation (`Rotation` is folded into `SliceStart(i)` / `SliceMid(i)`; wrap each option's drawing in `using (WheelPainter.Item(dc, frame, i, center))` to get its staggered entrance).

`WheelPainter` has the helpers the built-in renderers use: cached frozen brushes (`Solid`), rounded and gapped ring segments (`RoundedSector`, `InsetSector`), arcs, shadows, `DrawItemContent` (icon + label for any density) and `DrawHubContent` (the hovered option, file badge or message in the center).

`Render` runs on the UI thread on every frame while the wheel animates, so keep it light. If it throws, OG Converter logs the error and draws the built-in ring instead.

The [sample plugin](../samples/WheelPluginSample) draws options as petals:

```powershell
cd samples/WheelPluginSample
dotnet build -c Release
# then Add template… → bin/Release/net10.0-windows10.0.19041.0/WheelPluginSample.dll
```

Its `template.json` is imported along with the `.dll`. Plugins are loaded into a collectible context from memory, so their files are never locked and can be updated or removed while the app runs.

## Testing

`og-converter.exe --test` includes a template suite: every built-in renders in every mode at 40% and 100%, JSON and package imports are normalized and sandboxed, bad files are rejected. Set `OGCONVERTER_PLUGIN_SAMPLE` to the sample plugin's `.dll` to include the plugin checks. `og-converter.exe --snapshot <folder>` renders every installed template (Home, Settings, each wheel, the smallest size) to PNG.
