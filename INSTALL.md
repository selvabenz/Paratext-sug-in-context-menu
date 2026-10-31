# Install the preview

Version: 0.1.0-preview.1. Windows, Paratext 9.5.110.1, .NET Framework 4.8. The compiled plugin references the installed 2.0.100.0 interface assemblies; those proprietary host assemblies are not redistributed.

This is a preview with automated and synthetic desktop validation. It has not yet completed real-host loading, disposable-project export, or Send/Receive validation. Do the acceptance checks in VALIDATION.md before editing a working translation.

## Installation

1. Download the release ZIP and its `.sha256` sidecar from this repository's GitHub release.
2. Check the ZIP using `Get-FileHash -Algorithm SHA256 .\ParatextNoteSuggestions-0.1.0-preview.1.zip` against the sidecar. Extract the ZIP to a folder.
3. Close Paratext yourself after saving your work. The installer refuses to run while Paratext is open.
4. Open PowerShell as administrator in the extracted folder and run `powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`. This is a process-local execution-policy choice, not a persistent policy change. The script checks the plugin hash and host version before copying the plugin into `Paratext 9\Plugins\ParatextNoteSuggestions`.
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

## Recovery and removal

**Revert last plugin edit…** restores the previous chapter/book only when the entire saved scope still matches the plugin's expected post-edit USFM. It refuses to overwrite later changes. The one-entry-per-project recovery record exists only in memory until Paratext exits. History points are requested before and after writes; native Ctrl+Z support is not claimed.

To remove the plugin, close Paratext and run `powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1` as administrator. This removes only the named plugin file. Uninstalling does not revert project edits.

## Preview limits

- Complex verse exchanges, numbering/bridge changes, moving note calls, cross-chapter edits and replacements spanning multiple USFM text tokens remain manual review tasks.
- Latin-text replacements, obvious alternative separators, reviewer instructions, and multiline text replacements are refused by the text writer.
- The public note interface exposes no stable note ID or native note-tag list. This preview uses note/anchor fingerprints and semantic QA headings; it cannot filter native Clarity/Accuracy tags.
- Introduction edits require whole-book scope and permission because chapter zero is the API's whole-book/default scope. The exact matched introduction text token alone is changed. Confirm these tokens and lock semantics in the real host before using this route.
