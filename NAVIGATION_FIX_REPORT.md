# Queue navigation and target highlight fix — 19 November 2026

Reported symptom: after **Previous**, **Next**, **Return to passage**, **Apply + next**, **Reject + next** or **Completed manually / no change**, the Scripture editor did not end on the queued verse and its target text.

## Status

- Built against the installed Paratext 9.5.110.1 interfaces; **226 synthetic checks pass, 0 fail**.
- **Not yet installed and not yet checked in the live host.** The cause below comes from reading the navigation code of the installed Paratext 9.5.110.1 assemblies and the 19 November `ParatextLog.log`; the failure was not reproduced on screen.
- No project, note or Scripture file was touched.

## Cause

All six buttons share one path: `LoadCurrent` / `NavigateCurrent` in [src/QueueForm.cs](src/QueueForm.cs). That path sent its requests once, in an order that Paratext's own delayed work could undo, and then accepted a confirmation that could not fail.

1. **The confirmation was not evidence.** The queue stopped as soon as a project window reported the target reference. Paratext sets that window property synchronously inside the plugin's own call, before the editor has scrolled. Anything that moved the editor afterwards went unnoticed and uncorrected.
2. **Every load hopped to a neighbouring verse first, 150 ms before the target.** The hop exists for one case: the editor already holds the target reference, so Paratext ignores the request. It was used for every load. Paratext postpones a reference change that arrives while another is being processed and runs it when the application is next idle, and each window that follows a hop raises its own change. A postponed neighbour change can therefore land after the target. For verse 1 the neighbour was the last verse of the previous chapter.
3. **The 18 November change widened this.** It sent the neighbour to the queue's selected scroll group as well, and it stopped re-sending the target to the editor's own group. The selected group is restored from the queue's saved layout (group B for both local projects), not from the editor. An editor in another group lost its only repeat request.
4. **The highlight re-rendered the editor twice per item.** Each `AnnotationsChanged` makes Paratext rebuild the editor's HTML for the project, on a later UI message. The queue raised it before the hop and again before the target, including when nothing had changed. The second rebuild ran after the target request.
5. **Decisions add more delayed work.** The log shows the project open in whole-book view, with each Apply followed by a whole-book reload, two history commits of 0.4–2.4 s each, and note-file change notifications arriving up to five seconds later. The single navigation request was sent in the middle of this.

In the disposable NSTest project the chapters are small and few windows are open, so these steps finished in order and the checks passed.

## Change

**Navigation ([src/QueueForm.cs](src/QueueForm.cs), [src/NoteService.cs](src/NoteService.cs))**

- The target reference is sent directly. The neighbouring-verse hop is used only when a project editor already reports the target verse, and the neighbour is chosen inside the same chapter when one exists.
- The hop goes to the project window only. Its own scroll group follows through Paratext; no other group is sent to the neighbour.
- After the request the queue keeps watching the project editor for about 1.2 s of unbroken agreement, up to 4.5 s in total. If the editor leaves the target in that time, the reference is sent again.
- Repeats go to the editor's own scroll group and the selected group through `SetReferenceForSyncGroup`, which does not activate a window, so typing in the queue is not interrupted. An editor with no scroll group can only be reached by the activating call; that is limited to two repeats.
- One repeat is always sent about 0.6 s after the request. Paratext ignores it when the editor is already there.
- Only windows that report a Scripture selection count as the project editor when any exist, so a notes list of the same project cannot confirm on its behalf. A verse bridge containing the target verse counts as the target.
- A failed navigation request is shown in the navigation line and logged. It no longer pauses the review.

**Highlight ([src/QueueTargetHighlight.cs](src/QueueTargetHighlight.cs))**

- `AnnotationsChanged` is raised once per changed target and before the navigation request, so the rebuild is finished when the editor is moved. Return to passage and a reload of the same proposal raise nothing.
- When the editor has to load another book, that load renders the new target. A rebuild is requested afterwards only if Paratext never asked for the target verse.
- Nothing is raised until Paratext has asked this layer for annotations, which it does only while **Highlight queued note target** is ticked. Reviewers who leave the layer off no longer pay for a rebuild on every item.
- A single occurrence of Original in the verse is highlighted even when the note's stored before/after context is out of date. Context is used only to choose between repeats, as the Scripture writer already does.
- A heading target is also looked for in the verse displayed before it, and an introduction target in the text before verse 1.
- The navigation line (Tools tab) now ends with the highlight result: highlighted, not found exactly once, layer not ticked, or not highlightable.

## Limits that remain

- Paratext's public plugin interface moves an editor to a **verse**. It has no call to place the caret on a word or to scroll to an offset inside a verse. The target text is marked by the highlight layer, which the reviewer must tick once per project window. In a very long verse the highlighted words can still be below the visible area.
- A proposal anchored on several verses, or whose Original is missing or repeated without usable context, gets verse navigation without a text highlight.
- During the 1.2–4.5 s watch, clicking another verse in the project editor is treated as drift and the editor is sent back. Reference windows in other groups are not affected.
- If the project is open in two windows that belong to no scroll group, only the one Paratext picks for the project is moved.

## Checks added

Five synthetic checks were added to [tests/QueueTests.cs](tests/QueueTests.cs) and four rewritten. Together they cover: direct navigation to another book without a hop; hop only for the same verse; hop not broadcast to the selected group; repair of a late neighbour echo through the editor's own group when the queue's saved group differs, with no extra window activation; bounded repeats and at most two activating repeats for an editor that never confirms; a failed request leaving the review running; one rebuild per target and none while the layer is off; stale anchor context; heading and introduction placement.

## Acceptance still required in Paratext

Install with Paratext closed, then in a disposable project that is large enough to load slowly, with the usual window layout:

1. Tick **Highlight queued note target** and use each of the six buttons across verses, chapters and books, in whole-book and by-chapter view.
2. Repeat with the project editor in a scroll group different from the queue's, and in no group.
3. Apply two proposals in the same verse and confirm the second highlight appears.
4. Scroll the editor away and use **Return to passage**.
5. Confirm that typing a comment immediately after **Next** keeps the keyboard in the queue.
