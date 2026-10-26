# Update channels, signing and recovery

Development 0.4.17 adds Settings → Update channel. Preview receives both channels;
Stable filters preview tags and GitHub prereleases. Existing version ordering still
applies: switching to Stable waits for a newer stable build instead of downgrading.
Channel changes clear offered/in-flight results. Installation needs confirmation.
This change does not publish or promise a Stable release.

Fixed-repository/HTTPS/asset-name/size/digest/file-version checks, same-path replacement,
restart acknowledgement and rollback remain. A channel label is not a security
assertion. GitHub's digest is consistency metadata, not a publisher signature.

## Signing

Current releases and this development binary remain **unsigned**: the maintainer
has no publisher certificate yet. No self-signed certificate is presented as public
trust. `build.ps1 -Publish -SigningThumbprint <exact thumbprint>` supports a selected
code-signing certificate in CurrentUser/My and Windows SDK SignTool. It must have a
private key, valid dates and code-signing EKU. Nothing is selected automatically.
Do not commit private keys or passwords.

The published EXE is signed with SHA-256 and an HTTPS RFC3161 timestamp, then verified
for trust/timestamp and selected signer before copies/hashes are made. Stable
packaging fails without a valid timestamped signature. Use
`packaging/sign-release.ps1 -Path <exe> -VerifyOnly` without private-key use.
The gate is in the build script; it does not prevent a repository owner manually
uploading other files. See [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool)
and [timestamps](https://learn.microsoft.com/windows/win32/seccrypto/time-stamping-authenticode-signatures).

## Recovery

The updater keeps the exact old EXE until the new window acknowledges startup.
Failed launch attempts restoration; failed recovery retains backup/journal.
Preferences and cases are not deleted. This is installation recovery, not permanent
version history or detection of every runtime regression.

After an acknowledged-startup regression, close Baseera, back up its data and obtain
a known release from the official repository. Review version/checksum/signing before
manual replacement. No automatic downgrade or forced rollback across data schemas.
Unsupported newer case schemas are refused and files preserved. Windows servicing
repairs are not automatically reversible and are separate from app-update recovery.
