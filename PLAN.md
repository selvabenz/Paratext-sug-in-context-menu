# Implementation plan

## Objective and scope

For Paratext 9.5.110.1, surface existing project-note suggestions at the passage where the translator is working. The context menu should make relevant suggestions visible with minimal navigation.

Confirmed: Windows Paratext version 9.5.110.1.

The supplied screenshot shows the Scripture editor's right-click menu alongside a QA project note containing `Original:`, `Suggested:`, `Reason:`, confidence, and review information. Use the `Suggested:` field as the correction preview and retain the other fields for review. The screenshot demonstrates the desired source format; it does not establish the stored rich-text structure or authorize automatic replacement.

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
- Initial release reads notes and displays suggestions. Selecting a preview opens details; it does not change Scripture text or resolve a note.
- Applying an explicitly structured replacement can be a later feature, after source/target mapping, write locks, permissions, and undo behavior are validated. Never infer a replacement automatically from arbitrary prose.
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

Before choosing the framework or producing an installer:

1. Locate the installed 9.5.110.1 plugin interfaces and API documentation; inspect signatures and match a compatible official sample revision. Record assembly/API versions and the sample commit used.
2. Build a minimal C# plugin against the installed interfaces using their required .NET target and architecture. Do not guess the target from modern .NET defaults.
3. Verify loading, deployment location, and a static right-click command on a disposable test project.
4. Verify whether supported APIs permit nested menu entries, per-opening population, changing labels, and removing old entries. Test stale state after changing project, selection, or verse.
5. Verify whether the command receives the right-click passage or only active-window state, and how to read current selection without requiring a separate plugin window.
6. Verify available note identifiers, anchor ranges, note visibility, rich-text conversion, refresh notifications, and native note-opening support.
7. Record results. If direct dynamic previews are unsupported, document the limit and agree on a supported panel-based design before treating that alternative as the final solution. Avoid private UI hooks or process patching.

## Proposed components

| Component | Responsibility |
| --- | --- |
| Paratext adapter | Read active project, passage, selection, and notes using supported APIs. |
| Note matcher | Filter unresolved notes and compare anchors using the project's versification and supported selection information. |
| QA field parser | Extract explicit Original, Suggested, and Reason fields from the same source comment while preserving the correction text. |
| Preview formatter | Produce readable, bounded, Unicode-safe menu text without treating note prose as code or replacement instructions. |
| Context-menu integration | Register commands and, if supported, refresh previews for the captured context. |
| Detail view | Display the full note thread and its source context. |

Keep project IDs and note IDs in internal models where the API provides them. Do not match projects by display name alone. Capture the context when opening the menu and revalidate it before opening a note. Discard late refresh results after a project switch or closure.

Use a short-lived in-memory cache scoped by project and passage only if measurement shows a need. Invalidate it on supported note-change events; otherwise refresh on command invocation and provide manual refresh. Follow the API's threading rules and never block a menu-opening callback on an unbounded scan of all project notes.

## Milestones

### 1. Prove API support

Deliver a minimal loadable plugin, a compatibility record, and evidence of context-menu and note-reading behavior on 9.5.110.1. Resolve the dynamic submenu feasibility question.

### 2. Implement note matching

Map API notes to internal models and handle selection overlaps, verse ranges, resolved state, project isolation, missing anchors, and empty threads. Use synthetic data for focused matcher checks, including Tamil text and repeated words in a verse.

### 3. Implement the context-menu experience

Add previews and full-note viewing using the supported design. Handle loading, no matches, errors, overflow, refresh, and stale context. Validate keyboard navigation and Tamil rendering manually in Paratext.

### 4. Package and validate

Document installation and removal for the verified deployment mechanism. Test in a disposable project, collect screenshots and results, and publish a versioned build only after compatibility checks pass.

## Acceptance criteria

- Plugin loads on Paratext 9.5.110.1 without startup errors.
- A right-click on the intended passage exposes the note-suggestions feature without removing native commands.
- For the preferred design, matching suggestion previews are visible directly in the menu/submenu before opening a panel.
- For structured QA notes like the supplied screenshot, the preview contains the `Suggested:` correction, and the full view retains the original text, reason, and human-review statement.
- Selected-text anchors rank ahead of other notes for that verse; repeated words and verse ranges do not produce a falsely precise match.
- Resolved notes are excluded by default; inaccessible notes are never exposed.
- Opening a preview displays the correct full thread and reference.
- Switching projects, moving the selection, closing a project, and refreshing notes do not show another context's results.
- Empty notes, no matches, long threads, Tamil combining marks, and API failures produce readable states.
- No Scripture or note content changes as a side effect of displaying or opening a suggestion.
- Menu population targets under 100 ms for cached matching results; any slower reading uses a visible loading flow compatible with the host API. Measure this on the user's environment before claiming it is met.

## Remaining decisions

- Confirm whether structured QA notes should be restricted to a particular category/tag or whether any note containing the explicit fields qualifies.
- Confirm whether a later version should apply replacement text or only show the notes.
- Verify dynamic submenu support, framework target, SDK compatibility, installation path, and native note-opening support on the installed version.
