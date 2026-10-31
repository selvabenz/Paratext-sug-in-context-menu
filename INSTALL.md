# Install the preview

Version: 0.2.0-preview.1. Windows, Paratext 9.5.110.1, .NET Framework 4.8. The compiled plugin references the installed 2.0.100.0 interface assemblies; those proprietary host assemblies are not redistributed.

This preview has automated and real-host validation for loading, exact body/footnote writes, ambiguity refusal, and revert in a disposable project. Export and Send/Receive acceptance remain pending. See VALIDATION.md for the exact boundary.

## Installation

1. Download the release ZIP and its `.sha256` sidecar from this repository's GitHub release.
2. Check the ZIP using `Get-FileHash -Algorithm SHA256 .\ParatextNoteSuggestions-0.2.0-preview.1.zip` against the sidecar. Extract the ZIP to a folder.
3. Close Paratext yourself after saving your work. The installer refuses to run while Paratext is open.
4. In the extracted folder, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`. Approve the Windows administrator prompt if requested. This is a process-local execution-policy choice, not a persistent policy change. The script checks the plugin hash and host version, elevates if needed, installs into `Paratext 9\Plugins\ParatextNoteSuggestions`, and runs Paratext's official plugin-cache updater.
5. Start Paratext and open a disposable test project. Check that **Note suggestions…** appears in the Scripture right-click menu and the project's Tools menu.

For a different installation directory, pass `-ParatextDir 'D:\Apps\Paratext 9'`.

The installer supports `-WhatIf`. It does not edit notes, Scripture, Send/Receive settings, or project permissions. It does not close or restart Paratext automatically.

## Using corrections

- Select a word/phrase or choose the current verse, then right-click → **Note suggestions…**.
- A live popup lists up to twelve matching proposals. **Apply:** entries perform an exact validated text correction. **Review:** entries open the category-aware review window.
- Use **Review all notes / choose correction…** to filter categories, inspect the original discussion, select a target region, and enter one literal correction. Check the review box and apply. Explicit deletion requires its separate checkbox.
- **Apply supported marker rename** accepts only an exact heading/chapter-label marker rename with unchanged label text. Complex structural instructions cannot be applied by this preview.
- Notes are not automatically resolved. If a note remains visible after application, another attempt will refuse when the old Original no longer matches.
- Corrections save through Paratext's Scripture APIs. Subsequent export and Send/Receive should use that saved project data; verify the end-to-end workflows in a disposable project first. The plugin never starts Send/Receive itself.

The installed API has fixed plugin menu registrations. Dynamic corrections appear in a second popup after selecting Note suggestions; they are not inserted directly into Paratext's existing native menu on each right-click.

## Using the review queue

Open the intended Scripture project, then right-click **Review note queue?** (also in Tools). It indexes all unresolved structured AI-QA proposals, one book at a time. The queue window is modeless: move or minimize it while checking references. No timer applies corrections or advances notes.

1. Inspect the **Correction and decision** and **Full note and discussion** tabs. Verify the displayed project, passage and content region. Edit the chosen literal correction if necessary.
2. Check **I reviewed this exact correction**, then **Apply and next**. Only a successfully verified USFM write records Applied and advances. Apply comments are optional.
3. To reject, enter a reason and click **Reject and next**. To record work performed manually in Paratext, or a no-change decision, enter a comment, check review, and select **Completed manually / no change**.
4. **Return to queued passage** navigates back after reference browsing. **Leave for later** retains a draft and moves on. **Pause** disables decisions; **Resume** continues. Closing saves the current draft. Reopen the same command/project to continue after restarting Paratext.
5. **Export decisions...** writes a CSV of decisions/comments. Progress is local to this Windows user at `%LOCALAPPDATA%\ParatextNoteSuggestions\Review\<project-ID hash>`. Back up this folder with Paratext closed if you need to transfer review progress. It does not travel with Send/Receive. Do not delete it to retry an uncertain edit.

An apply that reaches submission is marked **Pending apply** immediately before the Scripture write. A pre-write validation refusal can be revised or rejected normally. Inspect the saved passage and project history. If it was completed, record that manually with a comment; if you verified no edit occurred, use **Verified no edit; allow retry** and its confirmation. The queue never guesses or automatically repeats it. Source notes remain unresolved, and comments are saved to the local review log rather than the Paratext discussion.

Scope is unresolved `[AI-QA ...]` source comments, including R2/P3 and all 16 semantic categories. Legacy/human notes and native tag filtering are outside this queue. Multiple structured proposals in a thread are separate items; any conflicting correction still requires explicit review. Resolving a source note outside the queue removes it from the next scan. New notes are discovered when you reopen the queue.

## Recovery and removal

**Revert last plugin edit…** restores the previous chapter/book only when the entire saved scope still matches the plugin's expected post-edit USFM. It refuses to overwrite later changes. The one-entry-per-project recovery record exists only in memory until Paratext exits. History points are requested before and after writes; native Ctrl+Z support is not claimed.

To remove the plugin, close Paratext and run `powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1` as administrator. This removes only the named plugin file. Uninstalling does not revert project edits.

## Preview limits

- Complex verse exchanges, numbering/bridge changes, moving note calls, cross-chapter edits and replacements spanning multiple USFM text tokens remain manual review tasks.
- Latin-text replacements, obvious alternative separators, reviewer instructions, and multiline text replacements are refused by the text writer.
- The public note interface exposes no stable note ID or native note-tag list. This preview uses note/anchor fingerprints and semantic QA headings; it cannot filter native Clarity/Accuracy tags.
- Introduction edits require whole-book scope and permission because chapter zero is the API's whole-book/default scope. The exact matched introduction text token alone is changed. Confirm these tokens and lock semantics in the real host before using this route.
