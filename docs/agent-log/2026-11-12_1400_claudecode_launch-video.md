---
date: "2026-11-12 14:00:00"
agent: "claudecode"
model: "Claude Opus 5.5"
session_type: "feature"
status: "completed"
sprint: "N/A (marketing)"
related_files:
  - ".gitignore"
  - "promotion/build.py"
  - "promotion/out/og-converter-launch-9x16.mp4"
---

# Agent Session Log: Product launch video (Shorts / Reels / LinkedIn)

## 1. Context & Objectives
- **User Goal**: An original product-launch video for OG Converter for LinkedIn, Instagram and YouTube Shorts, generated with Google Flow (gflow MCP). The YouTube video "Best SaaS Product Launch Ad Video | LangEase" (Zelios) was given as a style reference only, not to copy. Work goes in a new `promotion/` folder that is git-ignored.
- **Starting State**: `main` at c4218a9 (v1.8.0). No app code touched.

## 2. Actions Completed
- [x] Created `promotion/` and added it to `.gitignore` (new "Promotion" section at the end).
- [x] Created Flow project "OG Converter launch" (`0fa17f83-9bb3-4682-984c-3568b8210fbc`).
- [x] Generated 1 style plate (Nano Banana Pro) and 7 text-to-video clips (Veo 3.1 Fast, 8 s, 9:16, 20 credits each = 140 credits).
- [x] Assembled a 33.75 s 9:16 cut with ffmpeg: text overlays (Segoe UI), real app icon on the end card, per-clip audio with short fades.
- [x] Gates: video 33.750 s / audio 33.733 s, mean -19.5 dB, max -0.4 dB; every clip reviewed by eye on 1 fps strips (`promotion/review/`).

## 3. Files Created / Modified / Deleted
| Action | File Path | Description |
|---|---|---|
| `MODIFIED` | `.gitignore` | Ignore `promotion/` |
| `CREATED` | `promotion/prompts/b2..b7.txt` | Beat prompts (shared style block verbatim) |
| `CREATED` | `promotion/clips/b1..b7.mp4` | Raw Veo clips |
| `CREATED` | `promotion/build.py` | Re-runnable assembly: segments, trims, text, end card |
| `CREATED` | `promotion/out/og-converter-launch-9x16.mp4` | Final cut, 720x1280, H.264/AAC |

## 4. Key Decisions & Rationales
- **Original concept**: a clay-style 3D cursor mascot. Story: file chaos and upload sites, then "Meet OG Converter", the radial wheel, Convert/Tools/Actions, batch, 100% offline, end card. Only the general SaaS-launch pacing was taken from the reference.
- **One 9:16 cut** serves all three platforms. Text kept inside the vertical safe zone.
- **All text added in post**, because Veo invents garbled text. Clip ranges that rendered fake glyphs or a brand-like bird icon (b2 middle, b5 clouds reading "uplood") were trimmed out.
- **Generated audio kept** (each clip prompted for an upbeat electronic groove, no voice). The music changes at each cut. Swapping in a licensed music bed is the obvious upgrade.
- The app icon (`og-converter.ico`) is orange at every size; used as-is on the end card.

## 5. Environment notes / problems hit
- This Flow account is served the **agent-only composer** on flow.google.com: only t2i and t2v work (no i2i, no i2v), so consistency comes from a verbatim style block, not reference images. Veo 3.1 Fast only allows **8 s**; asking for 6 s makes Flow's agent stop and ask, and gflow times out.
- gflow **0.87** submits fine but cannot download from this composer. **0.97** via `uvx --from gflow-cli==0.97.0 gflow ...` works end to end.
- `gflow update` / `uv tool install --force` failed while the gflow MCP server was running and **left the uv tool install broken** (`ModuleNotFoundError: gflow_cli`). Fix with Claude Code / the MCP server closed: `uv tool install --force gflow-cli`.

## 6. Handover / Next Steps
- [ ] Repair the gflow tool install (above).
- [ ] Optional: replace the audio with one licensed music track; make 1:1 or 4:5 crops for the LinkedIn feed; upscale to 1080p.
- Rebuild after edits: `cd promotion && python -I build.py`.

