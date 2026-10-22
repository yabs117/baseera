# Baseera · بصيرة

A modular Windows companion with a calm native desktop interface. Enable just the
tools you need: local file inspection, window translation, selected app updates,
and image conversion.

[العربية](docs/README.ar.md) · [Français](docs/README.fr.md) · [Español](docs/README.es.md)

![Baseera in Arabic, right-to-left](docs/images/baseera-ar.png)

**Early preview, version 0.1.0.** This is a new project, not a certified antivirus
or a claim of 100% malware detection. Read the [capability status](docs/ROADMAP.md)
and [security scope](SECURITY.md) before relying on it.

## What works

| Tool | Current capability |
| --- | --- |
| File inspection | Files/folders, nested ZIPs, SHA-256, PE structure and script indicators. Reports skipped/encrypted/unsupported content explicitly. No execution or upload. |
| Window translation | Select a visible window, send its screenshot to Gemini on request, and display translated text over the image. Esc closes the overlay. |
| Page translation | Chrome/Edge extension replaces ordinary page text in place and restores originals. User-provided Gemini key and model. |
| App updates | Opt-in WinGet updates per package, at launch and every six hours while running. Manual per-app GitHub release URLs override automatic installation and show releases for review. |
| Image conversion | PNG, JPEG, BMP, TIFF and ordered multi-image PDF. Preserves source files. Opt-in Explorer menu and Ctrl + right-click on selected images while running. |
| Interface | Arabic RTL, English, French, Spanish, light/dark themes, vector icons, collapsible navigation and first-run feature selection. |

## Run

Requires Windows 10/11 and **.NET Desktop Runtime 10**. Download a packaged preview
from [Releases](https://github.com/yabs117/baseera/releases), extract the whole folder,
and open `Baseera.exe`. Source ZIP downloads from GitHub are not compiled applications.
All features are off until selected. Startup registration and Explorer integration
are optional settings, applied only when you save them.

For translation, enter your Gemini API key and a model ID supported by your account
in Settings. Choose a model supporting image input and structured output. No key
is bundled. Screenshots/page text are sent to Google's API only when translation
is requested. Local file inspection and conversion do not use AI or send files.

## Build and verify

Install the .NET 10 SDK on Windows. There are no third-party NuGet dependencies.

```powershell
./build.ps1 -Test -Publish
# Optional desktop integration checks and renders:
./build.ps1 -Smoke
# Browser extension protocol tests (Node 22+):
node --test extension/test/protocol.test.js
```

The executable is produced in `artifacts/Baseera-win-x64/`. This is a framework-dependent
build. `-Smoke` uses a separate local data directory and does not run updates or
register Explorer hooks. It renders the actual WPF screens and tests conversion,
PDF generation, translation-box validation, localization coverage and DPAPI storage.

## Browser extension

Open `chrome://extensions` or `edge://extensions`, enable Developer mode, choose
**Load unpacked**, and select the `extension` folder. Open an HTTP(S) page and click
the extension. Enter a Gemini key and model ID, choose the target language, then
translate. The extension key is separate from the desktop key and is kept only
for the browser session. It reads the current page after an explicit action, with
up to 300 segments / 30,000 characters per run. Inputs, editable fields, images,
iframes and closed shadow roots are excluded. New SPA content may require another run.

## Known limitations

- The scanner is bounded static inspection, not dynamic sandboxing or a full malware engine.
  Unsigned files and sensitive API references can be legitimate. RAR/7z are not supported yet.
- Vision translation depends on model output; coordinates and text can be wrong. It captures
  the visible screen region, so keep the target unobstructed. The overlay is a snapshot,
  not continuous tracking, and protected/fullscreen surfaces may not work.
- Live Gemini requests require a user key and quota. They were not exercised with a real
  API key during the initial local build. WinGet installs and Explorer hooks were not
  exercised against the developer's installed apps/files during automated tests.
- Manual update sources currently support GitHub Releases discovery, not unattended
  installation or arbitrary download-site scraping. Invalid manual URLs never fall back
  to automatic installation.
- Ctrl + right-click uses the current Explorer image selection while Baseera is running.
  The normal context menu entry lives under Windows 11's **Show more options**.
- TIFF/animated input uses only the first frame. WebP and office-document conversion are future work.

## Privacy and data

Preferences are stored under `%LOCALAPPDATA%/Baseera`. The desktop API key is encrypted
with Windows DPAPI. Conversion never overwrites an existing image output. PDF output
requires a new filename. Update logs stay local. No telemetry is built into Baseera.

Built with C#/.NET/WPF and a dependency-free Manifest V3 browser extension.
See [CONTRIBUTING](CONTRIBUTING.md), [ROADMAP](docs/ROADMAP.md), and the [MIT license](LICENSE).
