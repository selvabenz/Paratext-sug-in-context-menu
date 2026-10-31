## 0.2.0-preview.2 note resolution validation

104 synthetic checks cover the existing queue/writer plus original-note resolution after Apply, Reject and manual completion; multi-proposal thread deferral; notes-lock failure after a verified Scripture edit; idempotent already-resolved recovery; changed discussion refusal; resolution verification failure; compatibility with older records; and missing-source recovery notices. Fixtures assert a project-notes lock is held when Resolve is called.

Real-host note-resolution acceptance for this update is pending. Earlier real-host queue/writer results below remain historical evidence and did not resolve notes.

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
