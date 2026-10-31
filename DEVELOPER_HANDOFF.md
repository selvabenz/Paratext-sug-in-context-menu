# Developer handoff

## Release and status

0.1.0-preview.1 is an initial working C# implementation for Windows Paratext 9.5.110.1. It compiles against installed `CorePluginInterfaces.dll` and `PluginInterfaces.dll`, both assembly version 2.0.100.0. Target .NET Framework 4.8, AnyCPU. This is a preview, not a claim of production readiness or completion of every structural edit category.

The active live IRVTam project was only read for note-format inventory. No live Scripture, note, project configuration, or installed plugin file was changed. Existing Paratext was left running. All write-path tests used synthetic API doubles. A separate desktop harness exercised the real WinForms review UI and saved a synthetic `.sfm` file in ignored build output.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

No NuGet restore or .NET SDK installation is needed. The build uses Windows' .NET Framework C# compiler, C# 5 syntax, the installed netstandard facade, and the user's local host interfaces. Warnings are errors. `-ParatextDir` overrides the host directory. `-SkipTests` builds only the plugin; package.ps1 deliberately does not skip validation.

`artifacts/bin` contains a DLL, its `.ptxplg` copy, tests, synthetic demos and private local interface copies for test loading. Do not distribute this directory. `package.ps1` whitelists the plugin, installer/remover, documentation and checksum manifest, then emits a ZIP and its SHA-256 sidecar. No host DLLs, project notes or synthetic test executable are in the release ZIP.

## Code map

| File | Responsibility |
| --- | --- |
| src/Domain.cs | QA field parser, 16 semantic categories, conservative routing, Unicode-aware preview and exact token-local matching. |
| src/NoteService.cs | Live note retrieval, anchor/discussion fingerprints, conflict grouping, permissions/locks, token region mapping, host writes, verification, history and checked session revert. |
| src/Plugin.cs | Public standalone plugin, fixed context/Tools commands and dynamic secondary popup. |
| src/ReviewForm.cs | Category filter, source discussion, editable literal correction, explicit deletion, review acknowledgement and supported marker action. |
| tests/Tests.cs | Standalone test runner, interface doubles via RealProxy, desktop demos, optional read-only local route audit. |
| scripts/build.ps1 | Compile and test against the local 2.0.100.0 interfaces. |
| packaging/*.ps1 | Narrow installation/removal; refuses while Paratext is running. |

## Write invariant

Capture project/passage/selection identity → check permission → obtain scope write lock → re-read the unresolved source note and current tokens → locate exactly one Original within the explicit region → change only that text token → request history point → submit `PutUSFMTokens` → send notifications → re-read and compare the serialized scope to the expected USFM → request post-edit history point.

No raw project filesystem writes are used. No Send/Receive or export is invoked. QA note status is untouched. Stale source, conflicting proposals, unavailable/revoked locks, ambiguous repeats, unsupported region boundaries or changed active context block automatic actions. Failure messages distinguish an unverified submitted edit from a confirmed success; they do not promise transactional rollback.

For text edits, chapter scope is used except introductions (whole book, chapter argument 0). Intro, body, heading, footnote and cross-reference content are separated using the public token flags and surrounding markers. Unknown/special/metadata/figure content is excluded. Spans crossing token boundaries are refused; do not flatten markup to make a match.

The only supported structural writer is a deliberately small marker-rename operation: one of `ms`, `ms1`–`ms3`, `s`, `s1`–`s4`, `cl`, `cd` may become another of those markers if its whole label is identical and uniquely anchored. Generated USFM is reparsed and compared before saving. Verse/chapter numbers, attributes, note boundaries and verse exchanges are excluded.

## API findings and tradeoffs

- `PluginMenuEntry` has fixed location/callback/image data and a localization event, with no supported menu-opening population callback, writable label, submenu collection, or refresh method. Do not use internal host reflection or patch the UI to emulate one. A fixed command opens a real WinForms popup containing live correction labels. This extra click is a known UX difference from the original mockup.
- The host callback supplies `IParatextChildState.Selections`. Current selection and verse are used; the API's mouse-under-pointer passage is not assumed.
- `IProjectNote` exposes anchor, comments and resolved state, but not a stable ID or native tags. Fingerprint all comment text, creation/author metadata and anchor context. Uncertain duplicate identity refuses editing. Native tag filtering is not implemented.
- Parsing rich-text paragraphs concatenates each paragraph's `FormattedString.Text` spans before extracting fields. Do not parse bold styling or screenshot OCR.
- `Reviewer decision needed: No` alone never permits structural/instructional content to become replacement text. Uncertain categories, later replies, conflicting corrections, Latin commentary and alternatives go through review. Text writes continue to reject English instructions and obvious alternatives even after checking the review box.
- Revert stores one pre/post serialized scope per project in memory. It is guarded against later edits, but does not survive host exit. Native Ctrl+Z and history restore are not yet verified in the real host.

## Validation and remaining work

See VALIDATION.md for evidence and explicit pending real-host checks. Before stable release:

1. Close the user's session normally, install on 9.5.110.1 and verify plugin discovery/loading and context-popup lifecycle in the actual host.
2. In a disposable project, check real token offsets/flags and region inference, particularly paragraph endings, note-end markers, bridged verses and introductions. Confirm chapter-zero scope and the exact persistence/serialization behavior.
3. Apply and reopen body/footnote/intro corrections, export USFM, compare the exact diff, then perform an authorized test Send/Receive between test copies. Investigate any normalization differences rather than silently accepting a mismatched post-write serialization.
4. Confirm history-point behavior and safe recovery; do not advertise native undo before testing it.
5. Extend structural operations only as dedicated validated transforms. Complex note moves and verse exchanges remain review-only. The supplied category name is not enough to construct them safely.
6. Consider public API evolution or a host-supported annotation menu if direct integration improves. Do not misuse the localization event as a live Scripture menu hook.

The older PLAN.md and CATEGORY_HANDLING.md retain the user's full target requirements. This release's implementation status and limits are defined here and in INSTALL.md/RELEASE_NOTES.md. The ignored earlier HTML mockup is historical; it does not represent the compiled plugin.
