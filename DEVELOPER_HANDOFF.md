# Developer handoff

## Release and status

0.3.0-preview.1 is an initial working C# implementation for Windows Paratext 9.5.110.1. It compiles against installed `CorePluginInterfaces.dll` and `PluginInterfaces.dll`, both assembly version 2.0.100.0. Target .NET Framework 4.8, AnyCPU. This is a preview, not a claim of production readiness or completion of every structural edit category.

The preceding 0.1.0-preview.2 plugin was installed and loaded in the user's real Paratext host. Test writes targeted a separate synthetic NSTest project, with on-disk USFM comparisons for body and footnote corrections, ambiguity refusal, and revert. The user's session was saved and restarted normally with administrator approval for installation. IRVTam was not targeted by any plugin edit. See VALIDATION.md for the remaining export/Send-Receive boundary.

The new queue has also loaded 16,270 unresolved QA proposals in the real host, navigated to the first passage and paused without a production decision. All 85 Scripture/note hashes matched the immediate pre-queue baseline. The final installed build passed disposable-project Apply-and-next with an exact on-disk USFM comparison, commented Reject-and-next with no USFM change, Footnote region retention, and modeless reference browsing at a different verse. 125 synthetic checks pass. The installed 0.3.0 update also passed real-host preset selection and cumulative HTML reporting in NSTest, including authenticated reviewer and full snapshots. IRVTam scanning completed despite 215 legacy unreadable discussions, which are reported for manual attention. The queue is paused; no production decisions were performed and all 85 files matched the immediate pre-install baseline.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

No NuGet restore or .NET SDK installation is needed. The build uses Windows' .NET Framework C# compiler, C# 5 syntax, the installed netstandard facade, and the user's local host interfaces. Warnings are errors. `-ParatextDir` overrides the host directory. `-SkipTests` builds only the plugin; package.ps1 deliberately does not skip validation.

`artifacts/bin` contains a DLL, its `.ptxplg` copy, tests, synthetic demos and private local interface copies for test loading. Do not distribute this directory. `package.ps1` whitelists the plugin, installer/remover, documentation and checksum manifest, then emits a ZIP and its SHA-256 sidecar. No host DLLs, project notes or synthetic test executable are in the release ZIP.

## Code map

