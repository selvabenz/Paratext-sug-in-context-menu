# Unreleased - fast review (branch fast-review)

- **Fast review tab** in the review queue: one key per note (F5 apply, F6 reject, F7 fixed by hand, F8 footnote, F9 skip, PgUp/PgDn browse, F11 undo), with comments filled in automatically by category and outcome. The apply key writes exactly the before/after shown; a refusal writes nothing and leaves the note in place. Function keys by default; single letters are opt-in.
- **Undo** of the last fast decision while its note reply is held. The reply is posted at the next action.
- **Footnote composer** with editable templates (சில பிரதிகளில் … இவ்வாறு உள்ளது / காணப்படவில்லை and the project's existing forms). It inserts one `\f + \fr C:V \ft …\f*` after the note's Original or at the verse end, validated by Paratext's reparse.
- **Notes without ready-to-paste Tamil**: stop for a manual edit, leave for a later round, or use an imported wording sheet labelled as not from the note.
- **Reference texts** (ESV, Tamil BSI) beside the note when Paratext allows plugin access; otherwise opened as Paratext windows. **Host probe** on the Tools tab.
- **More notes arm the apply key**, always with the note's own Tamil: an English remark in brackets is dropped, `A / B` and `A (or B)` alternatives are offered one at a time (F4 shows the next), and a phrase is taken from `follow the ESV: …`, `follow the reference text (…)`, `or read …` or `(reviewer: '…')`. Examples after `e.g.`, footnote wording, headings to add and partial text are never taken. A note that warned its Original was missing is armed only if the live verse now has it exactly once.
- **Headings, footnotes and cross-references**: words a note quotes from a heading, footnote or cross-reference without saying so are found there (never the other way round). A note that gives exact USFM on both sides (footnote or cross-reference text, a word beside a footnote call, heading text or level, poetry indent, removing or adding a whole footnote) is applied as one replacement, validated by Paratext's reparse; numbering, note boundaries and everything outside the note's verses must be unchanged, and a Suggested line that gives only the changed part is refused.
- 294 synthetic checks pass. Not yet tested inside Paratext; see [FAST_REVIEW.md](FAST_REVIEW.md) and the checklist in [VALIDATION.md](VALIDATION.md).

# 0.4.1-preview - 19 November 2026

First published 0.4 release (0.4.0-preview was not published: the release build's tests exceeded the Windows path limit on GitHub's runner).

- **Windows installer and automatic releases.** Each release now includes `ParatextNoteSuggestions-<version>-Setup.exe`, which finds Paratext, waits for it to close, installs the plugin, refreshes the plugin list and adds an entry to Windows' installed apps. Every push to `main` that changes the plugin builds, tests and publishes a release on GitHub. Versions come from `VERSION` plus the commit count; the plugin reports the same version to Paratext.
- **Review queue navigation fix.** Previous, Next, Return to passage, Apply + next, Reject + next and Completed manually again bring the editor to the queued verse and its highlighted target. The queue no longer trusts a single navigation request: it keeps checking the project editor and sends the reference again if Paratext moves it. See [the navigation fix report](NAVIGATION_FIX_REPORT.md).
- **Docked queue window.** **Review note queue…** opens as a Paratext window that can be docked beside the editor, tabbed, floated or auto-hidden, and is restored with Paratext's layout.
- **Paratext look and responsive layout.** All plugin windows use Paratext's theme colours, icons, toolbar and status bar, standard command buttons and dialog button placement, and reflow or scroll in narrow windows instead of clipping.
- 228 synthetic checks pass. Navigation and docking were confirmed by the user in Paratext 9.5.110.1; the theme in Bright/custom themes and at 150/200% scaling still needs live acceptance.

# Installed development update - 13 November 2026

- Show the current queue suggestion as a green inline label before its exact yellow Original highlight. Right-click the label or text to Apply, Reject or Ignore for now.
- Apply/Reject use the existing verified write, attributed reply, thread resolution and recovery flow. Ignore preserves the draft and leaves the note unresolved. Stale clicks cannot decide another proposal.
- Keep comment confirmations reachable from the taskbar when the queue is minimized; focus the comment field when they open.
- All 215 synthetic checks pass. Visual acceptance in Paratext 9.5.110.1 NSTest passed for the label, tooltip, menu, Apply/Reject/Ignore, advancement and annotation cleanup. All 763 protected Scripture/note files outside NSTest remain unchanged.
- Installed SHA-256: `1ACC17E0E01CECC570046D23915A52B05F9F02D978F49BD82126409D7E91FEF2`. See [the validation record](TARGET_HIGHLIGHT_PLAN.md) for scope and remaining DPI/synchronization limits.


# Installed development update — 6 November 2026

- Save attributed decision replies in original Paratext note discussions, verify persistence before resolution, and avoid duplicate replies on retry.
- Add persisted category/discussion search and selectable bulk manual/no-change resolution with a required shared comment. Bulk mode does not edit Scripture.
- Compact Review / Source discussion / Tools layout and styled primary action.
- All 144 synthetic checks pass. Real Paratext NSTest tests passed for single/bulk replies, Tamil content, source resolution and restart persistence; production Scripture/notes remained unchanged.
- Installed binary SHA-256: `D24717ABC33FF5A764532B1D9BA65C9D9F7E76F4A65FD89B2BC47C9F3BB15AFF`.
- Send/Receive with a configured second test reviewer and the complete DPI/monitor matrix remain pending. Issue #5 remains open.

# Local development update - 10 November 2026

- Add **Tools > Bulk apply confirmed typos...** using the reviewed Sandhi exact-match, write-lock, saved-USFM verification and recovery flow. Possible typos remain excluded. See [the safety plan](CONFIRMED_TYPO_PLAN.md).
- Keep Sandhi and confirmed-typo previews modeless, with read-only before/after USFM, source discussion and **Open selected verse in editor** navigation. Applying requires checked selections, approval and a decision comment. The installed public API has no supported preview overlay inside the Scripture editor.
- Recheck Scripture editing permission under the write lock. Changed previews are skipped; uncertain saves stop the batch and prevent automatic replay.
- Installed at the user's request with Paratext closed; the installed SHA-256 matches the tested build (`DA6BA0ED91944EE416CA43FCE33CDB0DF84180EA8B407DA6D0C85FEE078BA41F`). All **168 synthetic checks pass**. Live-host validation remains pending. No production Scripture or notes were edited during implementation or installation.

# 0.3.0-preview.1

Adds editable reusable comments and full session review reports.

- Five compiled decision phrases in a dropdown, with free typing/editing retained. The user chose reusable summaries after resolved-note inspection found no five repeated personal decision comments.
- Automatically save timestamped offline HTML under the original project's Review Queue folder when pausing/stopping/closing a nonempty session.
- Show full before/after anchored text, Tamil-safe highlighted differences, exact target/replacement, comments, authenticated Paratext reviewer, UTC decision/action times and resolution status.
- Provide a modeless report viewer inside the plugin, plus Open in browser, search, decision filters and printing.
- Persist full audit snapshots before submission; clearly distinguish verified writes, unchanged rejections, manual observations and unverified attempts.
- Recover interrupted session reports and preserve older decisions without inventing historical context or reviewer identity.
- Verify the project folder identity; report failures preserve local decisions and offer retry.
- Continue scanning past unreadable legacy-language discussions with visible report warnings; block decisions on unreadable sources and preserve other readable notes.
- 125 automated checks pass. Installed-host preset selection, exact Apply, unchanged Reject, source resolution and embedded HTML report acceptance passed in disposable NSTest.
- IRVTam scanning completed with 16,270 proposals and 215 explicit legacy-discussion warnings. The queue was left paused; all 85 production Scripture/notes files matched the pre-install baseline. See VALIDATION.md for evidence and remaining export/Send-Receive limits.

---

# 0.2.0-preview.2

Reviewed queue decisions now resolve their original Paratext notes while comments/progress remain local.

- Apply, Reject, and Completed manually / no change save the decision/comment before resolving the source note.
- Threads containing multiple structured proposals stay open until every proposal has a saved decision.
- Use a separate project-notes lock, fresh discussion checks and post-resolution verification.
- Resolution failures retain the saved decision with a dedicated retry; no Scripture correction is repeated.
- Recover pending resolution even if the note was already resolved before a restart. Missing sources remain visible as recovery work.
- Pause/defer/close leave pending notes open; older preview.1 decisions are not bulk-resolved on upgrade.
- Preserve local comments, CSV export, and normal Paratext synchronization of note status.
- 104 synthetic checks pass. Real-host Apply-and-resolve and Reject-and-resolve acceptance passed in disposable NSTest, including exact USFM comparison, local-only rejection comments, verified resolution and next-note navigation. Production Scripture/notes were unchanged.

---

# 0.2.0-preview.1

Adds a modeless review queue for large QA-note collections in Paratext 9.5.110.1.

- Index unresolved structured QA proposals across the selected project and navigate in Scripture order.
- Wait for explicit Apply or Reject, then proceed to the next note. Reject requires a comment; Apply comments are optional.
- Browse Paratext reference windows without changing the queued edit target or triggering a timer.
- Pause/resume, leave for later, close/reopen and retain current drafts and recorded decisions.
- Save comments/decisions locally and export CSV; source notes stay unresolved and comments are not synchronized or appended to source discussions.
- Persist pending apply intent before writing. Interrupted/unverified edits stop for explicit manual reconciliation, preventing blind retries.
- Keep existing category routing, exact-target validation, permissions, locks and saved-USFM verification. Complex structural work remains manual and can be recorded with a comment.
- 94 passing synthetic checks, including queue advance, restart/draft recovery, changed sources, duplicate identities, interrupted writes, Unicode comments and 16,000 pending lookups. This is not a benchmark of 16,000 real-host edits.

The existing writer's real-host body/footnote/revert validation remains applicable. The new queue has loaded 16,270 unresolved proposals in the real host, opened the first passage, and paused without changing Scripture or notes. Disposable-project acceptance also passed for exact Apply-and-next, commented Reject-and-next, preserved Footnote region, and reference browsing while the queued target remained fixed. Full export and Send/Receive acceptance remain pending. See INSTALL.md and DEVELOPER_HANDOFF.md for use, recovery and local progress limits.

---

# 0.1.0-preview.2

Fixes after real Paratext installation and testing:

- Move history checkpoints outside write-lock lifetimes to fix the real-host disposed WriteLock failure.
- Preserve the proposed content region when the review form is created; a new regression check catches the previous Footnote-to-Body reset.
- Replace the invisible modal popup owner with a visible native suggestion picker and fit the review window to the screen.
- Elevate installation/removal when needed and refresh Paratext's official plugin cache.
- Verify real-host body and footnote USFM edits, repeated-phrase refusal, and guarded revert in a synthetic project.

Initial compiled preview for Windows Paratext 9.5.110.1. This release includes a `.ptxplg`, installation/removal scripts, SHA-256 checksums, validation evidence and developer handoff.

## Included

- Scripture context-menu and Tools commands opening live note suggestions.
- Category-aware parsing for all 16 observed AI-QA categories and preservation of R2/P3 provenance.
- Exact word/phrase/sentence/verse-content replacements within a supported USFM text token, including region-specific review for body, headings, introductions, footnotes and cross-references.
- Review and explicit deletion workflows; source discussion retained, note resolution unchanged.
- Permission and lock checks, fresh source/current-text checks, post-write USFM verification, history-point requests and checked session revert.
- Narrow heading/chapter-label marker renames with unchanged labels and USFM round-trip validation.
- 80 passing synthetic automated checks and a desktop UI apply/save check using synthetic Tamil data.

## Known limits

This is a prerelease. Real-host loading and the write/revert cases above are validated; full export and Send/Receive acceptance remain pending. Do not interpret those checks as validation of every structural category.

The public menu API provides fixed registrations; corrections appear in a secondary popup after clicking Note suggestions, rather than directly in Paratext's existing context menu. Complex structural edits, verse rearrangements/bridges, note movement, cross-chapter edits, replacements spanning text tokens, Latin-text replacements and native tag filtering are not implemented as automatic edits. They remain review/manual workflows.

See INSTALL.md, VALIDATION.md and DEVELOPER_HANDOFF.md for the exact installation steps, test boundary and remaining stable-release work. No Paratext host assemblies or private notes are distributed.
