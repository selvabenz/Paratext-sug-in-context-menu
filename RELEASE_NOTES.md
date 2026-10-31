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
