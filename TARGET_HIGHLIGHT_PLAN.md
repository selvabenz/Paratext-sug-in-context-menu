# Scripture target highlighting

## Plan

1. Use Paratext 9.5's public Scripture annotation API to mark the current queue proposal's `Original` text in the pinned project's editor. The public API cannot move the editing caret or programmatically enable an annotation layer.
2. Provide **Highlight queued note target** in Paratext's annotation menu. Once enabled for the project, update it automatically when Apply + next / Reject + next load the next item and when Return to passage is clicked. Preserve existing verse navigation.
3. Match literal text using the host's Scripture-selection API against the displayed USFM. Require a unique match, using the source anchor's context when Original equals its anchor. Refuse missing, ambiguous and cross-verse targets. Never guess a replacement after applying an edit.
4. Clear the old highlight on scope changes, end of queue and disposal. Keep annotations transient; no Scripture or note writes.
5. Cover matching, ambiguity, project isolation, advancement, Return and cleanup in synthetic checks. Build with warnings as errors, install with Paratext closed, verify the installed hash, then commit and push descriptive changes. Record live-host limits honestly.

Reference: [official quotation annotation example](https://github.com/ubsicap/paratext_demo_plugins/tree/master/QuoteAnnotationPlugin), checked against installed interfaces 2.0.100.0.

## Build and installation

10 November 2026: all **189 synthetic checks passed**, zero failed, with warnings treated as errors against the installed Paratext interfaces. Build log: ignored `artifacts/target-highlight-build.log`. The initial sandboxed baseline encountered the repository's documented atomic-file-replacement restriction and was stopped; validation passed with the required filesystem access. Automatic item loads now use the same adjacent-verse/target-verse reveal as Return, including repeated loads in the same verse.

Installed using the checksum-verified installer with Paratext closed and the official cache refresh. The final installed plugin matches the build that passed 189 checks, SHA-256 `EC0868296AE3FA2AB30D43C7BC8CC4BD60CCFF9612F7F034A9DDF281F9AF3DD6`. The previous binary is retained under ignored `artifacts/install-target-highlight-9396ce333fda45eab224ae41ce499a86/ParatextNoteSuggestions.previous.ptxplg`.

Paratext 9.5 loaded the combined menu/annotation plugin and displayed **View > Highlight queued note target** in the IRVTam project menu. The layer was enabled there. Synthetic tests establish advancement after Apply/Reject; no production decisions are performed for installation validation. The annotation is a visible highlight, not an editing-caret selection. Cross-verse targets and exact-text matches that remain ambiguous are intentionally unmarked.

The initial 188-check build was visually checked in the live host: Return revealed EXO 14:4 with the exact Original phrase highlighted yellow across its wrapped lines. Next reached EXO 15:20, where the note's Original no longer matched exactly; no approximate highlight was drawn. Previous restored the original queue position, then the queue and Paratext were closed normally. No Apply, Reject, manual decision or resolution was invoked. This prompted the final automatic-load reveal correction; that correction passed its additional regression check before final installation. Live Apply/Reject advancement remains synthetic-only acceptance. The final binary's changed navigation path has not received a second host visual check.
