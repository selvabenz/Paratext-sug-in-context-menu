# Decision comments, review layout and bulk resolution

Requested 6 November 2026. Implemented as a development build; host acceptance remains pending.

## Issue submission

Created [GitHub issue #5](https://github.com/selvabenz/Paratext-sug-in-context-menu/issues/5): **Save decision comments in Paratext notes, streamline review UI, and add filtered bulk resolution**.

The connector returned HTTP 403 for issue creation. The local CLI login was verified successfully outside the network sandbox, with repository admin access; its earlier invalid-token report was caused by restricted network access. No credential reset was required. Issue #5 was created through the authenticated CLI and remains open for host acceptance.

## Plan and implementation

1. Save each completed decision as a reply in its original Paratext discussion using `IProjectNote.AddNewComment` under a project-notes write lock. Include source proposal ID, outcome, reviewer, UTC time, category, original/replacement and decision comment. Applied decisions with an empty comment use “Accepted as suggested.”
2. Read the discussion again to verify the exact reply before resolving. Stable reply identifiers and full-content matching prevent duplicate replies after interrupted resolution. A failure keeps the local decision pending for Recovery; Scripture is never reapplied to retry a note reply.
3. Preserve human-discussion conflict detection while excluding plugin decision replies from proposal revision keys. Check every proposal has a completed decision and its exact saved reply before resolving the thread. Resolved legacy notes without the expected reply require manual inspection. Older completed local decisions are not automatically migrated or posted.
4. Use a compact review header, consistent button spacing, a primary Apply action, shorter tabs, and a separate Tools area for navigation settings, reports and recovery. Preserve scrolling, modeless operation and saved bounds. Native docking remains the separately documented API limitation.
5. Extend book/status scope with exact category and case-insensitive discussion-text filters. Save them per project/user, with distinct resume cursors. Custom category text is supported; search can find verse alignment or USFM spacing discussions even when their category is broader.
6. Preview filtered proposals in Bulk resolve with an initially unchecked selection list and full-discussion preview. Require a shared comment and an explicit reviewed/no-queue-edit acknowledgement. Revalidate each source and permission, record a manual/no-change decision, post its reply, and resolve only complete threads. Never edit Scripture in this action. Skip changed/ambiguous/completed/interrupted sources; continue other selected items and report failures with pending recovery counts.

## Acceptance checklist

- [x] Build against the installed Paratext 9.5 interface assemblies.
- [x] Synthetic tests for reply persistence, comment failures, duplicate-free retries, shared threads, category/search persistence, bulk decisions and changed-source refusal.
- [x] Render and inspect narrow and wide queue windows using synthetic Tamil text.
- [x] Verify replies, reviewer attribution, Tamil content and resolution in real Paratext NSTest. Restart persistence also verified.
- [ ] Verify a controlled Send/Receive round trip and inspect replies from another reviewer account.
- [ ] Verify 100/150/200% DPI and multiple-monitor behavior on hardware.
- [x] Submit GitHub issue #5 through the authenticated CLI.

The build was installed through the checked installer and official cache updater on 6 November 2026. Its SHA-256 is `D24717ABC33FF5A764532B1D9BA65C9D9F7E76F4A65FD89B2BC47C9F3BB15AFF`. The previous installed plugin is backed up in ignored `artifacts/decision-host/previous-installed.ptxplg`.

Real-host checks used three new synthetic NSTest threads: a textual-basis rejection and two bulk manual/no-change decisions for Verse alignment and USFM marker. Paratext saved one attributed, Tamil-containing reply per thread and resolved each source. Bulk reported zero failures/pending recovery. The test Scripture remained byte-for-byte identical, and all 86 production Scripture/note files matched the immediate baseline. A normal Paratext restart retained the replies and resolved status. HTML session reporting was generated in NSTest's Review Queue folder.

The two-monitor desktop was used at its current display settings; this does not establish the full DPI/monitor matrix. NSTest is an unregistered local fixture with no configured second reviewer/test synchronization partner. Send/Receive acceptance remains pending; no production synchronization was initiated.
