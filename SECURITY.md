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

The 0.4.15 development preview adds [Smart Repair](docs/SMART-REPAIR.md). Optional
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
