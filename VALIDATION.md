## 0.3.0-preview.1 preset/report validation

125 automated checks cover the prior workflow plus editable five-phrase presets, next-proposal input availability, under-lock full context capture, before/after snapshots and reviewer identity, footnote/body preservation, introduction and cross-chapter read snapshots, unchanged rejections, honest manual attribution, unverified attempts, 20 decisions out of 100, cumulative reports without duplicate empty stops, crash recovery, wrong-project refusal, audit path validation/repair, Tamil-safe highlighting, markup escaping and explicit unavailability for older metadata.

A synthetic 20/100 HTML report has been opened and visually inspected in the embedded report viewer. Host logs also revealed a legacy rich-text language tag failure during older queue indexing. Three regressions verify that an unreadable resolved discussion does not block scanning/resolution of other notes, unreadable unresolved sources cannot be edited/resolved through the queue, and warning-only reports remain explicit. All private analysis, reports and note data stay in ignored local artifacts.

Installed 0.3.0-preview.1 acceptance passed in Paratext 9.5.110.1 on November 1, 2026 (local time). In disposable NSTest, selecting the acceptance preset populated the comment, explicit Apply saved exactly the expected single replacement in the complete on-disk USFM, resolved the source note and advanced. Selecting the rejection preset then Reject resolved its source and advanced without an additional USFM change. Both session audit entries contain full verse context, the actual authenticated Paratext user, decision/action UTC timestamps, comments and verified resolution. Rejection before/after snapshots are identical. Pause generated cumulative HTML in NSTest/Review Queue; View latest report opened the embedded viewer, with readable summary, search/filter controls, reviewer, comment and resolved status. A repeated unchanged stop reused its report.

The final installed build also completed the real IRVTam scan: 16,270 proposals, with 215 unreadable legacy discussions reported explicitly instead of aborting indexing. No production Apply, Reject, manual completion or resolution was performed. The queue was left paused and a warning-only HTML report was saved in IRVTam/Review Queue. All 85 production Scripture/notes hashes still matched the immediate pre-install baseline. Installed plugin SHA-256 matched the staged release binary. Full export/Send-Receive acceptance remains pending.

## 0.2.0-preview.2 note resolution validation

104 synthetic checks cover the existing queue/writer plus original-note resolution after Apply, Reject and manual completion; multi-proposal thread deferral; notes-lock failure after a verified Scripture edit; idempotent already-resolved recovery; changed discussion refusal; resolution verification failure; compatibility with older records; and missing-source recovery notices. Fixtures assert a project-notes lock is held when Resolve is called.

Installed preview.2 acceptance passed in Paratext 9.5.110.1 using two newly added synthetic NSTest notes. Apply saved exactly the expected single body-text replacement, recorded Applied locally, resolved the original note via the public API, verified IsResolved, and advanced. Reject saved its reason locally, resolved its original note, verified IsResolved, and advanced without changing the on-disk USFM. The rejection comment was absent from project note contents. Persisted local records reported Resolution=Resolved; the host retained each original QA comment and appended its native resolution status record. The installed binary SHA-256 matched the package.

All 85 IRVTam Scripture/notes files matched the immediate pre-update baseline; no production decision or resolution was performed. Full export/Send-Receive testing remains pending. Earlier real-host queue/writer results below remain historical evidence and did not resolve notes.

## 0.2.0-preview.1 queue validation

94 automated checks pass against synthetic fixtures. Added coverage verifies stable source identities across later discussion, source changes under lock, explicit pinned targets while the active reference changes, no writes during indexing/navigation, decision-driven advancement, saved deferred drafts, duplicate blocking, apply/reject consent, persisted comments, pause/reopen, uncertain apply recovery, exclusive sessions, Unicode/multiline XML and CSV, atomic updates, and 16,000 pending lookups. This does not establish 16,000 real-host edits or a full-project performance benchmark.

The native modeless queue UI was opened with synthetic Tamil data and visually inspected. In the real 9.5.110.1 host, the new command loaded 16,270 unresolved structured proposals from IRVTam, navigated to GEN 1:8, displayed the correction and source-discussion tabs, and paused with saved cursor/draft. No Apply, Reject or manual-completed decision was made on that project. All 85 Scripture/notes hashes matched a baseline captured after saving and closing the prior host session. That baseline is private/ignored; one notes file had already differed from the older release baseline before this queue test.

Final installed-build acceptance in disposable NSTest also passed: Apply at GEN 1:1 wrote exactly the expected single body-text replacement (all other serialized text/markers preserved), recorded Applied, and advanced to GEN 1:2. Reject with a comment recorded Rejected, left the on-disk USFM unchanged, and advanced to GEN 1:3 with Footnote region preserved. Local XML records were inspected. Clicking a reference resource changed its active verse to GEN 1:4 while the queue remained waiting at GEN 1:3. Modeless ownership kept the queue visible after asynchronous host navigation. The installed plugin SHA-256 matched the packaged binary. No production decisions were made; all 85 production Scripture/notes hashes still matched the immediate pre-queue baseline.

