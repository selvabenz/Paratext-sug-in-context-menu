# Reviewed bulk Sandhi application

Requested 9 November 2026. Implemented in the development plugin and installed with the confirmed-typo update at the user's request on 10 November 2026. Live-host acceptance remains pending; see CONFIRMED_TYPO_PLAN.md for the installation record.

## Safety decision

Sandhi / word-joining suggestions can be applied in a reviewed batch when their exact literal targets are unambiguous. The category does not establish linguistic correctness. A Tamil reviewer must approve each selected correction in context. The plugin does not generate Sandhi rules or infer missing letters, normalize Unicode, or apply every category item automatically.

Keep **Bulk resolve...** as the existing manual/no-change decision action. Use the separate **Tools > Bulk apply Sandhi...** action when Scripture edits are intended. Both use the current queue filters.

## Plan implemented

1. Preflight unresolved, unfinished Sandhi proposals in the filtered queue. Exclude duplicate identities, interrupted applies, unreadable/changed discussions, competing corrections, reviewer-decision flags, anchor warnings, non-body regions and multi-verse targets. Require the normal automatic-literal route and editing permission. Restrict both strings to Tamil letters/marks and ordinary spaces. Punctuation, numbers, alternatives, instructions, deletions and structural operations require individual review.
2. Locate the exact original inside one Scripture text token. Require a unique match or uniquely identifying anchor context and complete word boundaries inside that token. Refuse stale targets and matches crossing formatting tokens. Do not loosen or normalize matching.
3. Open an unchecked selection list showing the original, proposed replacement, full source discussion and before/after USFM of the anchored verse. Require a shared decision comment and an explicit acknowledgement approving every selected Scripture correction. Cancel performs no writes.
4. Revalidate source and preview immediately before each edit. Reuse the existing history checkpoints, Scripture write locks, permission checks, source revalidation and whole-chapter saved-USFM verification. Under the lock, compare the before/after passage with the approved preview. A changed passage is skipped without a write. Edits in the same verse conservatively require a new preview after the first edit, including independent corrections.
5. Persist Pending apply intent with expected before/after evidence before submission. Mark Applied only after saved-USFM verification. Save the reviewer, exact strings, scope and comment. Post and verify the attributed discussion reply, then resolve only when all thread proposals have completed decisions and verified replies.
6. Log and skip ambiguous matches, formatting boundaries, reviewer-decision proposals and other pre-write refusals; update status and continue with the next proposal. Also continue after a note-reply/resolution failure when the Scripture edit has been verified and its Applied decision saved durably. Keep that note unresolved in Recovery. Stop only after a failure following durable intent when the saved Scripture/audit is uncertain. Leave remaining selections unattempted. Resolution retries never repeat Scripture edits.
7. Finish with explicit resolved and not-resolved proposal totals, verified Scripture edits, skipped items and unattempted selections. Include preview exclusions in the not-resolved/skipped totals; eligible proposals the reviewer did not select are outside these totals. Count resolution from final saved thread status, not simply from successful Apply calls. Save each skipped reference/reason immediately, plus the final summary, in the durable session log and HTML session report. Partial batches are possible; there is no batch transaction or batch undo. Existing checked session revert covers only the last plugin edit. Use Paratext history for earlier edits, inspecting subsequent work before restoring.

## Validation and release gate

- Compile against installed Paratext 9.5 interfaces and run the full synthetic regression suite.
- Exercise joining/splitting, changed source/text, changes between preflight and lock, ambiguity, partial words, uncertain-save replay prevention, and note-reply recovery without another Scripture write.
- Before production use, validate in a disposable Paratext project: preview/cancel, multiple verses, two proposals in one verse, locked/read-only chapters, a failed reply, restart recovery, Tamil display and narrow/high-DPI windows.
- Confirm export and a controlled Send/Receive round trip in a test project. Existing production synchronization acceptance remains pending.
- Complete disposable-project acceptance before production use. No production Scripture was changed while implementing this feature.

## Local verification result

9 November 2026: build succeeded against the installed Paratext interfaces; **155 synthetic checks passed, zero failed**. This includes eight new bulk Sandhi checks (some covering several cases) and the existing regression suite. The rendered Tamil before/after dialog was inspected at its default size. Test log: `artifacts/sandhi-build.log`. Development binary: `artifacts/bin/ParatextNoteSuggestions.ptxplg`.

The sandbox initially denied Windows atomic file replacement in local test output. The suite passed when run with that filesystem operation available; no production project was used. This does not establish live-host, restart, DPI, export or Send/Receive acceptance for the new feature.

## Skip-and-continue update

The 9 November follow-up adds per-item skip logging/status, preview exclusion counts, final resolved/unresolved totals and continuation after durable applied decisions whose note replies fail. Mixed-batch synthetic checks cover an ambiguous first target followed by a successful correction, reviewer-only exclusions, preflight counts and a failed reply followed by another successful correction. These tests do not edit production Scripture.

Follow-up verification: **159 synthetic checks passed, zero failed**; development plugin rebuilt successfully. This was followed by the installed confirmed-typo update with **168 synthetic checks passed, zero failed**. Live-host acceptance remains pending.
