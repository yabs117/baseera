# Baseera changelog

See [GitHub Releases](https://github.com/yabs117/baseera/releases) for published packages, checksums, and older release entries. The notes below record changes by version.

## 0.6.5 Preview

- Added green, graphite, red, and blue palettes with saved light and dark appearance. The sidebar eye plays a short blink on click and respects the Windows animation setting.
- Added feedback submission inside Baseera. The form prevents duplicate sends while a request is running and keeps the draft when delivery cannot be confirmed.

[Full 0.6.5 notes](RELEASE-0.6.5.md)

## 0.6.0 Preview

- Reworked Overview around current CPU, RAM, and system-drive readings. The session CPU chart marks gaps when readings are unavailable.
- Added the **Ctrl+K** tool finder. Refined the Smart Repair composer, attachment review, and access to diagnostics and privacy details.

[Full 0.6.0 notes](RELEASE-0.6.0.md)

## 0.5.0 Preview

- Added device diagnostics, including performance, security, network, storage, system timeline, local search, and USB investigation.
- Added persistent Smart Repair cases and before/after comparisons. Selected diagnostic evidence is reviewed before sharing, and approved repair actions can be retested.
- Reorganized navigation and corrected Arabic icon direction. Smart Repair opens when selected.

[Full 0.5.0 notes](RELEASE-0.5.0.md) · [Investigation capabilities and limits](INVESTIGATION-CENTERS.md)

## 0.4.15 Preview

- Moved AI connection setup and testing into Settings. Added account sign-out that removes the local profile and preserves unrelated API keys.
- Added multi-turn Smart Repair, image and video attachments, optional voice input, and a checkpoint after ten attempts.
- Added optional Windows event and system diagnostics, separate approval for supported repairs, and explicit limits on conversation and diagnostic sharing.

[Full 0.4.15 notes](RELEASE-0.4.15.md)

## 0.4.14 Preview

- Streamed large files to remove the former 64 MiB buffer limit. Added scan progress, file details, and copyable SHA-256. The total file and ZIP limits still apply.
- Introduced the `Baseera.exe` asset name with a versioned compatibility asset for older updaters. Improved update cleanup on read-only temporary directories.
- Refined hover, press, and motion for buttons, navigation, switches, menus, and scrollbars.

[0.4.14 release](https://github.com/yabs117/baseera/releases/tag/v0.4.14-preview) · [File inspection](FILE-INSPECTION.md) · [Windows interaction notes](WINDOWS-INTERACTIONS.md)

## Earlier releases

[Browse the complete GitHub release archive](https://github.com/yabs117/baseera/releases).