Full production review and full export/Send-Receive remain separate checks. The preceding release's writer evidence below is not a claim of 16,270 real-host edit tests.

# Validation record — 0.1.0-preview.2

Environment inspected: Windows, Paratext executable 9.5.110.1, installed CorePluginInterfaces and PluginInterfaces 2.0.100.0, .NET Framework 4.8 target. Build performed with C# 5 compiler, warnings treated as errors.

## Completed

- Plugin compiles against both installed public interface assemblies. Public standalone plugin menu registrations and target framework are asserted by tests.
- **80 automated checks pass**, including all 16 category labels, reviewer/alternative/instruction routing, stale note and original refusal, exact Tamil replacement, combining-character boundaries, duplicate source fields, region separation, conflicting corrections, explicit deletion, permission/lock failures, revoked locks, repeated-click refusal, save exceptions, saved-token mismatch reporting, checked revert, supported marker rename and refusing verse-number changes.
- Synthetic host doubles exercise `GetNotes`, `CanEdit`, `RequestWriteLock`, `GetUSFMTokens`, `PutUSFMTokens`, serialization/reparse, notifications and history calls. These tests establish plugin behavior against the interface contracts, not the real host's persistence implementation.
- The actual WinForms review window was opened on the desktop with synthetic Tamil data. Text, controls and source discussion were readable at the observed window size. Apply was disabled until review acknowledgement. Applying the sample removed the intended comma; the test harness saved and the executor re-read the synthetic USFM file containing the corrected text.
- The local IRVTam note inventory found 16,905 structured QA comments (R2 and P3), without modifying project files or copying source notes into the repository. The test runner supports a read-only aggregate routing audit; this audit does not assert current thread status or edit applicability.

## Real-host checks on 2026-11-01

- Installed the plugin into the user's Paratext Plugins folder with administrator approval and verified matching binary hashes. Startup log confirms the plugin loads on 9.5.110.1. The Scripture context-menu command is visible and opens the native proposal window.
- Created a separate, unregistered `NSTest` project containing only synthetic Tamil Scripture and QA comments. No plugin write test targeted IRVTam.
- Reproduced a real-host failure in preview.1: creating a history point while holding the Scripture lock invalidated that lock and caused an ObjectDisposedException. Moved pre-edit checkpoints before lock acquisition and post-edit checkpoints after release. The synthetic host now rejects any checkpoint attempted while a write lock is held.
- Applied a word/phrase correction using the real host. Compared the on-disk USFM against the original fixture plus exactly one intended replacement; all other text and markers matched.
- Attempted a repeated-phrase correction. The host showed the expected ambiguity refusal, and the saved USFM remained unchanged.
- Ran the plugin's revert action. The complete saved USFM matched the original fixture afterward.
- Applied a footnote-only correction through the review form. Exact saved-USFM comparison proved that only the specified footnote text changed, preserving body text and markers.
- Found and reproduced a WinForms binding bug that reset the region to Body on initial form creation. Replaced the bound enum list with explicit items. An added form-creation regression check verifies that Footnote remains selected. The review window now also fits the available screen.
- Replaced the invisible owner/context popup with a visible, keyboard-accessible native proposal window. Added automatic installer/remover elevation and the official plugin-cache refresh step.

## Not yet established

- Broad coverage of every real introduction, heading, bridged-verse, and custom-marker layout.
- USFM export and Send/Receive round-trip from a disposable project.
- Native Ctrl+Z behavior, full history restore, bridged-verse identity and all real token-region layouts.

The user's session was saved and restarted normally for installation and updates, with Windows administrator approval. Test writes were limited to NSTest. Send/Receive was not invoked. A stable release must not be inferred from the automated test result.

## Disposable-project acceptance checklist

1. Back up or create a disposable project, then install using INSTALL.md and confirm the two Note suggestions commands.
2. Add synthetic QA notes with a known exact word/phrase correction. Test direct Apply and category-filtered review. Verify saved text after closing and reopening.
3. Repeat for sentence and whole-verse content contained in a single supported text token. Confirm unrelated verse text and markers remain unchanged.
4. Put identical words in Scripture body and a footnote. Apply a footnote correction and confirm only that note changes. Repeat for a heading and introduction after checking actual tokens.
5. Test stale/duplicate originals, deleted/resolved notes, restricted passages, later comments, repeated words, competing R2/P3 proposals and an unavailable lock. Confirm no accidental write.
6. Test the supported marker rename, validate USFM, and verify complex verse swaps/bridges or note moves are refused.
7. Revert immediately, then confirm revert refuses after another edit. Inspect requested history points.
8. Export USFM and compare an exact before/after diff. Confirm marker/note structure is preserved except for the deliberately chosen operation.
9. With explicit test-project synchronization authorization, Send/Receive to another disposable copy and verify the same correction arrives. Record merge/conflict behavior.
10. Record Paratext version, results and any API normalization differences before promoting a stable release.


