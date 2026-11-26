# Baseera 6.5.0

## Settings and language

- Settings opens a list of six categories. Each category has its own page and a route back to the list. Links from translation and disabled tools open the relevant category directly.
- A fresh installation uses English for the interface and for the translation target. Change the interface language under **Settings → Appearance and language**. Saved preferences carry over from earlier versions.
- Simplified Chinese, Traditional Chinese using Taiwan terminology, and Vietnamese are available alongside Arabic, English, French and Spanish. The same languages are available as translation targets. Arabic retains its right-to-left layout.
- The [redesigned welcome guide](USER-GUIDE.md#welcome-screen) introduces Baseera's tools, highlights the error assistant, and adapts to smaller screens while keeping Continue and the startup preference in view.
- The optional browser extension also starts in English on first use. Its interface language is changed under **Settings** in the extension popup, separately from the language used to translate a web page.

## Smart Repair and tool search

- The message field, attachments, model selector, microphone and Send button share one composer. Switch between compatible models from your configured connection for the current assistant window.
- **Add a model** opens AI service settings and keeps your conversation, draft and attachments available when you return.
- Attachment, model and More menus follow the current palette and stay aligned with their controls. The model menu keeps its setup actions within reach when scrolling through a long list.
- Use **Ctrl+K** to open **Find a tool**. Exact name matches appear first. Use the arrow keys to select, **Enter** to open or **Esc** to close.

## Windows distribution

The Windows x64 package targets Windows 10 and 11. The standalone EXE includes the .NET runtime. Stable packaging requires a trusted, timestamped publisher signature. See the [release channels and signing guide](RELEASE-CHANNELS.md).

When the signed release is available, existing installations can check under **Settings → Baseera updates**. Installation requires confirmation. Publisher signing and GitHub's SHA-256 download verification are separate checks.
