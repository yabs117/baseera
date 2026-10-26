# Investigation centers (0.5.0-preview development)

These eight centers extend Baseera's existing Windows tools. They are **development
features** in the draft pull request, not in the public 0.4.15 download. Each
center is optional in Settings. Opening a page displays its controls; collection
starts only after an explicit action. Reports identify missing and partial sources.

## Scope

| Center | What the current implementation does | Important limit |
| --- | --- | --- |
| Windows AI Doctor | Collects selected system, device, service, driver and Application/System event evidence into an editable review for the existing Smart Repair assistant. | The model proposes possible causes; it does not learn from the local machine, prove a cause or execute generated commands. One request's diagnostic sharing needs explicit recipient-bound approval. |
| Performance Detective | Samples CPU, physical memory and disk counters over time. Shows a chart, sample table, symptom markers and process CPU/I/O coincidences, with protected local history. | Process I/O includes non-disk I/O and a shared service host cannot identify one responsible service. Correlation is not causation; gaps and PID reuse split evidence. |
| Security Center | Reads Defender, firewall, registered antivirus, volume protection and cached Windows Update dates; checks a selected file's SHA-256 and embedded Authenticode signature. Optional VirusTotal hash reputation requires a user-supplied key and separate approval. | Cached update dates do not prove currency; protection state is not a full audit. Trust verification uses local cache, not live revocation. Reputation sends only SHA-256 and may be unknown/outdated; Baseera is not an antivirus. |
| Network Doctor | Measures DNS resolution and bounded ICMP latency/loss for a chosen host; includes adapter state and available Wi-Fi signal/link rate. Repeated samples can be saved locally for comparison. | ICMP may be blocked; a timeout does not prove the internet is down. Wi-Fi quality/rate is not throughput and may be unavailable due to Windows permissions. No SSID or BSSID is retained. |
| Storage Explorer | Measures a chosen local folder with bounded traversal, displays large folders/files, hashes size-matched candidates for actual SHA-256 duplicates. Separate safe-temp preview offers an individually selected, exact-path recycle operation. | A partial scan is not a whole-disk accounting. Cleanup is limited to eligible old small files under the fixed user temp folder, requires fresh review/acknowledgement, and uses Windows' recycle confirmation. Never auto-selects or silently permanently deletes. |
| Universal Search | Builds a bounded local name index, optionally extracts text from selected TXT-like, OpenXML Office and PDF files, and supports Arabic/English normalization plus basic time/type phrases. Result previews are inert. | This is a keyword index, not a semantic model. PDFs use extractable text on the first 20 pages; scans/images need OCR, which is not included. Office macros or external links are never executed. Content indexing may retain text locally; opt in before enabling it. |
| System Timeline | Reads bounded Windows event channels, updates/driver notices and uninstall-registry dates, then orders results around a chosen symptom time. Protected history can be reopened. | Event retention, permissions and registry dates vary; an update near a failure is a clue, not evidence that it caused it. Application errors include non-crash records. |
| USB and external devices | Lists selected USB/PnP and removable-volume states. A user-picked file on a verified external volume can be read twice and compared without writing it. | Read speed may be cached and two matching reads cannot certify the hardware. No destructive surface test, repair or driver installation is offered. |

## Privacy, safety and recovery

- Native WMI, event, PDF/Office and signature probes run in a short-lived child
  process with a job-object time/memory limit. This reduces hangs from providers
  and malformed files; it is not a complete security sandbox.
- File scanning and search accept local fixed/removable roots, reject linked
  directories and keep bounded entry, time and content limits. Partially read
  files are labelled as such.
- Locally available OneDrive/Cloud Files items are distinguished from symbolic
  links by their native reparse tag. Offline/recall items are skipped until the
  user downloads them. Unknown tags and name-redirecting links remain excluded.
- Search indexes and measurement histories are encrypted locally with Windows
  DPAPI for the current user. They are not uploaded to an AI provider on opening
  a center. A review action opens the existing assistant with an editable
  preview; explicit one-request consent remains required before any transfer.
- The existing repair catalog is unchanged: DNS cache flush, SFC and DISM.
  Each action needs its own approval and Windows elevation where applicable;
  the app does not execute model-generated shell commands, terminate servicing,
  or restart Windows automatically.
- An external center report cannot be retested by guessing its sources inside
  Smart Repair. Refresh it in the originating center, then review the new
  before/after evidence. No single sample or exit code is reported as a
  verified repair.

## Build and validation

Run `./build.ps1 -Test` and the packaged UI smoke suite on Windows x64. The
`--investigations-only` smoke route uses fixtures for the eight pages. Its
service checks use an isolated child on synthetic text/Office/PDF files and a
fake reputation response. These checks avoid live WMI, event, Internet, repair
and cleanup operations. Windows 10/11 device variety, accessibility audits and
signed distribution still require additional validation; no publisher signing
certificate is currently available.

For explicit native source integration checks, run
`./tests/windows/Test-ReadOnlySystem.ps1` from the repository. Add
`-AllowNetworkCheck` to permit fixed-host DNS/ICMP measurements. Results contain
counts and source availability, not raw event payloads or window titles. See
[the review report](QA-REVIEW.md) for reproduced defects, fixes and coverage gaps.
