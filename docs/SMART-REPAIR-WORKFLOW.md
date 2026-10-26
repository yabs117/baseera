# Smart Repair cases — development 0.4.17

This extends the existing assistant. The public download is still 0.4.15.
AI setup, conversation, one-request diagnostic consent and the three-action repair
catalog remain in use.

## Four stages

1. **Discover:** choose system summary, device readings, Application/System error
   events or fixed service states. Opening a case collects nothing. Collect locally,
   review the editable redacted preview, then separately approve sending that exact
   preview to the selected AI recipient for one request.
2. **Analyze:** existing integrations distinguish observations, plausible causes,
   uncertainty and safe checks. Model output is untrusted advice. Text-only and
   image-only requests remain supported.
3. **Act:** review the exact catalog command, effects, risk, network use and admin
   requirement. Approve each action separately; Windows UAC remains separate. Only
   DNS cache flush, SFC repair and DISM repair exist. No generated commands, service
   modification, forced process termination or reboot.
4. **Verify:** Retest collects the same selected sources locally. It compares CPU,
   RAM, matching-volume free space and known fixed service states against the
   action baseline (or preceding sample). It sends nothing to AI and grants no
   upload consent. Repeat the original failing action yourself to check the symptom.

A changed reading is **not** a proven fix. Exit zero records tool completion only.
Missing baselines, different installation scopes, edited previews, different
sources/time ranges or invalid chronology are inconclusive. Partial samples are
labeled. Overlapping event periods are not treated as reduced error counts or proof
of improvement. Each comparison retains these limits after reopening.

Services use `SC_MANAGER_CONNECT` and `SERVICE_QUERY_STATUS` only: Dnscache, Dhcp,
wuauserv, BITS, EventLog and WinDefend. Inaccessible states are unavailable. A stopped
trigger-start service can be normal; that is not a fault diagnosis. See the
[Microsoft API](https://learn.microsoft.com/windows/win32/api/winsvc/nf-winsvc-queryservicestatusex)
and [trigger behavior](https://learn.microsoft.com/windows/win32/services/service-trigger-events).

## Local case files

**Save case locally** persists the title, optional local onset time, bounded analysis
summaries, fixed action/exit-code history and retest comparisons. Before first save,
the case lives only in memory. Saved cases also persist action-start/result metadata;
pending actions reopen as **result unknown**. Closing a modified saved case offers
save/discard/cancel. Opening a different case cannot replace unsaved changes.

Case files reopen history for reading. Archived actions cannot execute. A fresh
analysis and per-command approval are required. Local samples can be retested
without AI setup; inference still needs the connection in Settings.

Files live in `%LOCALAPPDATA%\Baseera\Cases` (or the explicit test data folder).
Windows DPAPI protects them for the current user. They use bounded schema-versioned
JSON internally, GUID filenames, an exclusive store lock, atomic same-directory
writes and optimistic revision checks. At most the newest 200 files are listed.
Unreadable files are preserved and counted without hiding valid cases. DPAPI does
not protect against another process running as the same user.

Problem/analysis text and numeric comparisons are saved and can be private even
with raw storage off. **Store reviewed preview** is off by default. It adds only
reviewed, best-effort-redacted text and associated aggregate readings. Unreviewed
raw evidence stays excluded. Hidden full process/startup lists are never stored;
names explicitly in the reviewed preview can remain. Editing the preview removes
its typed readings, preventing comparison of hidden values that no longer match.
Images, video, audio, API keys and OAuth tokens are excluded. No case is uploaded
automatically; loading grants no sharing consent. Delete unneeded files from the
folder while Baseera is closed; no cloud sync or automatic retention deletion.
Literal event filters are omitted with raw storage off and redacted when included;
a changed filter marks the archived scope edited, preventing false comparisons.

Cases retain at most 20 evidence samples, 20 analyses, 40 actions and 20 comparisons,
within 2 MiB. This is recent history, not a complete forensic audit trail. Losing
the installation scope file makes cross-scope comparisons inconclusive.

## Architecture, activity and access

The case workflow has an `INotifyPropertyChanged` view model with constructor
injection of `ICaseRepository` and `IDiagnosticCollector`. The application composition
root supplies them and a shared `TaskHub`; no new runtime package/container. This is
a gradual migration, not an application-wide MVVM rewrite.

Activity tracks foreground utilities, automatic app updates, self-update checks and
downloads, analysis/transcription, discovery, retests and native repairs. Download
progress uses actual received bytes; work without a measurable total is indeterminate.
Inspection retains separate per-file byte progress rather than a fabricated total
folder percentage. Linked cancellation is available where supported. Native
servicing cannot be force-canceled after launch. Task history is process-local,
bounded to 32 finished/16 simultaneous operations. Disposal without a result is
interrupted, never successful.

New controls provide keyboard focus, accessible names and polite live status.
Navigation scrolls at constrained heights. The shell uses Windows high-contrast
colors when enabled. Automated four-language/RTL, focus and palette tests do not
replace a full Narrator audit or every custom Windows theme.

## Validation limits

`./build.ps1 -Test -Smoke` includes corrupt-case bounds, comparisons, cancellation,
revision conflicts, DPAPI, reopen, source opt-in, mocked repair/AI isolation and
four-language UI checks. `--workflow-only` selects focused fixtures. Screenshots
use synthetic data. Local native checks read device/service status, never repair.

The manual `native_vm_repairs` Actions job runs fixed SFC/DISM commands only inside
a disposable **GitHub-hosted Windows Server 2025 guest**. It refuses local/self-hosted
runs and requires an explicit switch/sentinel. Each step records command, UTC times,
exit code and stdout/stderr; servicing is never killed or followed by a reboot.
The [validation run](https://github.com/yabs117/baseera/actions/runs/40227040960)
succeeded at commit `a8e9f49c5c598a3e03ba8d1749f6022df5934929`; its
`Baseera-native-vm-evidence` artifact records the results. This validates native
invocation there, not this later UI, a broken-to-fixed scenario, or Windows 10/11
end-to-end certification. Licensed Windows 10/11 VM coverage is still needed before
widening the repair catalog.

Dependabot, transitive NuGet audit and CodeQL security-extended workflows are added.
Malformed case/ZIP/PE/model/update inputs are exercised by tests. Passing checks
is not a security guarantee or a substitute for review.
