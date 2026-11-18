# Review note queue fixes ? 18 November 2026

## Delivery and validation

The changes are in this working tree. The built plugin is `artifacts/bin/ParatextNoteSuggestions.ptxplg`. Following the subsequent build/test/install request, it was installed into `C:\Program Files\Paratext 9\Plugins\ParatextNoteSuggestions\ParatextNoteSuggestions.ptxplg`. The installer completed the official plugin-cache update. The installed SHA-256 matches the tested build: `e133d3e2def67925636590d5bbf695a11ab16eff03ffa90a2fe7933aece52cd5`. The previous plugin is backed up in `artifacts/installed-backup-20261118-081832`. A fresh full build/test run passed all 221 checks; its log is `artifacts/queue-refinement-final-validation.log`. Installed-build host checks were performed in disposable NSTest as described below. Paratext is left open on NSTest; no production decisions were made.

Validation result: **221 passed, 0 failed**. Final compilation also passed after refining the resolution-popup wording. The final test log is `artifacts/queue-refinement-final-validation.log`.

Compilation uses the installed Paratext 9.5 interface assemblies, .NET Framework 4.8 and warnings as errors. Regression tests use synthetic notes, token streams and host interfaces. Desktop layout checks render the queue at 450?640, 669?720 and 760?900. The 450?640 rendering was also visually inspected: the enlarged reference, review fields, actions and activity footer fit.

The restricted filesystem sandbox prevented the existing atomic-file-replacement tests from running correctly. The suite was rerun with local filesystem access; the atomic-save implementation was preserved.

## 1?2. Progress and completion feedback

**Cause:** Indexing only changed the header. Bulk previews and writes ran on the UI thread, updated an internal status label and supplied no visible progress bar. Completion details could be hidden behind the information button.

**Change:** Added a persistent activity footer with a progress bar and activity/count label. Indexing reports books processed; correction previews and both bulk execution paths report proposals processed. Single decisions and note-resolution retries show indeterminate progress. Explicit refreshes paint the footer between host calls without pumping application input during a Scripture transaction.

Completion popups are limited to bulk previews and bulk execution. Single Apply + next, Reject + next, Completed manually / no change, note-resolution retries and indexing update the activity footer without opening a popup. Bulk completion opens a modeless popup containing the relevant totals. Bulk results include verified Scripture corrections, skipped, not attempted, unresolved and pending recovery counts. An existing popup is updated rather than creating a stack of windows. Errors update the activity footer, preserve recovery records and pause review. Popups are displayed only when the queue is visible.

**Limit:** A slow individual Paratext API call still occupies the UI thread. Progress advances between calls; these changes do not make the host API asynchronous or establish a measured speed improvement.

## 3. Verse pinning and scrolling

**Confirmed code defect:** `ScrollProjectTo` broadcast the reference to the actual project window's group in addition to the selected group. Even selecting **None** could broadcast to a group. This conflicted with the UI's stated behavior and moved additional synchronized windows.

**Change:** Broadcast only to the explicitly selected group. **None** performs project-window navigation without an explicit group broadcast. The project and source-note reference remain pinned independently of reference browsing. The adjacent-verse/target-verse sequence reveals a target that Paratext already reports as selected. A further gap was found during follow-up review: the neighbouring verse was opened in the project but not sent to its selected synchronization group. That left grouped editors with an unchanged group reference, defeating same-verse reveal. Both the neighbouring reference and the pinned target now go to the selected group, with the final target restored on the next UI tick. No extra group receives a broadcast. Scroll requests are retried periodically while waiting for delayed window activation, within the existing bounded confirmation period. Error handling now stops the navigation timer as well as indexing/draft timers.

**Limits:** The installed public API exposes verse references, not editor viewport or caret positioning. A confirmed reference is not proof of a particular pixel position or selection. The queue's group should match the intended project editor group. Live-host acceptance is still required to determine whether the reported scrolling symptom has additional causes; synthetic tests cannot establish that every host/layout-specific symptom is fixed.

## 4. Speed and a native Paratext plugin

This project already is a native Paratext .NET plugin: it compiles to a DLL/`.ptxplg`, implements the Paratext plugin interfaces and reads/writes through the installed host API. Paratext's official plugin information describes this integration model: https://paratext.org/download/download-paratext-plugins/ . Reference implementations are at https://github.com/ubsicap/paratext_demo_plugins .

There is no separate web-to-native conversion that would automatically improve speed here. If ?native? means becoming part of Paratext's own source product, that is a separate integration effort with the Paratext maintainers; its speed and API benefits cannot be assumed.

