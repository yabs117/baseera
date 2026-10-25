# Baseera 0.4.15-preview

## AI setup in one place

- Configure the provider, API key or eligible ChatGPT sign-in, and available image-capable model in **Settings → AI service**.
- **Test connection** sends a fixed prompt and a generated 64×64 image, with success, partial-result, failure, cancellation and timeout states. It sends no chat history, attachments or diagnostic logs. This optional request may consume provider credit or plan usage.
- Open the error assistant directly from Settings. The assistant is for conversation; returning to Settings retains its temporary chat, draft and attachments. Stale test results cannot validate another provider, account, key or model.
- ChatGPT sign-out removes that account locally, cancels its in-flight requests and updates open windows without closing Baseera or changing unrelated API credentials.

## Smart Repair and continuous conversations

- Extend the existing error assistant with optional local system information and bounded Application/System error events. Sources are off by default. Review and edit/redact the preview, then approve sharing it for one analysis only.
- See likely causes, evidence limits, confidence and practical next steps. Only three fixed repair actions can run: DNS cache clearing, SFC and DISM. Each requires separate command/effect review and explicit approval; native elevation uses Windows UAC. No generated scripts or automatic restart.
- Continue in a bottom composer with selectable formatted replies, provider/model identity, inert code blocks and independent copy actions. Keep the last twenty exchanges locally and send at most eight recent exchanges / 24,000 characters. Private-diagnostic exchanges are excluded from future context; old media bytes are not resent.
- A local checkpoint asks before continuing after each ten analysis/transcription attempts. It is not a balance lookup or spending guarantee.
- Attach images or short videos. Gemini can receive the original selected clip, including audio/metadata; other supported vision adapters receive six sampled images without the original audio. Media decoding depends on installed Windows codecs.
- Record voice locally, review/play/discard it and explicitly approve transcription where supported by the selected API service. Transcribed text remains editable before sending. Windows dictation is also available. This is voice input, not a live voice call.
- Preserve Arabic RTL, English, French, Spanish, the existing navigation/fonts and other utilities. File inspection, window translation, app updates and image conversion remain available.

## Download and in-app update

Download [**Baseera.exe**](https://github.com/yabs117/baseera/releases/download/v0.4.15-preview/Baseera.exe) for Windows 10/11 x64. The .NET runtime is included.

Existing users can use **Settings → Check for updates**, or receive the update offer when startup checks are enabled. Installation still needs the user's update confirmation. The download is verified with GitHub's SHA-256 metadata and the embedded version, replaced at the same path and restarted with the existing recovery backup.

`Baseera.zip` includes the same app, licenses and start guide. The browser extension is an optional separate ZIP. `Baseera-v0.4.15-Windows-x64.exe` has identical bytes to `Baseera.exe` and exists for the updater shipped in 0.4.1–0.4.13. Both current and legacy update-feed parsers are checked against the published assets.

## Validation and scope

The release build and self-contained x64 package passed with no build warnings/errors. Verification includes 85 core tests, five extension tests, 58 base UI renders and the additional Smart Repair, conversation, twenty-provider, sign-out, settings, translation and self-update regression suites.

Provider/repair tests use fake HTTP, diagnostics and process launchers. They do not certify real-provider diagnosis quality, account eligibility, every Windows configuration or native repair outcomes. No paid AI inference, real microphone capture or Windows repair ran during automated verification. This remains an unsigned preview; code signing and independently measured antivirus detection are not claimed. Review diagnostic previews before sharing. SFC/DISM modify Windows and are not automatically reversible by Baseera.
