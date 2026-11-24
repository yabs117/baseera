# Baseera 0.5.0 Preview

Baseera opens on Overview. Smart Repair opens when you select it or press
Ctrl+Shift+E. Tools and diagnostics now expand on demand, with Universal Search
in Your tools. Arabic keeps the right-to-left layout and the intended orientation
of check marks, status symbols and provider logos.

## Device diagnostics

- Overview, Device Health and Performance show sampled CPU, memory, storage
  and processes with timestamps and missing-data indicators.
- Windows AI Doctor combines selected findings for review in Smart Repair.
  Performance Detective tracks resource use over time and reports correlations.
  Security Center reads available Windows security status and verifies file
  signatures and hashes. Its optional reputation lookup sends the approved hash.
- Network Doctor measures DNS, latency and packet loss. System Timeline shows
  changes and errors in time order. USB Center displays available device details
  and offers a non-destructive read of a user-selected file.
- Storage Explorer measures selected folders and hashes candidate duplicates.
  Supported temporary-file cleanup requires review and approval. Universal
  Search indexes selected local paths, including supported PDF and Office text.
- Smart Repair can save cases on this Windows profile and compare selected
  evidence before and after a reviewed repair. Any execution is limited to the
  built-in catalog and requires separate approval. Diagnostic evidence sent to
  an AI provider requires review and approval for that request.

The [investigation guide](INVESTIGATION-CENTERS.md) documents data sources,
limits and consent. AI features depend on your configured provider and model.

## Download and update

For Windows 10/11 x64. Save `Baseera.exe` in a writable folder, or extract
`Baseera.zip` and open `Baseera.exe`. The .NET runtime is included.
`Baseera-Browser-Extension.zip` is optional. `SHA256SUMS.txt` lists asset hashes.
The versioned EXE is included for older updaters that expect that filename.

In Baseera, check **Settings → Baseera updates**. Choose **Preview** if your
version offers an update-channel selector. Installation starts after approval.
The EXE is unsigned. See [update channels, signing and recovery](RELEASE-CHANNELS.md).

## Validation and limits

On the development Windows x64 test host, 275 core and five extension protocol
checks passed, along with packaged UI and diagnostics smoke checks. The
packaged tests exercised four interface languages and both themes. An isolated
upgrade from 0.4.15 and forced-startup rollback preserved local settings.
See the [QA record](QA-REVIEW.md) for test coverage and gaps.

Full Windows 10/11 device coverage, live paid AI calls and a full screen-reader
audit remain unverified. File inspection provides indicators for review. A
performance correlation or a successful repair exit code does not establish
the cause or prove that a symptom is resolved.
