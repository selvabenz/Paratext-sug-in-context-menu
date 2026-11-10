# Reviewed confirmed-typo application

Requested 10 November 2026. Implemented and installed at the user's request on 10 November 2026. Live-host acceptance remains pending.

## Safety decision

The `Confirmed typo` category is a proposal label, not proof that a correction is right. A Tamil reviewer must approve the exact replacement in context. No project-wide find/replace, inferred spelling correction, Unicode normalization, or automatic selection is performed. `Possible typo` stays outside this action.

## Implementation plan

1. Add **Review note queue > Tools > Bulk apply confirmed typos...**, scoped to the current queue filters. Keep manual **Bulk resolve...** separate.
2. Reuse the reviewed Sandhi transaction and recovery flow. Admit only unresolved, unfinished, unambiguous `Confirmed typo` proposals with the literal Apply route, Tamil letters/marks and ordinary spaces, a single verse, body text, editing permission, and no reviewer-decision or anchor warning. Reject instructions, punctuation, numbers, empty replacements, structural edits, partial words, formatting boundaries, competing corrections, duplicate identities and pending apply records. Unsupported proposals remain available for individual review.
3. Build exact before/after USFM from an in-memory copy. Open an unchecked, read-only preview showing the full source discussion. Require selected corrections, an explicit approval acknowledgement and a nonblank decision comment. Cancel and closing the preview perform no Scripture or note writes. Existing local draft/session metadata may still be saved.
4. Keep the preview modeless so the reviewer can inspect Paratext. **Open selected verse in editor** navigates the pinned project to the selected proposal without changing text or approving it. Freeze queue decision/filter controls until preview closes. The installed Paratext 9.5 public interfaces expose verse navigation but no supported editor preview/overlay API. Therefore the proposed text appears in the plugin preview beside the editor; it does not appear as unsaved replacement text inside Paratext. Never simulate preview by writing then undoing Scripture.
5. After approval, re-read the source and regenerate the exact preview before each correction. Take a history checkpoint, acquire the Scripture write lock, re-read source/text and editing permission, and compare the resulting passage against the approved before/after evidence under the lock. A changed verse is skipped, including a second correction in a verse already changed by this batch. A new preview is required for those remaining corrections.
6. Save durable `Pending apply` intent and expected evidence before submission. Verify the whole saved chapter against the expected USFM. Only then persist `Applied`, reviewer, exact strings, scope and comment. Post and verify the attributed discussion reply; resolve only when all proposals in that thread have decisions and verified replies.
7. Skip pre-write refusals with durable reasons and continue. Continue after reply failure only when the verified Applied decision is durable; leave the note in Recovery. Stop after any uncertain save/audit, leave later selections unattempted, and block automatic replay of the pending edit. Resolution recovery never repeats Scripture edits.
8. Report resolved, unresolved, verified-edit, skipped and unattempted totals, including preview exclusions. Eligible unselected items are outside these totals. Preserve the existing checked last-plugin-edit revert. There is no batch transaction or batch undo; older edits require careful review in Paratext history.

## Verification and release gate

Compile against the installed Paratext 9.5 interfaces and run the full synthetic regression suite, including confirmed-typo approval/cancel/navigation, exact apply/evidence, stale text/source, changes under the lock, permission/lock refusal, unsafe targets, uncertain-save replay protection and note-reply recovery.

Before production use, validate in a disposable Paratext project: Tamil rendering; narrow/high-DPI layouts; reading and editing in the editor while preview remains open; cancellation; changed preview refusal; same-verse selections; locked/read-only chapters; restart recovery; export and controlled Send/Receive. Synthetic tests do not establish these host behaviors. No production Scripture is changed by development tests.

## Local verification result

10 November 2026: build succeeded against the installed Paratext interfaces; **168 synthetic checks passed, zero failed**. This includes nine new confirmed-typo checks, several with multiple safety cases, plus the prior regression suite. The rendered Tamil preview was inspected at its default size. The same-verse test explicitly orders its two selections to check that the second proposal is skipped after the first changes the verse.

Test log: `artifacts/confirmed-typo-build.log`. Preview image: `artifacts/bin/confirmed-typo-preview.png`. Development binary: `artifacts/bin/ParatextNoteSuggestions.ptxplg`.

The sandbox initially denied Windows atomic file replacement in local test output. The complete suite passed with that filesystem operation available. No live project was used. This does not establish live-host, restart, DPI, export or Send/Receive acceptance.

## Installation record

10 November 2026: installed the tested build with Paratext closed, using the checksum-verified installer and official plugin-cache updater. The previous installed binary was backed up under ignored local installation staging. The installed file's SHA-256 matches the tested build: `DA6BA0ED91944EE416CA43FCE33CDB0DF84180EA8B407DA6D0C85FEE078BA41F`. Installation did not open a project or apply Scripture corrections.