---

## 7. Rebuild (v2) after user feedback
**Feedback:** v1 looked like random clips merged together: no continuity between shots, and the cartoon mascot read as made for kids. The user wants professional work, with the product looking the same in every shot.

**Decision:** drop AI video entirely and build the film as coded motion design from real product assets (`promotion/v2/`):
- `scene.html`: one HTML composition driven by `render(t)`, 1080x1920 at 30 fps, 34 s. It shares one background, one type system (Segoe UI Variable) and one enter/exit grammar across all seven scenes.
- **Wheel:** a faithful rebuild of the **Graphite** template from `GlassRenderer.cs` / `BuiltInTemplates.cs` / `RadialWheelCanvas.cs`:
  - InnerRatio 0.40, parallel-offset tile gaps, corner 12s, the exact palette and shadows.
  - Spring-open and stagger-switch maths; hint bar from `RadialWheelOverlay.xaml`; Fluent glyphs from `FormatRegistry.cs`.
- **Demo:** runs inside the real Explorer screenshot (`Assets/public/og-short-cut-convert.png`):
  - The baked-in wheel is painted out (flat #191919).
  - **Personal sidebar labels are redacted** (OneDrive account name, phone name).
- `music.py`: an original synthesized track, 120 BPM. Scene cuts sit on bar lines (every 2 s), with a riser and impact at the reveal, whooshes into cuts, and UI ticks on the key tap, drop and wheel switches.
- `render.py`: Playwright (Chrome) captures each frame and pipes it to ffmpeg (x264 CRF 16, AAC 256k).
- **Output:** `promotion/out/og-converter-launch-v2-9x16.mp4`. Video and audio both 34.000 s; mean -14.3 dB, max -0.7 dB.

**Facts shown on screen come from the README/code:** formats, the three wheels and their Home-screen descriptions, offline/no account/no telemetry, Windows 10/11 + Linux beta, GPLv3 (shown as "Free & open source").

**Open items:**
- The README demo videos (github user-attachments) return 404 without a login, so they couldn't be used. A fresh 1080p+ screen capture of the real app would be the next upgrade for S3.
- The synthesized track is a functional placeholder; a licensed track at 120 BPM drops in without retiming (replace `build/music.wav`).
- Re-render: `cd promotion/v2 && uv run --no-project --with numpy python -I music.py && uv run --no-project --with playwright python -I render.py`.

## 8. v3: new hook + Microsoft Store CTA
- **Hook (0-4 s) rebuilt to stop the scroll.** A stopwatch is on screen from frame 0 under "Converting one file online:" and races 0:00 → 4:12. Five generic web-converter cards slam in on 8th notes with screen shake: uploading 37%, watch an ad, sign in, 50 MB limit, #14 in queue. Everything gets cut away on the 2.0 downbeat. Then the payoff: "Or just tap [Shift]", the real Graphite wheel springs open, PDF is hovered, the file drops, and "README.pdf · Converted on your PC" appears.
- Audio in `music.py` to match: impact on frame 0, accelerating stopwatch ticks, a slam per card, tape stop at 1.75, near-silence, key/hover/drop ticks and a chime, then the riser into the 4.0 reveal.
- **End card:** a "Available on the Microsoft Store" badge (generic Fluent shopping-bag glyph E719, not the Microsoft logo); GitHub demoted to "Open source on github.com/...".
  - No Store URL or product ID is in the repo. The user stated the app is on the Store.
  - The official Microsoft Store badge (from Microsoft's badge generator) can replace `#s7store` if wanted.
- **Output:** `promotion/out/og-converter-launch-v3-9x16.mp4`. Video and audio both 34.000 s; mean -13.9 dB, max -0.3 dB.

## 9. v4: AI-generated soundtrack (ACE-Step 1.5)
- **Feedback:** the synthesized track sounded simple and robotic. Flow has no standalone music model, so **ACE-Step 1.5** (MIT licence, local GPU, RTX 5070 Ti) was installed into git-ignored `promotion/tools/ACE-Step-1.5` (`uv sync --python 3.12`) after the user approved running it.
- `promotion/v2/gen_music.py`: turbo DiT + 1.7B LM, PyTorch backend. Original instrumental caption, 120 BPM, A minor, 40 s, a section map in the lyrics field, 4 takes per batch → `build/takes/`.
- `promotion/v2/mix_music.py` (numpy only):
  - Measures tempo, beat phase and drop for each take, and scores fit to the edit. All 4 takes measured exactly 120.0 BPM.
  - The chosen take `b80c1011…` (seed 3982510703) is shifted 2.0 s so its drop lands on the 4.0 s reveal, keeping it on the 0.5 s grid.
  - The picture-synced SFX stem (`build/sfx.wav`, now written by `music.py`) is mixed on top, then loudnorm to -14 LUFS (measured -13.9, TP ≤ -1).
- **Output:** `promotion/out/og-converter-launch-v4-9x16.mp4` (video remuxed, frames unchanged).
- The old synthesized bed is kept as `build/music_synth.wav`.
- **More takes:** `uv run --project ../tools/ACE-Step-1.5 python gen_music.py 4`, then `uv run --no-project --with numpy python -I mix_music.py [take.flac]`, then remux or re-render.

## 10. Microsoft Store screenshots + logos
- `promotion/v2/store.html` + `store_render.py`: 5 designed 1920x1080 screenshots and 3 logos, using the same design system as the video.
  - The wheel is shared via `wheel.js`, extracted from `scene.html`.
  - The app icon is rebuilt as a vector (brand gradient #FF8A55 → #F2541B), matched against the .ico.
- **Real visuals only:** the redacted Explorer screenshot, `og-home.png`, `og-settings.png`, and the Graphite wheel rebuilt from the renderer code. Every format pair shown was checked against `FormatRegistry.cs`; GIF→MP4 was not supported and was replaced with PPTX→MP4.
- **Output in `promotion/out/store/`:**
  - `screenshots/01..05-*.png`
  - `logos/box-art-2160x2160.png`, `poster-art-1440x2160.png`, `app-tile-icon-300x300.png` (transparent)
- Re-render: `cd promotion/v2 && uv run --no-project --with playwright python -I store_render.py`.

## 11. Store trailer to spec + super hero art + tiles
- **16:9 trailer:** `scene.html?layout=landscape` (`render.py --landscape`) re-lays out the same scenes for 1920x1080, keeping key content central.
- **Re-encoded to Microsoft's MP4 trailer spec** (MSIX "screenshots and images" page, updated 2026-08-24):
  - H.264 High profile, progressive, bf=2, closed GOP of 15 (half of 30 fps), CABAC, max 50 Mbps, 4:2:0.
  - AAC-LC 48 kHz stereo at 384k; loudnorm to -16 LUFS (measured -15.8).
  - +faststart; `-use_editlist 0` (verified: 0 elst atoms, moov before mdat).
- **Output in `promotion/out/store/`:**
  - `trailer/og-converter-trailer-1920x1080.mp4` and `trailer/trailer-thumbnail-1920x1080.png`
  - `logos/super-hero-art-3840x2160.png`: required for trailers to show at the top of the listing. No text or UI per policy; wheel kept out of the bottom third.
  - `logos/tile-150x150.png` and `logos/tile-71x71.png`

## 12. Social exports (YouTube, Instagram, LinkedIn)
- `promotion/v2/export_social.py` muxes the existing portrait and landscape renders with `build/music.wav` (-14 LUFS master) and encodes for upload:
  - H.264 High, 30 fps, 2 s closed GOP, max 20 Mbps
  - AAC-LC 48 kHz 320k
  - faststart, no edit lists
- **Output in `promotion/out/social/`:**
  - `youtube/`: Shorts 1080x1920, video 1920x1080, `shorts-cover`, `thumbnail-1280x720.jpg`
  - `instagram/`: reel 1080x1920, `reel-cover` (7.55 s brand-reveal frame; fits the 3:4 grid crop)
  - `linkedin/`: 1920x1080 + vertical 1080x1920, a thumbnail and a vertical cover
- All 34 s, ~10 MB, -13.9 LUFS.
- **Note:** after editing `scene.html`, re-render (`render.py`, `render.py --landscape`) before running `export_social.py`.
