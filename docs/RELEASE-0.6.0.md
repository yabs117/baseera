# Baseera 0.6.0 Preview

## Overview

- Review the latest CPU, RAM and system drive readings in a dashboard that adapts to narrow windows.
- The session CPU chart displays up to sixteen samples. It draws a trend after two consecutive measurements and marks missing readings.
- Press `Ctrl+K` to find a tool in Arabic, English, French or Spanish. Use the arrow keys and Enter to open it.

## Smart Repair

- The assistant keeps the message composer at the bottom. Open **Diagnostics** to capture a window, choose data sources or review collected readings.
- The attachment area appears when there is something to review. Diagnostic sharing still requires approval for each request, and running a repair requires separate approval.
- Replies keep their provider and model labels, progressive text and copy controls. Details about privacy and request counts are under **More**.

## Download and verification

Open `Baseera.exe` on Windows 10 or 11 x64, or extract `Baseera.zip` and open the executable inside. The .NET runtime is included. The browser extension is optional. Check the files against `SHA256SUMS.txt`. This preview is unsigned.

Existing users can check for updates from Baseera Settings on the Preview channel. `Baseera-v0.6.0-Windows-x64.exe` is an identical copy for the updater in older releases.

Validation covers 275 core tests, five browser extension protocol tests, and Windows UI checks for compact layouts, right-to-left text, conversations and diagnostic consent. Provider requests, microphone input and repair commands were simulated where applicable. [Diagnostic scope and privacy](DIAGNOSTIC-ATTACHMENTS.md).
