# Implementation plan

Implementation update: a compiled **0.1.0-preview.2** now exists. This file retains the full target requirements. See [DEVELOPER_HANDOFF.md](DEVELOPER_HANDOFF.md), [RELEASE_NOTES.md](RELEASE_NOTES.md), and [VALIDATION.md](VALIDATION.md) for the implemented subset, the secondary-menu limitation, and pending real-host/export/Send-Receive acceptance checks.

## Objective and scope

For Paratext 9.5.110.1, surface existing project-note suggestions at the passage where the translator is working. The context menu should make relevant suggestions visible with minimal navigation.

Confirmed: Windows Paratext version 9.5.110.1.

The user's IRVTam notes have now been inspected locally. [CATEGORY_HANDLING.md](CATEGORY_HANDLING.md) records the 16 observed QA categories, native project tags, and category/region/proposal rules. These rules refine the generic workflow below: only verified literal corrections apply directly; alternatives, source decisions, and structural operations use their appropriate review or edit flow.

The supplied screenshot shows the Scripture editor's right-click menu alongside a QA project note containing `Original:`, `Suggested:`, `Reason:`, confidence, and review information. Use the `Suggested:` field as the correction preview and retain the other fields for review. The user subsequently confirmed that clicking a correction must replace the exact word, sentence, or verse in the project and affect the USFM used by exports and normal Send/Receive. The stored rich-text structure still needs verification.

Working assumption: unresolved notes are shown by default. A note can contain general discussion rather than a replacement instruction, so the plugin must preserve the distinction between a suggestion and an actionable text replacement.

## Proposed interaction

Conceptual menu, pending verification of dynamic submenu support:

```text
[Existing Paratext commands]
Note suggestions >
    [Suggested: proposed correction matching the selected phrase]
    [Suggested: another proposed correction for this verse]
    View all notes for this passage...
    Refresh suggestions
```

- Prefer notes whose anchors overlap the selected phrase; then show remaining notes anchored to the current verse. For a multi-verse selection, include overlapping note ranges.
- Without a selection, use the active verse. Do not assume the API identifies the word under the mouse or moves the active verse on right-click; verify that behavior.
- Show up to five previews, with an overflow action for additional results. Truncate at Unicode text-element boundaries, keep full text in the detail view, and render note contents as plain text in menu labels.
- Show an explicit empty state when no unresolved notes match. Distinguish unavailable notes, loading, and errors from an empty result.
- The detail view shows the full thread, passage, author, status, and anchor. Open the native note if a supported API exists; otherwise use a read-only plugin detail view.
- Clicking a correction applies the explicit Suggested value to the exact anchored Original span after a fresh match and permission check. A separate View source QA note action opens details without editing. Keep the note unresolved unless a separately specified action changes it.
- Replacement is required in the initial functional release. Never infer replacement text automatically from arbitrary prose or perform a global find-and-replace.
- Preserve existing Paratext context-menu commands and keyboard operation, including the Menu key / Shift+F10 where supported.

## QA note field extraction

- Read the stored note contents through the API and identify explicit `Original:`, `Suggested:`, and `Reason:` field boundaries across rich-text runs and paragraphs. Do not depend on screenshot OCR or bold styling.
- Display the `Suggested:` value in the context menu; show `Original:`, `Reason:`, confidence, and any human-review statement in the detail view.
- Preserve punctuation, whitespace within the proposed correction, and Tamil combining sequences. Do not strip meaningful punctuation while formatting labels.
- Use the note anchor and passage as the primary match. Use `Original:` as an additional check against the current text, accounting for repeated occurrences. If the original text has changed, mark the suggestion as needing review.
- Treat missing, empty, or multiple ambiguous `Suggested:` fields as review-only notes. Do not invent a correction or silently choose among conflicting comments.
- Keep each candidate tied to its source note and comment. Do not combine the original text from one comment with a suggestion from another.
- Validate extraction with synthetic QA notes representing rich-text split labels, multiline fields, punctuation-only corrections, missing fields, and conflicting suggestions.

## Technical evidence and feasibility gate

