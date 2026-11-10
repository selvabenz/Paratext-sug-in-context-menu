# Selected bulk Scripture corrections

Requested and implemented 10 November 2026 in the development build.

## Workflow

1. In **Bulk resolve...**, check the proposals to consider and enter a decision comment. **Save decisions and resolve** retains the manual/no-change behavior and requires its existing acknowledgement.
2. Choose **Preview Scripture edits...** to preflight only those checked proposals. This step writes neither Scripture nor note decisions. Supported categories are Sandhi / word-joining, Confirmed typo, Possible typo, Plural agreement, Punctuation and Grammar. Unsupported or unsafe proposals remain unresolved; the session report records each skip reason.
3. Inspect the modeless before/after USFM preview and full discussion, opening the highlighted verse in Paratext as needed. Nothing starts checked. The comment carries forward and remains editable. Select the exact corrections to apply and explicitly approve their Scripture changes. Cancel writes nothing.
4. Apply each selected correction using the existing guarded batch engine. Recheck the source, exact target, approved passage, permissions and project identity; compare passage evidence again under the Scripture write lock. Verify saved USFM before marking Applied. Persist intent before submission, then the decision, reviewer and evidence before posting/verifying the note reply and resolving the completed thread.
5. Skip pre-write refusals and leave them unresolved. Persist each skip and every selection left unattempted after a stopped batch in **Tools > Manual review (N)**, with its reason and source snapshot. This list survives restart, spans all filters/books, and reopens the source in the full queue for individual review. Missing/changed sources retain their snapshots. Dismissing a reminder affects neither Scripture nor notes. Completed decisions leave the list automatically; pending writes and completed decisions awaiting note resolution use Recovery. Continue after a verified edit whose note reply fails, with resolution available in Recovery. Stop on uncertain Scripture/audit saves; recovery never blindly repeats the edit. Show final resolved, unresolved, verified, skipped and unattempted counts.

## Eligibility and limits

Explicit preview approval can satisfy a reviewer-decision flag for these six categories, including Possible typo. It does not enable automatic application in the context menu or loosen the older dedicated Sandhi/confirmed-typo actions. The category does not establish linguistic correctness; the user must assess each correction in context.

Require structured, nonduplicate Original/Suggested fields with one literal replacement. Refuse alternatives, English instructions, placeholders, deletions, USFM changes, non-body targets, multi-verse anchors, stale/unreadable discussions, superseded source comments, competing corrections, anchor warnings and ambiguous identities. Tamil letters/marks, ordinary spaces and a small punctuation allowlist are supported. Punctuation proposals may replace punctuation alone if the occurrence is unambiguous. Other categories require Tamil words. Exact matching preserves Unicode; there is no normalization or inference.

Edits stay within one text token and preserve complete word boundaries. Multiple matches require uniquely identifying note context. Independent corrections in the same verse conservatively require a new preview after the first edit. There is no batch transaction or batch undo; inspect Paratext history for earlier edits.

## Validation

Run `scripts/build.ps1` against the installed Paratext interfaces and the complete synthetic suite. New checks cover each category, review-required proposals, punctuation-only edits, unsafe inputs, stale text/source, under-lock changes, selected-only preview, cancel, comment transfer, approval gating, mixed categories and uncertain saves. Existing tests cover recovery, thread completion, same-verse staleness and old dedicated category safeguards.

Before production use, validate the new entry point in a disposable Paratext project, including narrow/high-DPI rendering, restart recovery, export and a controlled Send/Receive round trip. Synthetic tests do not establish live-host acceptance. Development tests do not edit production Scripture.

10 November 2026: compiled successfully against the installed Paratext interfaces; **180 synthetic checks passed, zero failed**. The default-size bulk review dialog was rendered and inspected. Build log: `artifacts/selected-bulk-build.log`. Development plugin: `artifacts/bin/ParatextNoteSuggestions.ptxplg`. The suite required execution with atomic file replacement available in disposable workspace test output; the sandbox's denial of `File.Replace` caused the initial run to fail. Live-host, high-DPI, restart, export and Send/Receive acceptance for this new workflow remain pending.

The persistent manual-review follow-up passed **184 synthetic checks, zero failed**. Additional checks cover saved reminders across reopen, draft/decision preservation, reopening outside the current filters, missing-source snapshots, reminder dismissal and retention of unattempted selections after uncertain saves. The manual-review dialog was rendered and inspected at its default size. Log: `artifacts/selected-bulk-install-build.log`; screenshot: `artifacts/bin/skipped-manual-review.png`.

## Installation record

10 November 2026: installed the tested build from feature commit `9e4562c` with Paratext closed, using the checksum-verified installer and its official plugin-cache update step. The previous plugin is backed up under ignored local staging at `artifacts/install-selected-bulk-9ab7cb9b926f434d9060e5d0bfe49f71/ParatextNoteSuggestions.previous.ptxplg`. The installed plugin's SHA-256 matches the tested build: `F1019F8CE4FF741DBB28157165468D9A74344FB3B894712A385DB7F6A578408F`.

Installation did not open a project or apply Scripture corrections. Disposable-project live-host acceptance remains pending; installation and synthetic checks do not establish export or Send/Receive acceptance.
