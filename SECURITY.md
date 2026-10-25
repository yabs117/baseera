# Security scope

Baseera is a preview, not an antivirus replacement. No detection rate or safety
guarantee has been measured. Keep Windows protection enabled.

The scanner does not execute targets, extract archive contents to disk, exclude
files from Defender, quarantine files, or upload them. Static indicators can be
benign. Encrypted/unsupported archives and exhausted limits are reported as
incomplete. Native parsers and image decoders remain an attack surface; do not
treat this preview as a hardened sandbox for hostile samples.

Window translation and error analysis send user-selected text or screenshots
directly to the configured AI service when requested. Each service has a separate
key and official endpoint. Credentials are never reused across services, and
authenticated requests do not follow redirects. See [AI services](docs/AI-SERVICES.md).
Each desktop key uses Windows DPAPI for the current user. A screenshot is kept
in memory for the request, not written to the app's data directory. Other software
running as the same user is outside this storage protection boundary.

In 0.4.15, AI connection setup is centralized in main Settings. Its
optional **Test connection** action sends one fixed prompt and a synthetic 64×64
solid-color JPEG, without private attachments, conversation or diagnostics. This
is an explicit potentially billable request, separate from the chat's usage counter.
Changing draft credentials/recipient, canceling or unloading invalidates the pending
result; an old response cannot approve a different connection. Test output is
restricted to local status text; it does not display arbitrary response bodies or
credentials. A matching test answer is not proof of diagnostic accuracy or account health.

The 0.4.15 preview adds [Smart Repair](docs/SMART-REPAIR.md). Optional
diagnostics are local, bounded reads of selected Application/System error logs and
a minimal system summary. They require an editable preview and a separate one-request
sharing approval before being included in an AI request. Redaction is best effort;
logs may still contain sensitive data and must be reviewed. No Security channel,
credential store, personal files or raw event XML is requested. Diagnostics remain
in memory and are not attached to feedback or persisted in preferences.
Optional event filtering is literal text matching, never an interpolated XPath or
shell expression. A local index groups displayed event headers and error codes;
it neither authenticates edited logs nor establishes a cause or a healthy device.

Case reports are saved only through an explicit local file picker. They include
the analyzed description/advice and bounded in-memory command/outcome history;
diagnostic snapshots are excluded unless separately selected. Stored credentials,
account identifiers and screenshot bytes are not exported. Redaction is best effort:
user-provided text can contain sensitive data, so review the report before sharing.
Reports are not automatically uploaded or attached to feedback. Editing the error
description or replacing its image disables stale repair actions until reanalysis.

The desktop conversation renderer parses a small bounded subset of text markup
into read-only WPF Runs, not HTML/XAML. It creates no navigable links or remote
images from model output. Code blocks are selectable/copyable text only; their
copy buttons do not enter the repair runner or bypass command approval.

Continuous chats include at most eight recent text exchanges / 24,000 characters
for the same service/account/API-key fingerprint. Exchanges using private log
snapshots are excluded from subsequent context. No previous media bytes or raw
diagnostic previews are silently reuploaded. Only the latest twenty completed
exchanges remain in the window; export is an explicit local file action.

Voice capture starts only on the user's recording action, stays in bounded memory
and stops on close. Transcription needs separate confirmation and the selected
recipient's own API key; it does not auto-send the resulting text. ChatGPT OAuth
credentials are never used at API-key transcription endpoints. Windows dictation
is an explicit OS feature with Microsoft's own speech/privacy behavior. No real
microphone capture or OS dictation is used in automated checks.

Video selection uses the installed Windows decoder locally, with size/duration,
dimension and time limits. Gemini receives the original clip/audio/metadata only
on Send; other providers receive six normalized sampled images and no raw clip or
audio. Media decoders remain outside a hardened sandbox. The ten-request checkpoint
is a local usage reminder, not a verified financial limit or account balance.

AI recommendations cannot become arbitrary commands. Only three immutable catalog
actions are executable: DNS cache clearing, SFC repair and DISM component-store
repair with restart suppressed. Each needs an unchecked-by-default acknowledgement
in a separate exact-command/effects dialog. A single-use, expiring approval is bound
to the canonical action. Executables resolve from the OS system directory, with
fixed arguments and no shell wrapper or PATH lookup. Native administrator actions
also require Windows UAC; the main app is not elevated by the feature. Repairs are
serialized, exit codes do not guarantee a fix, and Baseera does not kill servicing
on window close. System servicing changes are not automatically reversible.
The OS/user account and native executables are trusted; this is not a sandbox for
arbitrary instructions. Automated tests use fake processes and do not repair the host.

The feedback form opens a prefilled GitHub issue in the browser; the user reviews
and submits it there. No API key, screenshot or diagnostic file is attached
automatically. Users should remove private details before posting a public issue.

Browser translation sends page text on explicit request. Form fields, editable
regions and script/style elements are excluded, but ordinary page text can still
contain private information. Keys live in extension session storage restricted to
trusted extension contexts. Model output is assigned as text, never HTML.

WinGet updates are opt-in per package. The app does not accept new license agreements,
bypass elevation, or sign in for the user. A manual source overrides automatic
WinGet installation and currently supports inspecting GitHub releases only. Its
installer assets require review; the app does not execute them.

Baseera's own updater is separate from custom app sources. It queries the fixed
`yabs117/baseera` GitHub repository over HTTPS, requires an uploaded x64 executable
with the exact expected asset name, URL, bounded size and GitHub-provided SHA-256
digest, and validates the complete download and embedded file version. Redirects
are limited to GitHub and its release-asset hosts. A checksum verifies consistency
with GitHub's metadata; it is not an independent publisher signature. Compromise of
the repository, release credentials, local user account or GitHub is outside this
trust boundary. Executables are not yet Authenticode signed.

Installation uses same-volume staging in a randomly named sibling directory and a
temporary copy of the current executable as a background helper. The helper checks
the parent process identity/start time, old image hash and new image hash before
waiting for exit and performing an atomic replacement with a backup. It does not
request elevation or modify Defender settings. The backup is removed only after
the restarted app acknowledges loading its window. Failure attempts rollback;
failed recovery retains the backup and error journal. Preferences and DPAPI data
are not part of the replacement. A writable application folder is required.

For a suspected vulnerability, use GitHub's private vulnerability reporting if
enabled. Otherwise open an issue requesting a private contact without including
exploit payloads, secrets, personal data or samples. Do not post live malware.

ChatGPT account sign-in uses the official OpenAI OSS OAuth flow, PKCE, state/nonce
validation, signed ID-token verification and separate plan-use permission. Account
identities and tokens are stored atomically with Windows DPAPI, separately from API
keys. Refreshes are serialized and sign-out attempts revocation before clearing
local tokens. Failed revocation is reported. No browser cookies or credentials from
other apps are read. Plan requests require completed SSE responses and never fall
back to API-key billing automatically. See [sign-in scope and tests](docs/CHATGPT-SIGN-IN.md).