The official [Reference Plugin B](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginB/PluginB.cs) registers a command using `PluginMenuLocation.ScrTextContextMenuDefault`. This establishes a sample API location for a Scripture context-menu entry; it does not establish dynamically populated nested entries on version 9.5.110.1.

[Reference Plugin J](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginJ/ControlJ.cs) demonstrates `ActiveWindowSelectionChanged` and Scripture selection ranges. [Reference Plugin L](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginL/ControlL.cs) demonstrates `IProject.GetNotes`, note anchors, comments, and resolved state.

[Reference Plugin I](https://github.com/ubsicap/paratext_demo_plugins/blob/master/ReferencePluginI/ControlI.cs) demonstrates chapter write locks, `GetUSFMTokens`, `PutUSFMTokens`, and change notifications. It also demonstrates `PutUSFM`. These are evidence of a supported Scripture-writing pathway; their precise contracts and compatibility must be checked against the installed 9.5.110.1 interfaces.

Before choosing the framework or producing an installer:

1. Locate the installed 9.5.110.1 plugin interfaces and API documentation; inspect signatures and match a compatible official sample revision. Record assembly/API versions and the sample commit used.
2. Build a minimal C# plugin against the installed interfaces using their required .NET target and architecture. Do not guess the target from modern .NET defaults.
3. Verify loading, deployment location, and a static right-click command on a disposable test project.
4. Verify whether supported APIs permit nested menu entries, per-opening population, changing labels, and removing old entries. Test stale state after changing project, selection, or verse.
5. Verify whether the command receives the right-click passage or only active-window state, and how to read current selection without requiring a separate plugin window.
6. Verify available note identifiers, anchor ranges, note visibility, rich-text conversion, refresh notifications, and native note-opening support.
7. Record results. If direct dynamic previews are unsupported, document the limit and agree on a supported panel-based design before treating that alternative as the final solution. Avoid private UI hooks or process patching.
8. Verify Scripture write permissions, token offsets, persistence, editor refresh, write-lock release, and undo/recovery behavior in a disposable project. Do not claim native Ctrl+Z support until tested.

## Exact replacement and saved USFM

1. Capture the project identity, note/comment identity, reference range, anchor, Original, and Suggested values when creating the menu candidate. Match within the specified word, sentence, or verse, rather than expanding every edit to the whole verse.
2. On click, revalidate the current project and request the smallest supported Scripture write lock. Re-read the affected Scripture under that lock and revalidate the source note before changing anything. Reject a missing lock, read-only project, stale note, or changed Original.
3. Map the rendered anchored span to Scripture text tokens. An exact selection/anchor must distinguish repeated words; a unique match within the verified anchored verse may be used only when unambiguous. Never silently choose the first occurrence or search other verses for a missing match.
4. Modify only the mapped text, preserving chapter/verse numbers, paragraph and character markers, note/cross-reference content, milestones, and attributes. A full-verse suggestion replaces its Scripture content, not the verse marker or unrelated embedded notes. If a span crosses markup that cannot be mapped safely, stop with an explanation rather than flattening its structure.

   This preservation rule describes ordinary text replacements. A note explicitly proposing a USFM marker or footnote/cross-reference change requires the separate validated structural operation described in CATEGORY_HANDLING.md; it must not be applied through the ordinary text writer.
5. Save through the verified supported API, preferably the token-based pathway when it preserves the required structure. A chapter-level write may be required even for a word change; reconstruct it from the freshly read chapter and retain all unaffected tokens. Never patch the on-disk project files behind Paratext.
6. Send the required change notifications, release the lock in all outcomes, and re-read the saved text before reporting Applied. Refresh the editor and cached suggestions. A repeated click must not apply the same edit again.
7. Use the same project's normal export and Send/Receive workflow after the edit. The plugin does not automatically export or start synchronization. Verify an exported USFM sample and an authorized controlled synchronization round trip before claiming end-to-end behavior. Normal project access and merge/conflict handling still apply.

Keep a recovery design appropriate to the verified host API. Prefer native undo if supported; otherwise provide a tested explicit revert using the exact previous text and a fresh current-text check. Do not promise atomic rollback or native undo without evidence. Limit initial supported edits to a single chapter until cross-chapter transaction behavior is established; explain unsupported ranges without partial edits.

## Proposed components

| Component | Responsibility |
| --- | --- |
| Paratext adapter | Read active project, passage, selection, and notes using supported APIs. |
| Note matcher | Filter unresolved notes and compare anchors using the project's versification and supported selection information. |
| QA field parser | Extract explicit Original, Suggested, and Reason fields from the same source comment while preserving the correction text. |
| Preview formatter | Produce readable, bounded, Unicode-safe menu text without treating note prose as code or replacement instructions. |
| Context-menu integration | Register commands and, if supported, refresh previews for the captured context. |
| Detail view | Display the full note thread and its source context. |
| Replacement writer | Revalidate anchors and Original under a write lock, preserve USFM structure, save, notify, and verify the exact change. |

Keep project IDs and note IDs in internal models where the API provides them. Do not match projects by display name alone. Capture the context when opening the menu and revalidate it before opening a note. Discard late refresh results after a project switch or closure.

Use a short-lived in-memory cache scoped by project and passage only if measurement shows a need. Invalidate it on supported note-change events; otherwise refresh on command invocation and provide manual refresh. Follow the API's threading rules and never block a menu-opening callback on an unbounded scan of all project notes.

## Milestones

### 1. Prove API support

Deliver a minimal loadable plugin, a compatibility record, and evidence of context-menu and note-reading behavior on 9.5.110.1. Resolve the dynamic submenu feasibility question.

### 2. Implement note matching

Map API notes to internal models and handle selection overlaps, verse ranges, resolved state, project isolation, missing anchors, and empty threads. Use synthetic data for focused matcher checks, including Tamil text and repeated words in a verse.

### 3. Implement the context-menu experience

Add correction previews, exact replacement, and a separate full-note action using the supported design. Handle loading, no matches, errors, overflow, refresh, and stale context. Validate keyboard navigation and Tamil rendering manually in Paratext. Update the earlier read-only interactive mockup to demonstrate applying a synthetic correction and the resulting USFM.

### 4. Package and validate

Document installation and removal for the verified deployment mechanism. Test in a disposable project, collect screenshots and results, and publish a versioned build only after compatibility checks pass.

## Acceptance criteria

- Plugin loads on Paratext 9.5.110.1 without startup errors.
- A right-click on the intended passage exposes the note-suggestions feature without removing native commands.
- For the preferred design, matching suggestion previews are visible directly in the menu/submenu before opening a panel.
- For structured QA notes like the supplied screenshot, the preview contains the `Suggested:` correction, and the full view retains the original text, reason, and human-review statement.
- Selected-text anchors rank ahead of other notes for that verse; repeated words and verse ranges do not produce a falsely precise match.
- Resolved notes are excluded by default; inaccessible notes are never exposed.
- Clicking a correction updates only its exact intended word, sentence, or verse. The separate source-note action displays the correct full thread and reference.
- Switching projects, moving the selection, closing a project, and refreshing notes do not show another context's results.
- Empty notes, no matches, long threads, Tamil combining marks, and API failures produce readable states.
- Displaying the menu or viewing the source note does not edit Scripture. Clicking an applicable correction changes Scripture only, leaving the note status unchanged.
- Repeated words, stale originals, missing permissions, unavailable locks, and unsupported markup boundaries cannot cause a wrong or partial replacement.
- Saved USFM contains the intended correction and preserves unrelated text, markers, footnotes, cross-references, and Tamil combining characters.
- Verify persistence after reopening the project, exported USFM, and an authorized controlled Send/Receive round trip. Record any merge conflicts rather than claiming synchronization always succeeds.
- Verify undo or an explicit safe revert on the target version and failure behavior before shipping.
- Menu population targets under 100 ms for cached matching results; any slower reading uses a visible loading flow compatible with the host API. Measure this on the user's environment before claiming it is met.

## Remaining decisions

- Confirm whether structured QA notes should be restricted to a particular category/tag or whether any note containing the explicit fields qualifies.
- Exact replacement affecting saved project USFM is confirmed scope; native undo and recovery capabilities still need verification.
- Verify dynamic submenu support, framework target, SDK compatibility, installation path, and native note-opening support on the installed version.
