# Security scope

Baseera is a preview, not an antivirus replacement. No detection rate or safety
guarantee has been measured. Keep Windows protection enabled.

The scanner does not execute targets, extract archive contents to disk, exclude
files from Defender, quarantine files, or upload them. Static indicators can be
benign. Encrypted/unsupported archives and exhausted limits are reported as
incomplete. Native parsers and image decoders remain an attack surface; do not
treat this preview as a hardened sandbox for hostile samples.

Window translation explicitly sends the selected visible region to Google's Gemini
API. The desktop key uses Windows DPAPI for the current user. A screenshot is kept
in memory for the request, not written to the app's data directory. Other software
running as the same user is outside this storage protection boundary.

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