The inspected paths scan notes book by book, repeatedly revalidate source discussions and Scripture, write durable local audit records, create history points, request host locks and verify saved results. Those steps account for work performed, but their relative time has not been measured. Before optimizing, time indexing, preview generation, host token reads, history/lock acquisition, note replies and report/audit writes separately on a disposable project. Next candidates are incremental scheduling between proposals, reduced repeated preview reads and less redundant UI/log rebuilding. Keep source revalidation, locks and saved-result verification around writes. Move only independent computation/file work off the UI thread unless host threading support is established.

Correction quality depends on accurate region/anchor matching, reviewer judgment and verification. Native packaging alone does not improve the linguistic quality of suggestions.

## 5. Larger passage reference

A separate passage-reference label uses 15-point Segoe UI Semibold. Only book, chapter and verse are enlarged; pass, category, review counts and status use the normal 10-point queue font. Existing resize/restore layout checks cover the minimum window size.

## 6. Headings, introductions, footnotes and cross-references

**Confirmed parser gap:** Region detection recognized English labels such as ?footnote? but not a location expressed as a marker such as `\s`, `\ip`, `\ft` or `\xt`. Those proposals could default to body text and fail exact matching.

**Change:** Recognize supported heading, introduction, footnote and cross-reference marker names in Topic/Location. Literal corrections keep the existing reviewed-text workflow; actual marker changes still use the structural workflow.

**Confirmed matching gap:** A section heading occurs before its verse marker. Its host token can carry the preceding verse's reference, which caused the anchored verse filter to exclude it.

**Change:** Map heading text to the following verse marker when one exists before the next chapter boundary. Heading evidence captures the whole fetched chapter, including preceding-reference heading tokens, so the approved before/after preview contains the actual correction. Introduction matching continues to use the whole book introduction; footnotes and cross-references remain isolated from body text.

**Regression coverage:** Marker-based locations for all four regions; a heading before verse 3 carrying verse 2's reference; exact saved evidence; unchanged identical body text; region/source revalidation under lock; full-book introduction reads/locks; ambiguous targets and formatting-boundary refusal.

**Limits:** These are reproducible code defects, not a claim that every reported note was examined. No real failing non-body note examples were supplied or modified. Multi-token corrections, structural instructions and ambiguous matches remain subject to the existing individual/manual review restrictions. Dedicated Sandhi/confirmed-typo bulk actions retain their body restriction; use the selected Scripture-edits preview for supported non-body corrections.

## 7. Reviewer attribution

New decisions already captured the signed-in Paratext reviewer's name in the local audit and decision reply. A recovery retry could, however, create the reply using a different currently signed-in user while its text still named the original reviewer.

The retry now requires the original reviewer's signed-in identity before posting a missing reply. A different account leaves the decision pending with an explanatory recovery message; it does not repeat the Scripture edit or rewrite the original attribution. Replies already verified can continue toward resolution without being posted again.

This records the person making the queue decision. A manual/no-change decision does not prove who made an earlier manual Scripture edit; the report continues to state that limitation.

## Follow-up installed-build host checks

The follow-up request restricted completion popups to bulk work and enlarged text to the reference alone. The installed build passed **221 synthetic checks** and was then exercised through the Paratext UI in NSTest.

- Next moved GEN 2:2 to GEN 2:12; Previous returned to GEN 2:2. The queue retained its original project and target, and the host confirmed the corresponding references.
- Completed manually / no change saved the comment/reply and advanced GEN 2:2 to GEN 2:12 without a completion popup or Scripture edit.
- Apply + next verified the correction at GEN 2:12, saved its reply and advanced to GEN 2:22 without a completion popup.
- Reject + next saved the reason/reply and advanced without another Scripture edit or completion popup.
- The initial last fixture used out-of-range GEN 2:32. It was corrected to valid GEN 2:24; reopening and same-verse Return left both the project window and toolbar at GEN 2:24. The invalid synthetic reference is not evidence of a plugin navigation failure.
- Full-file comparison verified the one intended verse-12 text replacement after normalizing line-end spaces removed by the host. All other Scripture content was preserved. Each completed test thread has one decision reply and the host's terminal `deleted` status record; the remaining fixture is unresolved.
- All **772** protected Scripture/note files outside NSTest retained their baseline SHA-256 values. Local results are in `artifacts/queue-navigation-host-results.json`; fixtures and private project files are not committed.

The enlarged reference and normal-size category/status text were visually checked in the installed queue. Very narrow panes and long verses still expose the public API's limitation: reference confirmation cannot certify exact viewport/caret positioning. Scroll-group B was exercised in the host; the complete A?E/None and multi-monitor/DPI matrix is not claimed.

## Acceptance still required

In a disposable Paratext project, inspect progress during a large scan and both kinds of bulk operation; verify the completion counts, stopped-batch recovery and reviewer-switch behavior; test repeated notes in the same verse, delayed book activation, groups A?E and None; apply one literal correction in each of the four non-body regions and verify saved/exported USFM. Live Scripture edits for these checks were confined to disposable NSTest.

Item 8 contained only an encoded space and was treated as blank.