| File | Responsibility |
| --- | --- |
| src/Domain.cs | QA field parser, 16 semantic categories, conservative routing, Unicode-aware preview and exact token-local matching. |
| src/NoteService.cs | Live note retrieval, anchor/discussion fingerprints, conflict grouping, permissions/locks, token region mapping, host writes, verification, history and checked session revert. |
| src/Plugin.cs | Public standalone plugin, fixed context/Tools commands and dynamic secondary popup. |
| src/ReviewForm.cs | Category filter, source discussion, editable literal correction, explicit deletion, review acknowledgement and supported marker action. |
| src/QueueForm.cs | Modeless project-wide QA queue, cooperative book indexing, pinned target navigation, explicit decisions, pause/resume and interrupted-write reconciliation. |
| src/ReviewReports.cs | Curated comment presets, project-ID checked report location, session journal/report recovery, encoded offline HTML with Unicode-aware diff and modeless embedded viewer. |
| tests/ReportTests.cs | Presets, locked context capture, reviewer identity, 20/100 sessions, full footnote/intro/cross-chapter context, unchanged rejections, unverified edits, markup escaping and crash/audit recovery. |
| src/ReviewProgress.cs | Per-user/project atomic XML decisions and drafts, exclusive session lease and CSV export. |
| tests/QueueTests.cs | Queue navigation/decisions, stale identity, duplicates, persistence, interrupted writes, Unicode, lease and 16k lookup coverage. |
| tests/Tests.cs | Standalone test runner, interface doubles via RealProxy, desktop demos, optional read-only local route audit. |
| scripts/build.ps1 | Compile and test against the local 2.0.100.0 interfaces. |
| packaging/*.ps1 | Narrow installation/removal; refuses while Paratext is running. |

## Write invariant

Capture project/passage/selection identity → check permission → create pre-edit history checkpoint → obtain scope write lock → re-read the unresolved source note and current tokens → locate exactly one Original within the explicit region → change only that text token → submit `PutUSFMTokens` → send notifications → verify saved USFM → release lock → create post-edit checkpoint. Never call MakeHistoryPoint while holding a write lock: the real host can invalidate that lock. This ordering bug was found and corrected during real-host testing.

No raw project filesystem writes are used. No Send/Receive or export is invoked. The Scripture writer itself does not alter note status. After a queue decision is durably saved, a separate notes-lock operation resolves the source thread when all its structured proposals have decisions. Stale source, conflicting proposals, unavailable/revoked locks, ambiguous repeats, unsupported region boundaries or changed active context block automatic actions. Failure messages distinguish an unverified submitted edit from a confirmed success; they do not promise transactional rollback.

For text edits, chapter scope is used except introductions (whole book, chapter argument 0). Intro, body, heading, footnote and cross-reference content are separated using the public token flags and surrounding markers. Unknown/special/metadata/figure content is excluded. Spans crossing token boundaries are refused; do not flatten markup to make a match.

The only supported structural writer is a deliberately small marker-rename operation: one of `ms`, `ms1`–`ms3`, `s`, `s1`–`s4`, `cl`, `cd` may become another of those markers if its whole label is identical and uniquely anchored. Generated USFM is reparsed and compared before saving. Verse/chapter numbers, attributes, note boundaries and verse exchanges are excluded.

## API findings and tradeoffs

- `PluginMenuEntry` has fixed location/callback/image data and a localization event, with no supported menu-opening population callback, writable label, submenu collection, or refresh method. A fixed command opens a visible native proposal picker containing live correction buttons. Preview.1's invisible modal owner was removed because it made focus and action testing unreliable. This extra click remains a known UX difference from the original mockup.
- The host callback supplies `IParatextChildState.Selections`. Current selection and verse are used; the API's mouse-under-pointer passage is not assumed.
- `IProjectNote` exposes anchor, comments and resolved state, but not a stable ID or native tags. Fingerprint all comment text, creation/author metadata and anchor context. Uncertain duplicate identity refuses editing. Native tag filtering is not implemented.
- Parsing rich-text paragraphs concatenates each paragraph's `FormattedString.Text` spans before extracting fields. Do not parse bold styling or screenshot OCR.
- `Reviewer decision needed: No` alone never permits structural/instructional content to become replacement text. Uncertain categories, later replies, conflicting corrections, Latin commentary and alternatives go through review. Text writes continue to reject English instructions and obvious alternatives even after checking the review box.
- Revert stores one pre/post serialized scope per project in memory. It is guarded against later edits, but does not survive host exit. Native Ctrl+Z and history restore are not yet verified in the real host.

## Queue implementation and recovery

The queue uses `GetNotes(book, 0, true)` per available book on a WinForms timer. Host APIs stay on the UI thread. It yields between books; a single large book can still take time. Only structured QA source comments enter this queue. Order is book/chapter/verse plus stable fingerprint; all pending decisions require user input. The form is shown with `Show(hostForm)` where available, leaving Paratext usable while modeless ownership keeps the queue above asynchronous host passage activation. Navigation uses `OpenTextWindowFor(project, UseExisting, source.Anchor.VerseRefStart)` only on loading the next item or the explicit Return button.

The original passage menu retains strict active-window context checks. Queue-created services instead pin the original IProject and candidate passage. They intentionally do not depend on the active reference/selection, so reference browsing cannot retarget edits. Every write still checks permissions, locks, the exact fresh thread/anchor fingerprint, region, text uniqueness and saved USFM. New source replies between display and Apply cause a refusal.

The public notes API lacks stable IDs. Queue identities hash the source comment's unchanged text, author, timestamp and anchored verse range. They exclude later discussion replies and changing word offsets. Editing the source proposal or verse range creates a new identity; identical duplicates are blocked from all decisions until disambiguated in Paratext. Each apply still uses the stronger full-thread/anchor freshness key. Never substitute a queue identity for the write freshness guard.

Progress is deliberately local per Windows user/project ID, under LocalApplicationData/ParatextNoteSuggestions/Review. There is one atomic XML file per touched proposal, a small current cursor/draft file, and an exclusive open session.lock handle. Records contain source reference/category, exact reviewed text/region, decision, comment and UTC time. Temporary files flush to disk before atomic File.Replace/File.Move; XML entitizes carriage returns to preserve multiline comments. Cache updates follow durable writes. Drafts save after a short debounce, on pause/leave/close and before navigation; consent checkboxes are never restored.

After permission/source/target validation under the write lock, the writer invokes a callback to durably persist `Pending apply` immediately before submission. A progress-save failure prevents submission. Pre-write validation refusals do not create pending intent, so the reviewer can revise/reject normally. On success, persist Applied only after the writer's USFM verification. Any exception stops/pauses; pending records cannot apply or reject again without explicit user reconciliation. Manual-completed decisions require review and a comment. Retry requires a comment and a separate confirmation that no edit occurred. The Scripture writer and progress ledger are not a shared transaction; this pending-state protocol intentionally exposes uncertain outcomes. A disk failure after the USFM write cannot become a silent retry.

Comments are not appended to Paratext notes and local progress does not sync through Send/Receive. The review queue resolves completed source threads via the public ProjectNotes lock and Resolve API; note status participates in normal project note synchronization. CSV export is available, including pending outcomes. This release does not provide concurrent reviewer merging or cross-device resume. The session lease is disposed even if the form is directly disposed without being shown. The queue does not implement a cross-session USFM undo: use Paratext history for old changes.

Tests require unsandboxed Windows filesystem semantics for atomic File.Replace. Under the restricted filesystem shim, Replace returns Access Denied; the queue pauses safely. The same checks pass outside that shim. Tests use ignored artifacts only.

## Resolving reviewed notes (preview.2)

Queue decisions are saved before note resolution, with `Resolution=Pending` and the exact reviewed discussion fingerprint. `ResolveReviewedThread` acquires `WriteLockScope.ProjectNotes`, locates exactly one source by its immutable comment identity, rejects changed discussion/duplicates, and checks that every QA source comment in that thread has a completed local decision. It calls `IProjectNote.Resolve`, sends notifications and verifies the resolved status by re-reading notes. It does not infer ProjectNotes permission from Scripture editing permission: the host notes-lock/Resolve API enforces authorization.

Incomplete multi-proposal threads save Waiting for remaining proposals. When the last decision resolves the thread, earlier peer decision records are updated too, and the current acknowledgement is saved last. A failure leaves pending resolution visible with a dedicated Retry note resolution action. Apply/Reject/manual decisions are disabled for that already-saved decision; retries never call the Scripture writer. Already-resolved source notes are acknowledged idempotently. No history checkpoint occurs while a notes lock is held.

Indexing reads resolved notes as well so a crash after Resolve but before local acknowledgement can recover. Only local reviewed history or pending resolution is retained from resolved notes. Old preview.1 decisions have an empty resolution field and are not retroactively resolved on startup. Sources missing/changed beyond identity matching produce a recovery notice and remain in the local outstanding log. Pause/defer/close never resolve a new note. CSV includes a Note resolution column. The one-off picker retains its prior behavior; this change belongs to the queue.

104 checks include apply/reject/manual resolution, multi-proposal deferral, notes-lock failure after a verified edit, idempotent acknowledgement after restart, changed discussion, unverified Resolve, old-record compatibility and orphaned recovery. Real-host Resolve acceptance passed on the installed update: a synthetic Apply performed the exact expected USFM edit and resolved its note; a commented Reject resolved its note with no USFM change. Both advanced and persisted local Resolution=Resolved. The rejection comment was absent from project notes. Native Resolve retained original QA comments and appended its own status record (serialized by this host as Status=deleted); the plugin calls Resolve, never deletes note XML or original comments, and verifies public IsResolved. All 85 production Scripture/notes hashes matched the pre-update baseline. See VALIDATION.md.

## HTML session reports and comment presets (0.3)

The user's resolved notes were inspected read-only. There were not five repeated substantive decision comments from their account; they explicitly chose five compiled reusable decision phrases rather than verbatim project comments. Values are product defaults in CommentPresets, not private note quotations or frequency claims. The dropdown only populates the existing editable comment field and never submits a decision.

A new queue gets a GUID session identity under the existing per-project local progress folder. Decisions/attempts get an audit GUID, authenticated host.UserInfo.Name, decision UTC, latest-action user/UTC, full before/after context, source discussion, context scope and evidence status. Root .review.xml remains the decision/resolution checkpoint. Each audit is also written under sessions/<session>/<audit>.entry.xml, preserving earlier attempts when a later decision replaces the root checkpoint. Outstanding enumeration repairs missing/stale origin-session copies from canonical root records after a partial audit write failure. Validate session/audit GUIDs before building paths.

Text and marker writers expose an evidence callback executed under the Scripture write lock after target validation and before the durable pre-submit callback. Before/expected-after full anchored passage tokens are serialized without discarding inline notes or markers; introductions include the entire introduction before the first chapter. Cross-chapter/cross-book read-only snapshots collect the anchored range; cross-chapter writes remain refused. Pending intents retain expected results explicitly unverified. Verified-save evidence is set only after the existing saved-USFM verifier succeeds. Reject captures a single current passage and uses it for both columns. Manual completion compares review-load/draft baseline and decision-time snapshots, with explicit unverified manual-editor attribution. A changed authenticated user during write preparation refuses before submission.

Pause, close, end-of-pass and recorded failures trigger reporting. Reports are cumulative for an open queue, while reopen starts a new session. Repeated stops without changed audit XML reuse the prior report. Reports use unique timestamped HTML filenames; no empty-session report is written. Partial writes use flushed temporary files then atomic move. Session manifests store digest/path and closure; unclosed sessions can be recovered into HTML without inventing old snapshots. Resolution-only recovery of an older checkpoint records a new audit but leaves the old decision user/context unknown. Explicit manual reconciliation updates the older pending audit rather than leaving it falsely marked as still awaiting confirmation.

The public project API has no filesystem path. Automatic detection checks known local roots and validates Settings.xml Guid against IProject.ID; a folder picker supports custom locations. The selection is cached locally, revalidated at report time, and reports go strictly into the ordinary Review Queue subfolder. Reparse-point report subfolders are refused. Settings XML prohibits DTD resolution. Folder failures preserve local decisions and stop/hold close for a retry, instead of silently claiming a report exists.

HTML is self-contained with no external assets/network requests. All note/Scripture/user fields are HTML encoded; fixed ES5 filter code never interpolates user data into script. Text-element diff boundaries preserve Tamil combining sequences. The modeless WebBrowser viewer uses an in-memory copy of the generated HTML, cancels external navigation/popups and offers the validated local file in the default browser. CSS includes legacy embedded-browser fallbacks. No global browser-emulation or security setting is changed. Private reports, source comments and local audit records are never committed or added to release archives.

An additional real-host log investigation found legacy rich-text language tags that throw ArgumentException inside CommentWrapper.Contents. ReadCandidates now isolates that per discussion instead of stopping the book scan. Such notes create explicit unreadable placeholders/warnings: unresolved ones cannot Apply/Reject/manual-complete through the queue, and already-resolved ones are informational. No invalid note XML is rewritten or silently treated as reviewed. Fresh also refuses an unreadable candidate, including the one-off review path. Resolution lookup first narrows comments by source author/timestamp, so an unrelated malformed legacy comment cannot prevent resolving a readable target; matching ambiguous/unreadable content still refuses. Warnings persist in session manifests and appear in HTML even in a warning-only session.

125 checks pass including the material report paths and next-proposal comment availability. Real-host report acceptance is recorded separately in VALIDATION.md. .NET Framework still has ordinary Windows path-length limits; test roots are intentionally short. Do not change the established lock/checkpoint order or regional text guards.

## Validation and remaining work

See VALIDATION.md for evidence and explicit pending real-host checks. Before stable release:

1. Continue from the now-verified 9.5.110.1 loading and native proposal-window flow. Preserve the real-host lock/checkpoint ordering regression and the form-creation region-selection regression.
2. In a disposable project, check real token offsets/flags and region inference, particularly paragraph endings, note-end markers, bridged verses and introductions. Confirm chapter-zero scope and the exact persistence/serialization behavior.
3. Apply and reopen body/footnote/intro corrections, export USFM, compare the exact diff, then perform an authorized test Send/Receive between test copies. Investigate any normalization differences rather than silently accepting a mismatched post-write serialization.
4. Confirm history-point behavior and safe recovery; do not advertise native undo before testing it.
5. Extend structural operations only as dedicated validated transforms. Complex note moves and verse exchanges remain review-only. The supplied category name is not enough to construct them safely.
6. Consider public API evolution or a host-supported annotation menu if direct integration improves. Do not misuse the localization event as a live Scripture menu hook.

The older PLAN.md and CATEGORY_HANDLING.md retain the user's full target requirements. This release's implementation status and limits are defined here and in INSTALL.md/RELEASE_NOTES.md. The ignored earlier HTML mockup is historical; it does not represent the compiled plugin.