## Decision comment and bulk review development build — 6 November 2026

The full suite passes **144 synthetic checks** against installed interface version 2.0.100.0. New checks cover decision replies in the original discussion, failed/ignored comment writes with pending recovery, duplicate-free retries after resolution failure, shared-thread completion, persisted category/search filters, bulk manual/no-change decisions without USFM writes, and refusal of sources changed after preview. Queue layouts were rendered and inspected at 450×640 and 760×900; filter and bulk-preview dialogs were also rendered and inspected. Logs and images are in ignored `artifacts/bin` and `artifacts/decision-build.log`.

These checks exercise synthetic host objects. Actual `AddNewComment` persistence, author/language formatting, a controlled Send/Receive round trip, DPI hardware checks, and release installation remain pending. No production project was changed. See [DECISION_COMMENTS_PLAN.md](DECISION_COMMENTS_PLAN.md) for issue-submission blockers and acceptance criteria.


## Installed decision-comment build — real-host acceptance, 6 November 2026

Installed SHA-256: `D24717ABC33FF5A764532B1D9BA65C9D9F7E76F4A65FD89B2BC47C9F3BB15AFF`. The checked installer ran with Paratext closed, refreshed the official plugin cache, and preserved the previous binary in ignored local artifacts. Installed and built hashes match. All 144 synthetic checks passed before installation.

In Paratext 9.5.110.1, the queue filtered three new NSTest proposals using a unique discussion search. Reject saved an attributed English/Tamil decision reply on the textual-basis thread and advanced. Bulk preview included exactly the remaining verse-alignment and USFM-marker proposals, initially unchecked, and required selection, shared comment and review acknowledgement. Submitting reported two decisions saved, zero failures and zero pending recovery. On-disk notes contain exactly one decision reply per test thread and a resolved terminal status. The full NSTest Scripture bytes are unchanged, and all 86 production Scripture/note files match the immediate pre-test baseline. A normal Paratext restart retained the saved filter and removed all three resolved fixtures from the unresolved scope. The session HTML report was generated in NSTest's Review Queue folder.

Two monitors were present and the real floating window restored on the desktop at the current display settings. The full 100/150/200% DPI and monitor-transition matrix is not established. Send/Receive and second-reviewer visibility remain pending: NSTest is an unregistered local test project without a configured test synchronization partner. No production Send/Receive was initiated. Issue #5 remains open for these acceptance items.

## Queue information and regional targeting — 12 November 2026

The development build passed **206 synthetic checks, zero failed** against the installed Paratext interfaces. Log: `artifacts/queue-information-build.log`. Regional targeting checks cover introductions, headings, footnotes, cross-references, introduction lock scope, Tamil dates, wrong-region refusal, source revalidation, paragraph boundaries and preservation of identical body text. Queue checks verify that recovery notices and complete session warnings appear in Log, the unreadable count excludes unrelated warnings, and the information window displays and updates the current review status and saved skip reason without recording a decision.

Rendered Review, Log and information windows were inspected: `artifacts/bin/queue-review-information.png`, `artifacts/bin/queue-log.png`, and `artifacts/bin/queue-information-details.png`. Existing compact-layout checks passed at all three test sizes.

Installed with Paratext closed using the checksum-verifying installer and official plugin-cache updater. Installed and tested plugin SHA-256: `8169B2CE0D505846487ECF059930D1535126D057863C01C7B5810358B35EB762`. Previous plugin backup: `artifacts/install-queue-information-9f98f32f41744579a5a5857ac38b0ed3/ParatextNoteSuggestions.previous.ptxplg`. Installation and tests did not edit production Scripture or notes. Live-host acceptance of the new regional bulk workflow, export and Send/Receive remain pending.

## Inline suggestion actions - 13 November 2026

The final installed build passed **215 synthetic checks, zero failed**, plus real Paratext 9.5.110.1 NSTest acceptance for Tamil inline labels, tooltips, right-click menus, required comment/review gating, Apply, Reject, Ignore, advancement, annotation cleanup and restart persistence. All **763 protected Scripture/note files outside NSTest remain byte-for-byte unchanged**. Installed/build SHA-256: `1ACC17E0E01CECC570046D23915A52B05F9F02D978F49BD82126409D7E91FEF2`.

See [the detailed host record](TARGET_HIGHLIGHT_PLAN.md) and [issue #6](https://github.com/selvabenz/Paratext-sug-in-context-menu/issues/6). Local fixture backups, checksums, final build log and result data are under ignored `artifacts/inline-host/`. This acceptance covers inline queue actions; the regional bulk, export, synchronization and full DPI/monitor acceptance limits above remain in effect.
