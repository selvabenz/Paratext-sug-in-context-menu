# Paratext sug in context menu

Show suggestions from existing Paratext project notes in the Scripture editor's right-click context menu.

Target: **Paratext 9.5.110.1 on Windows**.

## Status

**0.3.0-preview.1** is a compiled Windows plugin built against Paratext 9.5.110.1 interfaces (2.0.100.0), targeting .NET Framework 4.8. It includes 124 passing checks and real-host tests for loading, exact Scripture/footnote writes, ambiguity refusal, and revert in a disposable project. Export/Send-Receive acceptance remains pending; this is a prerelease.

Get the package from [GitHub Releases](https://github.com/selvabenz/Paratext-sug-in-context-menu/releases). Read [INSTALL.md](INSTALL.md), [VALIDATION.md](VALIDATION.md), and the [developer handoff](DEVELOPER_HANDOFF.md) before using it.

The user's screenshot shows QA project notes with `Original:`, `Suggested:`, and `Reason:` fields. The menu should surface the existing `Suggested:` correction for the relevant passage. Automatically generating suggestions is outside the initial scope.

## Review thousands of notes

Choose **Review note queue…** in the Scripture right-click or Tools menu. The modeless queue scans unresolved AI-QA proposals across the selected project in Scripture order and opens each passage. Read the full discussion in its tab, choose a literal correction, check the review box, then **Apply and next**. **Reject and next** requires a comment. Nothing advances on a timer.

You can browse reference windows for as long as needed. **Return to queued passage** restores the queued target; browsing never retargets its write. Use **Pause**, **Resume**, **Leave for later**, or close the window. Reopening resumes saved progress and drafts. Reviewed items are skipped. An interrupted/unverified apply is held for explicit reconciliation before another attempt.

Comments and decisions are stored locally on this Windows account and can be exported to CSV. They are not appended to source notes or synchronized by Send/Receive. After each new queue decision, the original note is resolved once every QA proposal in its thread has a saved decision. Paused/deferred items stay open. Resolution failures pause with a separate retry that never repeats the Scripture edit. Complex corrections can be performed in Paratext, then recorded with **Completed manually / no change** and a comment. Duplicate source identities require manual disambiguation before a queue decision can be recorded.

## Comment presets and session reports

The queue offers five reusable decision phrases compiled from resolved-note themes with the user's approval. Selecting one fills the editable comment box; it does not apply/reject or change Scripture. The phrases cover accepted, accepted with revision, existing wording retained, meaning-sensitive rejection, and already corrected/no change. They are not presented as five repeated quotations from the user's account.

Pause or close the queue to save a timestamped, offline HTML report in the original project's **Review Queue** folder. Reports show full passage snapshots before/after, highlighted changes, exact targets/replacements, comments, Paratext reviewer identity, UTC times, decisions and note resolution. A report is cumulative for the currently open queue session; closing and reopening starts a new session. Pausing twice with no new actions reuses the saved report. Empty sessions create no report.

**View latest report** opens the report inside a modeless plugin window, with an **Open in browser** option. Search and decision filters are included. Report folder identity is checked against Settings.xml; choose **Report folder...** if automatic detection cannot find the original project.

Applied snapshots are captured under the Scripture lock and labeled verified only after saved USFM verification. Rejected text remains unchanged by the queue. Manual comparisons show text observed at review load and decision time; the reviewer does not establish who made a manual edit. Unverified attempts remain clearly labeled. Older decisions have no invented before/after context or reviewer names. Session audit data is durable locally so an interrupted session's report can be recovered.

## Intended workflow

1. Select a word or phrase, or place the cursor in a verse.
2. Right-click and select **Note suggestions…** to open a live suggestions popup.
3. See the proposed corrections from the `Suggested:` fields of relevant unresolved QA notes.
4. Click a correction to replace its exact anchored word, sentence, or verse after the original text is revalidated. A separate **View source QA note** action opens the explanation.
5. Save the actual project Scripture through Paratext's supported USFM editing API. The revised project text must be used by subsequent exports and normal Send/Receive.

Exact replacement and saved USFM are implemented for supported exact token-local corrections. The plugin uses Paratext's write locks and token APIs, verifies the saved result, and provides a checked session revert. Complex structural transformations and edits spanning text tokens remain review-only. The one-off suggestion picker leaves notes unresolved; the project-wide review queue resolves completed threads.

The installed public interface has no supported dynamic submenu builder. The release uses a fixed native context-menu command followed by a live popup, adding one click compared with the original mockup. This limitation remains open; see the handoff for the inspected API surface.

## Build and package

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

Requires Windows with .NET Framework 4.8 and a local Paratext 9.5.110.1 installation. The host interfaces are referenced locally and are not distributed. Build output and historical mockups are ignored by Git.

See [PLAN.md](PLAN.md) for the interaction design, feasibility checks, milestones, and acceptance criteria.

The actual IRVTam notes have been inspected locally. See [CATEGORY_HANDLING.md](CATEGORY_HANDLING.md) for the 16 observed QA categories and distinct handling of literal corrections, alternatives, source-review decisions, introductions, footnotes, and USFM structure.

## Development references

- [Official Paratext demo plugins](https://github.com/ubsicap/paratext_demo_plugins)
- [Context-menu example: Reference Plugin B](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginB/PluginB.cs)
- [Selection example: Reference Plugin J](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginJ/ControlJ.cs)
- [Project notes example: Reference Plugin L](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginL/ControlL.cs)

Keep real Scripture projects, notes, credentials, and installed Paratext binaries out of this repository. Development examples should use synthetic text.
