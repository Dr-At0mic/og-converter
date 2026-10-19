# Make your own OG Converter wheel: a beginner's guide

This guide walks you through creating a **wheel template** for OG Converter, from changing a few colors to writing a plugin that draws a brand-new wheel. You don't need any programming experience for Part 1. Part 2 assumes you can install tools and copy-paste code; everything else is explained along the way.

> Looking for a quick lookup table instead? See [TEMPLATES.md](TEMPLATES.md).

---

## Contents

- [What is a template?](#what-is-a-template)
- **Part 1: A template without code**
  - [1. Make the folder and file](#1-make-the-folder-and-file)
  - [2. Add it to OG Converter](#2-add-it-to-og-converter)
  - [3. Change it and see the result](#3-change-it-and-see-the-result)
  - [4. Every setting, explained](#4-every-setting-explained)
    - [About the template](#about-the-template-name-id-author)
    - [renderer: which wheel design](#renderer-which-wheel-design)
    - [wheel: the wheel's colors](#wheel-the-wheels-colors)
    - [app: the colors of the rest of the app](#app-the-colors-of-the-rest-of-the-app)
    - [shape: hub size, gaps, rounding, font](#shape-hub-size-gaps-rounding-font)
    - [motion: animations](#motion-animations)
    - [sounds: sound effects](#sounds-sound-effects)
    - [options: settings for plugins](#options-settings-for-plugins)
  - [5. Writing colors](#5-writing-colors)
  - [6. Several templates in one file](#6-several-templates-in-one-file)
  - [7. Share your template](#7-share-your-template)
- **Part 2: A renderer plugin (code)**
  - [8. What you need](#8-what-you-need)
  - [9. Create the project](#9-create-the-project)
  - [10. How the wheel is laid out](#10-how-the-wheel-is-laid-out)
  - [11. Build the renderer step by step](#11-build-the-renderer-step-by-step)
  - [12. Build, install, try it](#12-build-install-try-it)
  - [13. API reference](#13-api-reference)
  - [14. Rules, performance and safety](#14-rules-performance-and-safety)
- [15. Testing and debugging](#15-testing-and-debugging)
- [16. Troubleshooting](#16-troubleshooting)
- [17. Checklist before you share](#17-checklist-before-you-share)

---

## What is a template?

When you drag a file and tap **Shift**, **Ctrl** or **Alt**, OG Converter shows a round menu called the **wheel**. A **template** decides:

| | What it controls | Where it's set |
|---|---|---|
| **Look** | Which design draws the wheel (ring, tiles, bubbles, neon…) and its colors | `renderer`, `wheel`, `shape` |
| **Motion** | How the wheel appears, how it changes between Convert / Tools / Actions, how fast the highlight follows your mouse | `motion` |
| **Sound** | The clicks and blips it makes | `sounds` |
| **App colors** | The colors of the main window, Settings, dialogs and pop-up notifications | `app` |

A template is a small text file named **`template.json`**. The design itself comes from a **renderer**. OG Converter has four built in (`ring`, `glass`, `orbit`, `neon`), so most templates only need the text file. If you want a design that doesn't exist yet, you write a **renderer plugin** (Part 2): a small `.dll` that draws the wheel your way.

The five templates that ship with the app are a good place to look for ideas:

| Template | Renderer | Look | Motion | Sounds |
|---|---|---|---|---|
| OG Classic | `ring` | Graphite ring, orange / blue / violet | `pop` / `stagger` | `click` |
| Aqua | `glass` | macOS light, frosted tiles | `spring` / `stagger` | `glass` |
| Graphite | `glass` | macOS dark | `spring` / `stagger` | `glass` |
| Orbit | `orbit` | Dock-style bubbles | `bloom` / `spin` | `bubble` |
| Neon Night | `neon` | Synthwave HUD | `spin` / `spin` | `synth` |

Tip: in **Settings › Wheel templates**, select one and press **Export** to save it as a `template.json`. That's the quickest way to start: copy a built-in and change it.

---

# Part 1: A template without code

## 1. Make the folder and file

1. Make a new folder anywhere, for example `Documents\My Sunrise Template`.
2. Inside it, create a text file named exactly **`template.json`**. In Notepad: *File › Save as*, set *Save as type* to **All files**, type `template.json`, and choose **UTF-8** encoding.
3. Paste this in and save:

```jsonc
{
  "name": "Sunrise",
  "author": "Your Name",
  "description": "Soft morning colors.",
  "renderer": "glass",

  "wheel": {
    "convert": "#FF8A5B",
    "tools": "#FFD166",
    "actions": "#7AD3C4"
  }
}
```

That's a complete template. Anything you don't write keeps OG Converter's default value (the OG Classic look), so you only write what you want to change.

> **Use a code editor if you can.** [VS Code](https://code.visualstudio.com/) is free and highlights mistakes in JSON. The file can contain `// comments` and trailing commas; OG Converter accepts both.

## 2. Add it to OG Converter

Any of these works:

- **Settings › Wheel templates › Add template…**, then pick your `template.json`.
- Drag the `template.json` (or the whole folder) onto the template cards in Settings.
- Copy your folder into the templates folder (the **Folder** button in Settings opens it: `%AppData%\OGConverter\Templates`), then press **Reload**.

OG Converter copies your template into its templates folder and switches to it. With **Add template…**, any sound files your template points to are copied along. Your template now has its own card, marked **Community**.

## 3. Change it and see the result

The copy OG Converter uses lives in `%AppData%\OGConverter\Templates\<your-template-id>\template.json`. To experiment:

1. Press **Folder** in Settings, open your template's folder, edit `template.json` there and save.
2. Press **Reload** in Settings.
3. Look at the result:
   - **Hover over the card** to replay its opening animation.
   - Press **▶** in the title bar to open the wheel in the middle of the screen. Move your mouse over the options and press **Esc** to close it.
   - Press **Play sample** in Settings › Wheel to hear the hover sound.
   - The **Home** page and **Settings** use your `app` colors straight away.

Or keep editing your original file and add it again with **Add template…** each time. Adding a template whose `id` is already installed replaces the installed copy, whichever way you add it (file, folder or zip).

## 4. Every setting, explained

Here is a template with **every** setting. Below it, each one is explained.

```jsonc
{
  "schemaVersion": 1,
  "id": "sunrise",
  "name": "Sunrise",
  "author": "Your Name",
  "description": "Soft morning colors.",
  "version": "1.0",
  "renderer": "glass",

  "wheel": {
    "ring": "#F2FFF8F0",
    "hub": "#FFFDF8",
    "text": "#2B2118",
    "textMuted": "#7A6656",
    "textFaint": "#A8968A",
    "edge": "#26000000",
    "divider": "#14000000",
    "shadow": "#50000000",
    "convert": "#FF8A5B",
    "tools": "#E0A500",
    "actions": "#2FA89A",
    "onAccent": "#FFFFFF"
  },

  "app": {
    "base": "light",
    "window": "#F6EFE8",
    "surface": "#FFFFFF",
    "surfaceRaised": "#FBF6F1",
    "surfaceHover": "#F3EAE1",
    "surfacePressed": "#EBDFD4",
    "input": "#FFFFFF",
    "border": "#E6D9CC",
    "borderStrong": "#D3C2B2",
    "divider": "#EFE5DB",
    "textPrimary": "#2B2118",
    "textSecondary": "#6B5A4C",
    "textTertiary": "#9C8A7C",
    "accent": "#FF8A5B",
    "success": "#2E9E5B",
    "warning": "#B7791F",
    "danger": "#D64545",
    "cardRadius": 14,
    "controlRadius": 8
  },

  "shape": {
    "innerRatio": 0.40,
    "gap": 7,
    "cornerRadius": 14,
    "font": "Segoe UI Variable Text",
    "uppercaseLabels": false
  },

  "motion": {
    "open": "spring",
    "switch": "stagger",
    "durationMs": 320,
    "hoverMs": 120
  },

  "sounds": {
    "pack": "glass",
    "volume": 0.8,
    "tick": "sounds/tick.wav",
    "switch": "sounds/switch.wav",
    "open": "sounds/open.wav",
    "select": "sounds/select.wav"
  },

  "options": {
    "anyName": "any text"
  }
}
```

Setting names are not case-sensitive (`textMuted` and `TextMuted` both work), and unknown settings are ignored.

### About the template: name, id, author

| Setting | What it does | Default |
|---|---|---|
| `name` | Shown on the template card and in the Home template picker. | The folder name |
| `id` | The template's unique name inside OG Converter. Lower-case letters, digits and dashes. OG Converter tidies whatever you write: `"My Cool Theme!"` becomes `my-cool-theme`, and it's cut to 48 characters. The id is remembered in settings, so **don't change it** after people install your template, or they lose their selection. | Made from `name` |
| `author` | Shown under the name on the card: *Community · Your Name*. | empty |
| `description` | Shown as a tooltip when you hover the card. | empty |
| `version` | For your own bookkeeping. Not shown yet. | `"1.0"` |
| `schemaVersion` | Template format version. Leave it at `1`. | `1` |

Rules about ids:
- The built-in ids (`og-classic`, `aqua`, `graphite`, `orbit`, `neon-night`) are reserved. If you use one, OG Converter renames yours to `community-<id>`.
- If two installed templates have the same id, the second one is skipped and a warning appears under the template cards.

### `renderer`: which wheel design

| Value | Design | Uses `shape.gap` / `shape.cornerRadius`? |
|---|---|---|
| `ring` | One smooth ring, thin dividers, glowing arc on the selected option. | No |
| `glass` | Separate rounded tiles with gaps (macOS style); the selected tile fills with the accent color. | **Yes** |
| `orbit` | Round bubbles on a circular path; the hovered bubble grows like the macOS Dock. | No |
| `neon` | Dark ring with glowing outlines, tick marks around the edge, mono-spaced CAPITAL labels. | No |
| *a plugin's id* | Whatever the plugin draws (see Part 2). | Up to the plugin |

Default: `ring`. If the renderer you name isn't installed (for example a plugin that's missing), the template still loads but uses `ring`, and a warning is shown under the cards.

### `wheel`: the wheel's colors

| Setting | Where you see it | Default |
|---|---|---|
| `ring` | The main surface of the wheel (ring, tiles, disc). | `#F515171C` |
| `hub` | The circle in the middle (shows the file name and the hovered option). | `#1B1D23` |
| `text` | Option labels and the title in the middle. | `#ECEDEF` |
| `textMuted` | Icons, and the description of the hovered option. | `#A1A6B0` |
| `textFaint` | Hints such as "Choose a format", and quiet details. | `#7A7F8A` |
| `edge` | Thin outline around the wheel and hub. | `#30FFFFFF` |
| `divider` | Lines between options (`ring`, `neon`) and the dotted path (`orbit`). | `#16FFFFFF` |
| `shadow` | The soft shadow under the wheel. Its alpha (the first two hex digits of an 8-digit color) controls how dark the shadow is; `#00000000` turns it off. | `#70000000` |
| `convert` | Accent color of the **Convert** wheel (Shift). | `#FF6B35` |
| `tools` | Accent color of the **Tools** wheel (Ctrl). | `#3B9EFF` |
| `actions` | Accent color of the **Actions** wheel (Alt). | `#A78BFA` |
| `onAccent` | Text and icon color on a filled, selected option (`glass`, `orbit`). If you leave it out, OG Converter picks white or near-black, whichever is easier to read on the accent. | automatic |

The accent colors also color the key caps in the hint bar under the wheel, the icons on the Home cards, and the outline of the hub. When you switch wheels (Shift → Ctrl), the accent smoothly blends from one to the other.

How a color decides between light and dark styling: if `ring` is a light color, renderers treat the wheel as a light wheel (stronger shine, dark text expectations). So for a light wheel, also set `text`/`textMuted`/`textFaint` to dark colors.

### `app`: the colors of the rest of the app

These color the main window (Home, Settings, About), the dialogs (Compress, Protect, Split…), and the pop-up notifications. Everything is optional: whatever you leave out comes from `base`.

| Setting | Where you see it | Dark default | Light default |
|---|---|---|---|
| `base` | `"dark"` or `"light"`: fills in every color you don't set. | | |
| `window` | Window background. | `#0D0E12` | `#ECECEF` |
| `surface` | Cards (the big rounded boxes). | `#15171C` | `#FFFFFF` |
| `surfaceRaised` | Buttons, drop-downs, chips. | `#1C1E24` | `#F4F4F6` |
| `surfaceHover` | Buttons and tabs under the mouse; the selected tab. | `#23262D` | `#EAEAED` |
| `surfacePressed` | Buttons while pressed. | `#2A2D35` | `#DFDFE3` |
| `input` | Text boxes. | `#101216` | `#FFFFFF` |
| `border` | Card outlines. | `#25282F` | `#DCDCE1` |
| `borderStrong` | Button and input outlines, dashed drop zone. Scrollbars are derived from it. | `#33363F` | `#C6C6CD` |
| `divider` | Lines between rows in Settings. | `#1F2128` | `#E6E6EA` |
| `textPrimary` | Main text. | `#ECEDEF` | `#1D1D1F` |
| `textSecondary` | Descriptions. | `#A1A6B0` | `#55555C` |
| `textTertiary` | Faint hints, timestamps. | `#6C717C` | `#8A8A90` |
| `accent` | Main buttons (Install, Browse files), toggles, the selected template's outline, the OG logo, progress bars. | the wheel's `convert` color | same |
| `success` | "Ready" badges, finished jobs. | `#3DD68C` | `#1E9E55` |
| `warning` | Warnings ("Not found"). | `#F5B83D` | `#B7791F` |
| `danger` | Errors, failed jobs. | `#F2555A` | `#D93A3F` |
| `cardRadius` | Corner rounding of cards, 0–24. | `10` | `10` |
| `controlRadius` | Corner rounding of buttons and inputs, 0–16. | `6` | `6` |

You don't set these yourself; OG Converter works them out from the colors above:
- **Hover and pressed shades** of the accent button (slightly lighter or darker accent).
- **Soft tinted backgrounds** behind icons, badges and the install banner (your accent, `success`, `warning`, `danger` and the three wheel accents at about 12% strength).
- **Text on accent buttons** (white, or near-black on very light accents such as yellow or lime).
- **Hover tints, scrollbars, menus, tooltips and the drop-zone fill**, based on `base`.
- **The OG logo gradient**, from your accent.

> Choose `base` to match your `window` color. A dark `window` with `"base": "light"` leaves you with dark text on a dark background wherever you didn't set a color.

### `shape`: hub size, gaps, rounding, font

| Setting | What it does | Allowed | Default |
|---|---|---|---|
| `innerRatio` | Size of the middle circle compared to the whole wheel. `0.3` is a small hub with big options; `0.5` is a big hub with a thin ring. | 0.25 – 0.6 (other values are clamped) | `0.404` |
| `gap` | Space between tiles, in pixels at 100% wheel size. **`glass` only.** `0` means "use the default" (6), so the smallest real gap is about 2. | 0 – 24 | `0` → 6 |
| `cornerRadius` | Rounding of each tile's corners, in pixels at 100% size. **`glass` only.** `0` means "use the default" (12). | 0 – 40 | `0` → 12 |
| `font` | Font of the option labels, for example `"Bahnschrift"` or `"Cascadia Mono, Consolas"` (a comma-separated list means "use the first one that's installed"). Only fonts installed on the user's PC work, so stick to fonts that ship with Windows. | any font name | Segoe UI Variable (`neon`: Cascadia Mono) |
| `uppercaseLabels` | `true` writes option labels in CAPITALS. | `true` / `false` | `false` |

The wheel size itself is the user's choice (Settings › Wheel › Size, 40–135%), and every renderer scales with it. On small sizes (below 55%) the wheel switches to a compact mode that shows short labels such as "PNG" or just icons, and the middle shows one line.

### `motion`: animations

| Setting | What it does | Allowed | Default |
|---|---|---|---|
| `open` | How the wheel appears. | see below | `pop` |
| `switch` | How the options change when you switch wheels while it's open (for example Shift, then Ctrl). | see below | `stagger` |
| `durationMs` | Length of the opening animation in milliseconds. Switching takes 80% of this. | 60 – 1200 | `260` |
| `hoverMs` | How quickly the highlight follows your mouse. Lower is snappier, higher is floatier. | 20 – 600 | `110` |

**Opening styles (`open`):**

| Value | What you see |
|---|---|
| `pop` | The wheel grows from 86% with a small overshoot and fades in; options pop in one after another, clockwise from the top. |
| `spring` | Bouncy: the wheel springs up from 55% and wobbles into place; options bounce in one after another. |
| `spin` | The wheel turns into place from 70° back while fading in; options appear one after another. |
| `bloom` | The wheel opens smoothly from 62% like a flower; options fade and grow in one after another. |
| `fade` | The whole wheel simply fades in. |
| `none` (or anything else) | Appears instantly. |

**Switching styles (`switch`):**

| Value | What you see |
|---|---|
| `stagger` | The new options pop in one after another. |
| `spin` | The options turn into place from 36° back while fading in. |
| `fade` | The new options fade in. |
| `none` (or anything else) | Instant. |

Good combinations: quick and crisp `pop` + `stagger` at 220–260 ms; playful `spring` + `stagger` at 300–360 ms; dramatic `spin` + `spin` at 300 ms; calm `bloom` + `fade` at 340–420 ms. Over 450 ms starts to feel slow for something you use many times a day.

Users who turn on **Settings › Advanced › Reduce motion** see no animations at all, whatever the template says.

### `sounds`: sound effects

The wheel makes four sounds:

| Sound | When it plays |
|---|---|
| `tick` | The mouse moves onto a different option. (At most about 40 per second, so a fast sweep doesn't buzz.) |
| `switch` | You switch wheels while one is open (for example Shift, then Ctrl). |
| `open` | The wheel appears. Also plays when someone selects your template in Settings, as a preview. |
| `select` | A file is dropped on an option. |

**Option A: a built-in sound pack.** Set `pack` to one of these:

| `pack` | Character |
|---|---|
| `click` | Dry, short mechanical clicks (the original OG sound). |
| `glass` | Bright, slightly ringing taps and pops, in the spirit of macOS. |
| `bubble` | Rising "bloop" sounds. |
| `synth` | Retro chiptune blips and short arpeggios. |
| `soft` | Low, muted, felt-like thocks. |
| `none` | Silent. |

`volume` (0 to 1, default `1`) makes the pack quieter. It only affects the built-in packs.

**Option B: your own sounds.** Put `.wav` files in your template folder (a `sounds` subfolder is tidy) and point to them:

```jsonc
"sounds": {
  "pack": "soft",                 // used for any sound you don't replace
  "tick": "sounds/tick.wav",
  "switch": "sounds/switch.wav",
  "open": "sounds/open.wav",
  "select": "sounds/select.wav"
}
```

You can replace one, some or all four. Rules for your own sounds:

- **Format:** `.wav`, uncompressed PCM. 16-bit, 44.1 kHz or 48 kHz, mono or stereo all work. MP3, OGG and compressed WAV files won't play; convert them first, for example with FFmpeg: `ffmpeg -i in.mp3 -ac 1 -ar 44100 -c:a pcm_s16le tick.wav`, or in Audacity: *Export › WAV (16-bit PCM)*.
- **Size:** up to 2 MB per file. Keep them short: about 10–40 ms for `tick`, under 150 ms for `switch` and `select`, under 300 ms for `open`.
- **Volume:** `volume` doesn't change your own files, so make them the loudness you want. Aim noticeably quieter than system sounds; you'll hear `tick` a lot.
- **One at a time:** a new sound stops the one still playing. Long sounds get cut off.
- **Location:** paths are relative to the folder that contains `template.json`, and must stay inside it. `"../other.wav"` and full paths like `"C:\\sounds\\a.wav"` are ignored, for safety.
- If a file is missing or can't be read, that sound falls back to the `pack`.

Users can turn all sounds off with **Settings › Wheel › Sound feedback**.

### `options`: settings for plugins

`options` is a list of name/value pairs that **plugins** read (see Part 2). The built-in renderers don't use it. **Every value must be in quotes**, even numbers:

```jsonc
"options": { "petalWidth": "0.5", "showDots": "1" }      // ✔ works
"options": { "petalWidth": 0.5 }                          // ✘ the whole template fails to load
```

## 5. Writing colors

| Format | Example | Meaning |
|---|---|---|
| `#RRGGBB` | `#FF8A5B` | Red, green, blue in hex (00–FF each). Fully opaque. |
| `#AARRGGBB` | `#80FF8A5B` | **AA first** is opacity: `FF` = solid, `80` = half, `00` = invisible. |
| `#RGB` / `#ARGB` | `#F85` | Short forms. |
| Color names | `"White"`, `"Black"`, `"Transparent"`, `"Tomato"` | Any WPF/.NET named color. |

> Watch out: CSS writes opacity **last** (`#RRGGBBAA`); OG Converter, like Windows, writes it **first** (`#AARRGGBB`).

If a color can't be read, OG Converter quietly uses the default for that setting. If something looks unchanged, check the spelling of the color.

Opacity is useful for `ring` (a slightly see-through wheel, for example `#F2…` ≈ 95%), `edge`/`divider` (soft lines, for example `#20FFFFFF`), and `shadow`.

## 6. Several templates in one file

A `template.json` can hold a list of templates, for example light and dark versions:

```jsonc
[
  { "id": "sunrise-light", "name": "Sunrise Light", "renderer": "glass", "app": { "base": "light" } },
  { "id": "sunrise-dark",  "name": "Sunrise Dark",  "renderer": "glass", "app": { "base": "dark" } }
]
```

Each one gets its own card.

## 7. Share your template

1. Put `template.json` and any sound files in one folder:
   ```
   Sunrise/
   ├── template.json
   └── sounds/
       ├── tick.wav
       └── open.wav
   ```
2. Zip the folder (right-click › *Compress to › ZIP file*).
3. Optionally rename `Sunrise.zip` to `Sunrise.ogtemplate`. It's the same file; the name just tells people what it is.
4. Share it. Others install it with **Add template…** or by dragging it onto the template cards.

Zips with everything at the top level and zips with a single folder inside both work. Installing a template whose `id` is already installed replaces the old copy, which is how people update. (If the old copy's folder held several templates, they're all replaced by the new package.)

---

# Part 2: A renderer plugin (code)

A **renderer** is the code that draws the wheel. A **plugin** is a `.dll` containing one or more renderers. Your template then says `"renderer": "your-id"`, and OG Converter calls your code to draw every frame. You decide everything about the look; OG Converter still handles the mouse, animation timing, sounds, sizes and colors.

The full example below, **Halo**, draws a dark disc with a thin track, a rounded highlight under the hovered option, and small dots on the rim:

```
           ·        ·
      Protect     Cover Image
   ·                        ·
 Remove     ( Split /    [Split /]   ← highlighted option
 Metadata   ( Extract )  [Extract]
   ·                        ·
     Compress       Merge PDFs
              Rotate 90°
```

The sample in the repository, [`samples/WheelPluginSample`](../samples/WheelPluginSample) ("Petals"), works the same way.

## 8. What you need

1. **Windows 10 or 11.**
2. **The .NET 10 SDK.** Download it from <https://dotnet.microsoft.com/download> and install it. Check it works in a terminal (PowerShell):
   ```powershell
   dotnet --version     # should print 10.x
   ```
3. **The OG Converter source code**, built once. Your plugin compiles against OG Converter's public drawing API, which lives in `og-converter.dll`. The released `.exe` is a single bundled file without a separate `.dll`, so build it from source:
   ```powershell
   git clone <OG Converter repository URL> og-converter
   cd og-converter
   dotnet build -c Release
   ```
   This creates `bin\Release\net10.0-windows10.0.19041.0\og-converter.dll`. (You can also just download the source as a zip and build it.)
4. **An editor.** [Visual Studio 2022+](https://visualstudio.microsoft.com/) (Community edition is free; install the *.NET desktop development* workload) or [VS Code](https://code.visualstudio.com/) with the C# Dev Kit extension.

## 9. Create the project

In a terminal, next to (not inside) the `og-converter` folder:

```powershell
dotnet new classlib -n HaloWheel
cd HaloWheel
del Class1.cs
```

Open `HaloWheel.csproj` and replace its contents with this. Fix the `HintPath` so it points to the `og-converter.dll` you built:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Must match OG Converter -->
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="og-converter">
      <HintPath>..\og-converter\bin\Release\net10.0-windows10.0.19041.0\og-converter.dll</HintPath>
      <!-- Compile against it, but don't copy it: OG Converter provides it at run time -->
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <!-- Ships the template next to the .dll so they're installed together -->
    <None Include="template.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

If your project lives **inside** the OG Converter repository (like `samples/WheelPluginSample`), you can use a project reference instead of the `HintPath`:

```xml
<ProjectReference Include="..\..\og-converter.csproj" Private="false" ExcludeAssets="runtime" />
```

Now add the template that uses your renderer. Create `template.json` in the project folder:

```json
{
  "id": "halo",
  "name": "Halo",
  "author": "Your Name",
  "description": "A thin halo with rounded highlights.",
  "renderer": "halo",
  "wheel": {
    "ring": "#F0111827",
    "hub": "#0B1220",
    "text": "#E5E7EB",
    "textMuted": "#9CA3AF",
    "textFaint": "#6B7280",
    "edge": "#33FFFFFF",
    "divider": "#22FFFFFF",
    "shadow": "#80000000",
    "convert": "#F59E0B",
    "tools": "#10B981",
    "actions": "#6366F1"
  },
  "app": { "base": "dark", "accent": "#F59E0B" },
  "motion": { "open": "pop", "switch": "stagger", "durationMs": 260 },
  "sounds": { "pack": "soft" },
  "options": { "trackWidth": "3", "showDots": "1" }
}
```

`"renderer": "halo"` must match the `Id` your code returns (next section). Everything from Part 1 (colors, motion, sounds, app colors) works the same with a plugin.

## 10. How the wheel is laid out

Before drawing, it helps to know the coordinate system:

```
                     top: angle -90° (option 0 starts here)
                              │
          ┌───────────────────┼───────────────────┐  ← drawing area: a square,
          │               .-~~~~~-.               │    460 × 460 at 100% size
          │           .-~  outer   ~-.            │    (460 × Scale in general)
          │         /   .-~~~~~~~-.   \           │
 180° ────┼────────|   (  inner  ●  )  |──────────┼──── 0° (right)
          │         \   '-._____.-'   /           │    ● = Center
          │           '-._        _.-'            │
          │               '~~~~~~'                │
          └───────────────────┼───────────────────┘
                              │
                          90° (down)
```

- **Units** are WPF pixels (1/96 inch; Windows scales them for high-DPI screens).
- **`Center`** is the middle of the square.
- **`OuterRadius`** is `188 × Scale`. **`InnerRadius`** is `OuterRadius × innerRatio`. The options go in the ring between them, and the hub goes inside `InnerRadius`.
- There is about `42 × Scale` px of free space outside `OuterRadius` for shadows, glows and "lifted" highlights. Don't draw further out than that or it gets cut off.
- **Angles** are in degrees: **0° = right**, and they increase **clockwise** (90° = down, −90° = up).
- The options are evenly spaced: each one gets `SliceAngle = 360 / Count` degrees. **Option 0 starts at the top** and they continue clockwise. `SliceStart(i)` and `SliceMid(i)` give you the angles, already including the opening animation's rotation.
- **`Polar(radius, angle)`** turns "this far from the center, at this angle" into a point. You'll use it constantly.

## 11. Build the renderer step by step

Create `HaloRenderer.cs` in the project folder. We'll build it in steps; the complete file is at the end of this section.

### Step 1: the skeleton

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OgConverter.Wheel;

namespace HaloWheel
{
    public sealed class HaloRenderer : IWheelRenderer
    {
        public string Id => "halo";            // matches "renderer" in template.json
        public string DisplayName => "Halo";   // used if someone installs the .dll without template.json

        public void Render(DrawingContext dc, WheelFrame f)
        {
            // drawing goes here
        }
    }
}
```

The class must be **`public`**, must **not be `abstract`**, and needs a **public constructor without parameters** (the default one is fine). OG Converter finds it automatically.

`Render` receives:
- **`dc`**, a WPF [`DrawingContext`](https://learn.microsoft.com/dotnet/api/system.windows.media.drawingcontext): you draw with `dc.DrawEllipse`, `dc.DrawGeometry`, `dc.DrawText`, `dc.DrawLine`, `dc.DrawRoundedRectangle`, `dc.PushTransform`/`dc.Pop`, and so on.
- **`f`**, the `WheelFrame`: everything about this moment (sizes, options, hover state, colors, animation). See the [API reference](#13-api-reference).

### Step 2: background

```csharp
var p = f.Palette;          // the template's wheel colors, ready to use
double s = f.Scale;         // multiply every pixel size by this
Point c = f.Center;
double R = f.OuterRadius;
double r = f.InnerRadius;

WheelPainter.DrawShadow(dc, c, R, 24 * s, 6 * s, p.Shadow);   // soft shadow, 6 px lower
dc.DrawEllipse(p.RingBrush, p.EdgePen, c, R, R);              // the disc, with a thin outline

double mid = (r + R) / 2.0;                                   // halfway across the ring
dc.DrawEllipse(null, WheelPainter.Pen(p.Divider, 3 * s), c, mid, mid);   // the "halo" track
```

**Always multiply sizes by `s`**, so your wheel looks right at every size the user picks.

### Step 3: the hover highlight

`f.HoverOf(i)` is a number from **0** (not hovered) to **1** (hovered) that **animates smoothly** when the mouse moves. Use it instead of `f.HoveredIndex` and your highlight glides for free:

```csharp
for (int i = 0; i < f.Count; i++)
{
    double h = f.HoverOf(i);
    if (h < 0.01) continue;                                   // nothing to draw

    Color fill = ColorUtil.WithAlpha(f.Accent, 0.9 * h);      // fades in with h
    WheelPainter.FillRoundedSector(dc, WheelPainter.Solid(fill), c,
        r + 6 * s,                    // inner edge
        R - 6 * s + 4 * s * h,        // outer edge: grows 4 px as it lights up
        f.SliceStart(i), f.SliceAngle,
        6 * s,                        // gap to the neighbours
        14 * s);                      // corner rounding
}
```

`f.Accent` is the current wheel's accent color (Convert, Tools or Actions). It animates too, so when the user switches wheels your highlight changes color smoothly.

### Step 4: the options (icon and label)

```csharp
for (int i = 0; i < f.Count; i++)
{
    double h = f.HoverOf(i);
    Point at = f.Polar(mid, f.SliceMid(i));                   // center of option i

    using (WheelPainter.Item(dc, f, i, at))                   // opening animation for this option
    {
        Color text = ColorUtil.Lerp(p.Text, p.OnAccentFor(f.Accent), h);
        WheelPainter.DrawItemContent(dc, f, i, at, WheelPainter.SliceLabelWidth(f, mid),
            WheelPainter.Solid(text), WheelPainter.Solid(text));
    }
}
```

- **`WheelPainter.Item(...)`** applies the template's opening and switching animation to this option (each option fades and pops in at its own time). Draw each option inside its `using` block and the `motion` settings just work.
- **`DrawItemContent`** draws the icon and label, and automatically adapts to small wheel sizes (smaller text, or short labels and icons only). It also respects `shape.font` and `shape.uppercaseLabels`.
- **`ColorUtil.Lerp(a, b, h)`** blends from color `a` (h = 0) to `b` (h = 1). Here the text turns into the readable-on-accent color as the highlight fades in.

If you'd rather draw the content yourself, `f.Items[i].IconGlyph` is the icon (a character in the *Segoe Fluent Icons* font, available as `WheelPainter.IconFace`) and `f.LabelOf(i)` is the label.

### Step 5: the hub (middle circle)

```csharp
double hubR = r - 4 * s;
dc.DrawEllipse(p.HubBrush, WheelPainter.Pen(f.Accent, 2 * s), c, hubR, hubR);

WheelPainter.DrawHubContent(dc, f, hubR, new WheelPainter.HubStyle
{
    Title = p.TextBrush,
    Body = p.TextMutedBrush,
    Faint = p.TextFaintBrush,
    Accent = f.Accent
});
```

`DrawHubContent` shows the right thing automatically: the hovered option's icon, name and description; or the file type badge, file name and a hint; or a "Drop a file here" message.

### Step 6: read your own settings from `options`

```csharp
private static double ReadOption(WheelFrame f, string key, double fallback) =>
    f.Template.Options != null
    && f.Template.Options.TryGetValue(key, out var text)
    && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;
```

Then use it, for example `double track = ReadOption(f, "trackWidth", 3.0);`. Always have a fallback: users may leave the option out or misspell it. Use `CultureInfo.InvariantCulture` so `"0.5"` is read correctly on PCs that use a comma as the decimal separator.

### Step 7 (optional): your own hit test

OG Converter works out which option is under the mouse by angle: anything in the ring (from a little inside `InnerRadius` to 30 px outside `OuterRadius`) counts. If your design has a different shape, implement `HitTest`:

```csharp
public int? HitTest(Point position, WheelFrame f)
{
    double dx = position.X - f.Center.X, dy = position.Y - f.Center.Y;
    double dist = Math.Sqrt(dx * dx + dy * dy);
    if (dist < f.InnerRadius || dist > f.OuterRadius + 10 * f.Scale) return -1;   // nothing here
    return null;                                                                    // let OG Converter decide
}
```

Return an **option index** (0 to `Count - 1`), **`-1`** for "no option here", or **`null`** for "use the normal angle-based test". Leave `HitTest` out entirely if the default is fine.

### The complete file

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OgConverter.Wheel;

namespace HaloWheel
{
    public sealed class HaloRenderer : IWheelRenderer
    {
        public string Id => "halo";
        public string DisplayName => "Halo";

        public void Render(DrawingContext dc, WheelFrame f)
        {
            var p = f.Palette;
            double s = f.Scale;
            Point c = f.Center;
            double R = f.OuterRadius;
            double r = f.InnerRadius;

            // Settings from template.json "options" (always strings)
            double track = ReadOption(f, "trackWidth", 3.0);
            bool showDots = ReadOption(f, "showDots", 1.0) > 0;

            // 1. Background: shadow, disc, thin track
            WheelPainter.DrawShadow(dc, c, R, 24 * s, 6 * s, p.Shadow);
            dc.DrawEllipse(p.RingBrush, p.EdgePen, c, R, R);
            double mid = (r + R) / 2.0;
            dc.DrawEllipse(null, WheelPainter.Pen(p.Divider, track * s), c, mid, mid);

            // 2. Hover highlight: a rounded segment that fades in with the hover amount
            for (int i = 0; i < f.Count; i++)
            {
                double h = f.HoverOf(i);
                if (h < 0.01) continue;
                Color fill = ColorUtil.WithAlpha(f.Accent, 0.9 * h);
                WheelPainter.FillRoundedSector(dc, WheelPainter.Solid(fill), c, r + 6 * s, R - 6 * s + 4 * s * h,
                    f.SliceStart(i), f.SliceAngle, 6 * s, 14 * s);
            }

            // 3. Options: a dot on the rim, then icon + label
            for (int i = 0; i < f.Count; i++)
            {
                double h = f.HoverOf(i);
                Point at = f.Polar(mid, f.SliceMid(i));
                using (WheelPainter.Item(dc, f, i, at))
                {
                    if (showDots)
                    {
                        Point dot = f.Polar(R - 10 * s, f.SliceMid(i));
                        dc.DrawEllipse(WheelPainter.Solid(ColorUtil.Lerp(p.TextFaint, p.OnAccentFor(f.Accent), h)), null, dot, 2.5 * s, 2.5 * s);
                    }
                    Color text = ColorUtil.Lerp(p.Text, p.OnAccentFor(f.Accent), h);
                    WheelPainter.DrawItemContent(dc, f, i, at, WheelPainter.SliceLabelWidth(f, mid),
                        WheelPainter.Solid(text), WheelPainter.Solid(text));
                }
            }

            // 4. Hub
            double hubR = r - 4 * s;
            dc.DrawEllipse(p.HubBrush, WheelPainter.Pen(f.Accent, 2 * s), c, hubR, hubR);
            WheelPainter.DrawHubContent(dc, f, hubR, new WheelPainter.HubStyle
            {
                Title = p.TextBrush,
                Body = p.TextMutedBrush,
                Faint = p.TextFaintBrush,
                Accent = f.Accent
            });
        }

        // Optional: only the ring counts (the default also accepts a little outside it)
        public int? HitTest(Point position, WheelFrame f)
        {
            double dx = position.X - f.Center.X, dy = position.Y - f.Center.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < f.InnerRadius || dist > f.OuterRadius + 10 * f.Scale) return -1;
            return null; // inside the ring: let OG Converter work out which slice
        }

        private static double ReadOption(WheelFrame f, string key, double fallback) =>
            f.Template.Options != null
            && f.Template.Options.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
    }
}
```

## 12. Build, install, try it

1. **Build:**
   ```powershell
   dotnet build -c Release
   ```
   You get `bin\Release\net10.0-windows10.0.19041.0\` containing `HaloWheel.dll` and `template.json`.
2. **Install:** in OG Converter, **Settings › Wheel templates › Add template…**, and pick **`HaloWheel.dll`**. The `template.json` beside it (and any sounds it uses) comes along automatically. OG Converter warns you that plugins run code; choose **Yes**. Or drag the whole output folder onto the template cards.
3. **Try it:** your template appears with a **Plugin** badge and becomes active. Hover its card to see the opening animation, press **▶** in the title bar to open the wheel, and drag a real file from Explorer and tap Shift / Ctrl / Alt.
4. **Change and repeat:** edit your code, build again, and add the `.dll` again. The old copy is replaced. OG Converter loads plugins from memory, so it never locks your file and you don't need to restart it.

To share your plugin, zip the `.dll`, `template.json` and any sound files together (see [Share your template](#7-share-your-template)).

**A `.dll` without a `template.json`** also works: OG Converter creates one template per renderer it finds, named after `DisplayName`, using the default colors.

**One plugin, many renderers:** put several `IWheelRenderer` classes in one project, each with its own `Id`, and list several templates in `template.json` (an array).

## 13. API reference

Everything is in the `OgConverter.Wheel` namespace (plus `WheelItem`/`WheelMode` in `OgConverter.Models`, and `WheelTemplate` and its parts in `OgConverter.Models`).

### `IWheelRenderer`

| Member | Description |
|---|---|
| `string Id` | Unique id, referenced by `"renderer"`. Compared without case. `ring`, `glass`, `orbit`, `neon` are reserved. |
| `string DisplayName` | Name used when there's no `template.json`. |
| `void Render(DrawingContext dc, WheelFrame frame)` | Draw one frame. |
| `int? HitTest(Point position, WheelFrame frame)` | Optional. Option index, `-1` for none, or `null` for the default test. |

### `WheelFrame`: everything about the current frame

**Size and position**

| Member | Description |
|---|---|
| `Point Center` | Middle of the drawing area. |
| `double OuterRadius` | Outer edge of the ring: `188 × Scale`. |
| `double InnerRadius` | Edge of the hub: `OuterRadius × shape.innerRatio`. |
| `double Scale` | The user's wheel size, about 0.4–1.4. Multiply pixel sizes by it. |
| `double TextScale` | Multiplier for font sizes. Doesn't shrink as far as `Scale`, so text stays readable. |
| `WheelDensity Density` | `Full` (Scale ≥ 0.8), `Compact` (≥ 0.55: smaller labels, the hub drops extra lines), `Micro` (smaller: short labels or icons only, one line in the hub). |
| `double PixelsPerDip` | Screen scaling; pass it to `FormattedText` if you create text yourself (the `WheelPainter` text helpers take it as a parameter). |

**Options and hover**

| Member | Description |
|---|---|
| `IReadOnlyList<WheelItem> Items` | The options, clockwise from the top. |
| `int Count` | Number of options (can be 0). |
| `int HoveredIndex` | The option under the mouse, or `-1`. Changes instantly. |
| `double HoverOf(int i)` | Animated hover amount of option `i`, 0–1. **Use this for highlights.** |
| `IReadOnlyList<double> Hover` | All hover amounts. |
| `string LabelOf(int i)` | Option `i`'s label, in capitals if `shape.uppercaseLabels` is on. |

`WheelItem` has `Title`, `Description`, `IconGlyph` (a *Segoe Fluent Icons* character), `Id`, `Extension`, `Mode`, `Category`.

**Angles**

| Member | Description |
|---|---|
| `double SliceAngle` | Degrees per option: `360 / Count`. |
| `double SliceStart(int i)` | Start angle of option `i` (option 0 starts at −90°, the top). Includes `Rotation`. |
| `double SliceMid(int i)` | Middle angle of option `i`. |
| `double Rotation` | Current rotation from the `spin` animations (already part of `SliceStart`/`SliceMid`). Use it to rotate decorations along with the wheel. |
| `Point Polar(double radius, double angleDeg)` | The point `radius` away from `Center` at `angleDeg`. |

**Animation**

| Member | Description |
|---|---|
| `double ItemAlpha(int i)` | Option `i`'s opacity during the opening/switching animation, 0–1. |
| `double ItemScale(int i)` | Option `i`'s size during the animation (can briefly exceed 1 with `pop`/`spring`). |

You rarely need these directly: `WheelPainter.Item(dc, f, i, center)` applies both. The whole wheel's grow/fade effect is applied by OG Converter around your `Render`; you don't have to do anything for it.

**Colors and template**

| Member | Description |
|---|---|
| `Color Accent` | The current wheel's accent color (animates when switching wheels). |
| `WheelMode Mode` | `Convert`, `Tools` or `Actions`. |
| `WheelPalette Palette` | The template's `wheel` colors (see below). |
| `WheelTemplate Template` | The whole template: `Template.Shape`, `Template.Motion`, `Template.Options`, `Template.Name`, … |

**Hub text** (what `DrawHubContent` shows; available if you draw the hub yourself)

| Member | Description |
|---|---|
| `string CenterBadge` | File type, such as `PDF` or `FOLDER`. Empty when no file is loaded. |
| `string CenterSubtitle` | File name (or a hint when no file is loaded). |
| `string CenterTitle` | Instruction such as "Choose a format", or "Drop a file here". |
| `string CenterGlyph` | An icon for the file type (Segoe Fluent Icons). |

### `WheelPalette`: the template's wheel colors

| Member | Description |
|---|---|
| `Color Ring, Hub, Text, TextMuted, TextFaint, Edge, Divider, Shadow` | From `wheel`. |
| `Color Convert, Tools, Actions` | The three accents. `AccentFor(mode)` picks one. |
| `Color OnAccentFor(Color accent)` | Readable text color on top of `accent` (the template's `onAccent`, or white/near-black automatically). |
| `Brush RingBrush, HubBrush, TextBrush, TextMutedBrush, TextFaintBrush` | Ready-made brushes. |
| `Pen EdgePen, DividerPen` | Ready-made 1 px pens. |
| `bool IsLight` | `true` when `ring` is a light color. Use it to adjust shine and shadows. |

### `ColorUtil`: color helpers

| Method | Description |
|---|---|
| `Parse(string, Color fallback)` / `TryParse(string, out Color)` | Read a color written like in `template.json`. |
| `WithAlpha(Color, byte)` / `WithAlpha(Color, double 0–1)` | Same color, different opacity. |
| `Lerp(Color a, Color b, double t)` | Blend from `a` (t = 0) to `b` (t = 1), opacity included. |
| `Mix(Color c, Color toward, double t)` | Blend toward another color but keep `c`'s opacity. Handy for lighter/darker shades: `Mix(c, Colors.White, 0.2)`. |
| `Luminance(Color)` | Brightness, 0 (black) to 1 (white). |
| `ToHex(Color)` | `#RRGGBB` or `#AARRGGBB`. |

### `WheelPainter`: drawing helpers

**Brushes, pens, fonts**

| Method | Description |
|---|---|
| `Solid(Color)` | A cached, frozen brush. Safe and cheap to call every frame. |
| `Pen(Color, double thickness, bool round = false)` | A frozen pen; `round` gives round line ends and joins. |
| `Frozen(freezable)` | Freezes a brush/pen/geometry you made yourself (makes drawing faster). |
| `Face(string? family, FontWeight)` | A cached `Typeface`. `null` family = Segoe UI Variable. |
| `IconFace`, `IconFamily` | The Segoe Fluent Icons font, for `WheelItem.IconGlyph`. |
| `UiFont`, `DisplayFont` | Font name lists for body text and headings. |

**Text**

| Method | Description |
|---|---|
| `Text(text, face, size, brush, pixelsPerDip)` | A simple one-line `FormattedText`. |
| `Centered(text, face, size, brush, maxWidth, maxLines, pixelsPerDip)` | Centered, wrapped, with "…" after `maxLines`. Draw it at `x = centerX - maxWidth / 2`. |
| `DrawStack(dc, cx, cy, lines)` | Draws several texts stacked and centered on a point. |

**Shapes**

| Method | Description |
|---|---|
| `Arc(center, r, startDeg, sweepDeg)` | An arc to stroke with a pen. |
| `Annulus(center, rInner, rOuter)` | A full ring (a donut). |
| `AnnularSector(center, rInner, rOuter, startDeg, sweepDeg)` | One slice of a ring. |
| `InsetSector(center, rInner, rOuter, startDeg, sweepDeg, sideInset, radialInset)` | A slice shrunk inward, with straight parallel gaps to its neighbours. |
| `RoundedSector(center, rInner, rOuter, startDeg, sweepDeg, gap, corner)` | A slice with rounded corners and an even gap (cached). |
| `FillRoundedSector(dc, brush, …same…)` | Fills a `RoundedSector`. |
| `DrawShadow(dc, center, radius, spread, offsetY, color)` | Soft round shadow. |

**Wheel content**

| Method | Description |
|---|---|
| `Item(dc, f, i, itemCenter)` | Applies option `i`'s opening animation. Use with `using`. |
| `DrawItemContent(dc, f, i, at, maxWidth, glyphBrush, labelBrush, labelFace = null, sizeBoost = 1)` | Icon + label for option `i`, centered on `at`, adapted to the wheel size. `sizeBoost` makes the text bigger or smaller. |
| `SliceLabelWidth(f, radius)` | A label width that fits one slice at that radius. |
| `DrawHubContent(dc, f, hubRadius, HubStyle)` | The text in the middle (hovered option, file, or message). |
| `HubStyle` | `Title`, `Body`, `Faint` (brushes), `Accent` (color), `TitleFace`, `BodyFace`, `BadgeFace` (optional fonts), `UppercaseTitle`. |

## 14. Rules, performance and safety

**How OG Converter loads plugins**
- Every `.dll` in a template's folder is loaded. Each public, non-abstract class with a public parameterless constructor that implements `IWheelRenderer` is registered.
- Plugins are read into memory and loaded into a separate, unloadable space, so the files are never locked: you can replace or delete them while OG Converter runs (then press **Reload**).
- A plugin `.dll` can be at most **20 MB**.
- Build against the **same OG Converter version** your users run. If the API changed in between, the template shows an error under the cards ("built against an incompatible version").
- When you add a `.dll` with **Add template…**, only that `.dll`, the `template.json` beside it and the sounds it points to are copied. If your plugin needs **other `.dll` files** (NuGet packages), share it as a folder or zip containing all of them, and install that instead. Better still, avoid extra dependencies: WPF and .NET give you everything a renderer needs.

**Performance:** `Render` runs on the UI thread for **every frame** while anything moves (opening, switching, hover), about 60 times a second, and once after each change otherwise.
- Keep it simple: a few dozen shapes and texts per frame is fine.
- Use `WheelPainter.Solid(color)` instead of `new SolidColorBrush(...)` in loops, and freeze brushes/pens/geometries you create yourself (`WheelPainter.Frozen(...)`).
- Avoid `BlurEffect`, `DropShadowEffect`, bitmaps and file or network access inside `Render`. For glows, draw a few strokes with increasing width and decreasing opacity (that's how the `neon` renderer does it).
- Compute expensive things once and keep them in a field. If they depend on size or colors, remember the inputs and recompute only when those change.
- Don't keep `WheelFrame` between calls: OG Converter reuses the same object for every frame.

**Errors:** if `Render` or `HitTest` throws an exception, OG Converter catches it, writes it to the console once, and draws the wheel with the built-in `ring` renderer instead. Your plugin can't crash the app, but it's still worth testing.

**Safety:** a plugin is a normal program with the same access to files as OG Converter. OG Converter asks the user before installing one. Don't do anything in a renderer except draw: no file, registry, network or process access. People will only install plugins they trust, so keep yours simple and open source if you can.

---

## 15. Testing and debugging

**See your template in every state, as images.** OG Converter can render every screen and every installed template to PNG files. Point it at a test templates folder so your real setup isn't touched:

```powershell
# A test folder with just your template in it
mkdir C:\temp\og-templates\halo
copy bin\Release\net10.0-windows10.0.19041.0\* C:\temp\og-templates\halo\

$env:OGCONVERTER_TEMPLATES_DIR = "C:\temp\og-templates"
& "path\to\og-converter.exe" --snapshot C:\temp\og-shots
```

Look at the `tpl_<your-id>_*.png` files in `C:\temp\og-shots`: Home and Settings in your colors, each of the three wheels with an option highlighted, the wheel over a light background, and the smallest (40%) size with and without the hint bar.

`OGCONVERTER_TEMPLATES_DIR` works for normal runs too: start OG Converter from that same PowerShell window and it only sees the templates in your test folder.

**See error messages.** Start OG Converter from a terminal (PowerShell or Windows Terminal) instead of the Start menu. Errors from your renderer then appear there, for example:

```
[OG Converter] Wheel renderer "halo" failed: System.NullReferenceException: ...
   at HaloWheel.HaloRenderer.Render(...) in C:\...\HaloRenderer.cs:line 42
```

Template loading problems (bad JSON, missing renderer, duplicate id) appear in orange under the template cards in **Settings › Wheel templates**.

**Step through your code** with the Visual Studio debugger:

1. Build your plugin in Debug: `dotnet build` (this also creates `HaloWheel.pdb`, which the debugger needs).
2. Point OG Converter at your build output. The templates folder must contain **one subfolder per template**, and `bin\Debug` already has exactly that (`net10.0-windows10.0.19041.0\` with your `.dll`, `.pdb` and `template.json`):
   ```powershell
   $env:OGCONVERTER_TEMPLATES_DIR = "C:\path\to\HaloWheel\bin\Debug"
   & "path\to\og-converter.exe"
   ```
3. In Visual Studio, open your plugin project, choose *Debug › Attach to Process…*, pick `og-converter.exe`, and set breakpoints in `Render`.
4. Select your template and open the wheel (▶ in the title bar). The breakpoints are hit.

After changing your code, build again and press **Reload** in Settings: the new version is picked up without restarting.

**Run OG Converter's own test suite** (from the OG Converter source folder) to check templates and the sample plugin still work:

```powershell
dotnet build samples\WheelPluginSample -c Release
$env:OGCONVERTER_PLUGIN_SAMPLE = "$PWD\samples\WheelPluginSample\bin\Release\net10.0-windows10.0.19041.0\WheelPluginSample.dll"
dotnet run --project og-converter.csproj -- --test
```

---

## 16. Troubleshooting

| Problem | Cause and fix |
|---|---|
| *"template.json isn't valid JSON (line N)"* | A typo: a missing comma or quote, or an unclosed `{`. Look at line N and the line before it. VS Code underlines these in red. |
| The whole template fails after adding `options` | Values in `options` must be strings: `"0.5"`, not `0.5`. |
| A color doesn't change | The color can't be read (typo, or CSS-style `#RRGGBBAA`), so the default is used. Write `#AARRGGBB` or `#RRGGBB`. |
| Text is unreadable after setting `"base": "light"` | Light base means dark text; set a light `window`/`surface` too, or use `"base": "dark"`. |
| *"renderer "x" isn't installed, using the ring renderer"* | The `renderer` in `template.json` doesn't match any `Id`, or the plugin `.dll` isn't in the same folder. Check spelling (case doesn't matter). |
| *"a template with id "x" is already loaded, skipped"* | Another installed template has the same `id`. Change yours, or remove the other one. |
| *"renderer id "ring" is reserved"* | Choose a different `Id` for your renderer. |
| *"…was built against an incompatible version of OG Converter"* | Rebuild your plugin against the OG Converter version you're running. |
| Plugin shows up but draws the classic ring | Your `Render` threw an exception. Start OG Converter from a terminal to see it. |
| Plugin isn't found at all | The class isn't `public`, is `abstract`, has no parameterless constructor, or doesn't implement `OgConverter.Wheel.IWheelRenderer`. |
| `gap` or `cornerRadius` do nothing | They only apply to the `glass` renderer (and to plugins that read them). |
| My `.wav` doesn't play | It isn't uncompressed PCM, is over 2 MB, is outside the template folder, or Sound feedback is off. That sound then falls back to the `pack`. |
| My sound gets cut off | A new sound stops the previous one; make it shorter. |
| Animations don't play | **Reduce motion** is on in Settings › Advanced, or `open`/`switch` is misspelled (unknown values mean "none"). |
| Changes don't show up | You edited your original file, not the installed copy. Add it again, or edit the copy in the templates folder and press **Reload**. |
| My plugin's `.dll` can't be deleted | It can: OG Converter doesn't lock plugin files. If something does, it's your editor or another program. |

---

## 17. Checklist before you share

- [ ] `name`, `author` and `description` are filled in, and the `id` is final.
- [ ] The wheel looks right in all three wheels (Shift, Ctrl, Alt): each accent stands out against `ring`, and selected text is readable.
- [ ] It looks right at **40%** and at **135%** size (Settings › Wheel › Size).
- [ ] It's visible on both light and dark desktops (try `--snapshot`: the `idle_light` image uses a light background).
- [ ] Home, Settings and a dialog (for example Compress) are readable in your `app` colors.
- [ ] Sounds are short, not too loud, and play (custom `.wav` files are 16-bit PCM).
- [ ] Animations feel quick enough for daily use (around 220–360 ms), and hover feels responsive.
- [ ] Plugins: no exceptions in the terminal, nothing besides drawing in `Render`, built against the current OG Converter, and any extra `.dll` files are included.
- [ ] The zip contains `template.json` (and the `.dll` and sounds), and installs cleanly with **Add template…** on a PC that hasn't seen it before.

Have fun, and share what you make!
