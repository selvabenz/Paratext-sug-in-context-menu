# Category-aware note handling

## Local inspection

Read the IRVTam project's current Notes_*.xml files and CommentTags.xml locally on 2026-10-31. Source notes were not changed or copied into this repository. This inventory describes stored comments, not a plugin-API determination of currently open threads; replies, deletion, and resolution must be reconciled through the host API at runtime.

Found 16,905 structured AI-QA comments with distinct thread IDs: 16,216 R2 and 689 P3. Each inspected QA comment has its own stored status `todo`; this does not replace runtime thread-status reconciliation. Other human comments and a separate five-comment AI Bridge file also exist and require their own parser/review handling.

The native project tags are **To Do, Clarity, Accuracy, Terms Check**. These differ from the semantic QA categories written in note headings. Keep native tags, QA pass, heading topic, canonical QA category, confidence, reviewer flag, content region, and proposed operation as separate fields.

## Observed canonical categories

Counts include all matching stored QA comments; they do not assert that every suggestion is current, unresolved, unique in effect, or applicable.

| Canonical category | Comments | Default route |
| --- | ---: | --- |
| Sandhi / word-joining | 3,593 | Exact text replacement when unambiguous. |
| Source comparison | 2,980 | Review meaning and source evidence; apply only an explicitly approved literal correction. |
| Punctuation | 1,700 | Exact text replacement when unambiguous. |
| Textual-basis review | 1,322 | Review the textual reading; do not infer a replacement. |
| Grammar | 1,118 | Exact phrase/sentence replacement when unambiguous. |
| Footnote / cross-reference | 848 | Target the identified note, reference, or call position through a structure-aware operation. |
| USFM marker | 843 | Preview and validate a specific marker/structure operation. |
| Name consistency | 782 | Apply a selected literal spelling locally; no implicit book-wide substitution. |
| Possible typo | 774 | Review uncertainty before approving a literal correction. |
| Possible missed source word | 705 | Review source evidence and specify exact insertion/replacement text. |
| Reviewer decision | 606 | Show options or an editable proposal; require the decision before applying. |
| Confirmed typo | 512 | Exact text replacement when unambiguous. |
| Theological consistency | 428 | Review the chosen term and local meaning; no automatic global substitution. |
| Plural agreement | 347 | Exact text replacement when unambiguous. |
| Case/object marker | 325 | Exact text replacement when unambiguous. |
| Addition not in source | 22 | Review source evidence and specify a precise deletion/replacement. |

These are defaults, not permission to write based on a category label alone. Every candidate must also pass content, target, ambiguity, and current-text checks. A punctuation or grammar note can concern an introduction or footnote rather than verse text.

## Operation selection

Resolve these dimensions in order:

1. **Live status and context:** current accessible project, current thread and comments, native tag, pass, category, and selected location. Exclude deleted/resolved notes according to the current filter. Do not trust XML status or a cached comment alone.
2. **Content region:** Scripture body, introduction, heading/chapter label, footnote, cross-reference, or structural range. Inspect Location, Original, Suggested, reason, and the actual USFM together. A verse reference alone does not locate an introduction or a footnote.
3. **Proposal type:** literal replacement, explicit alternatives, deletion, insertion, marker change, note movement, verse alignment, or review-only instruction.
4. **Review requirement:** honor an explicit reviewer-decision flag or uncertain content. High confidence and `Reviewer decision needed: No` do not override structural ambiguity or an instruction instead of replacement text. Opening the menu never automatically applies anything.
5. **Target validation:** obtain the appropriate lock, read fresh data, map the exact region, and check the original text. Proceed only when the intended edit is uniquely established.

### Clear text corrections

For a single literal correction in a verified region, show **Apply: [replacement]**. Clicking applies exactly that correction using the supported Scripture API. The word, phrase, sentence, or full-verse extent comes from the verified Original and anchor, not from its category.

### Alternatives and reviewer decisions

Show **Choose correction…** or **Review proposal…**. Once the reviewer chooses or enters the exact text and sees the intended target, **Apply chosen correction** commits it. Never insert the complete alternatives string into Scripture.

Observed suggestions include slash-separated alternatives, English parenthetical commentary, and reviewer instructions. Do not blindly split every slash or parenthesis: both can be legitimate Scripture punctuation. If a deterministic parser cannot separate approved replacement text from commentary, keep the note in review mode. Do not silently remove commentary heuristically.

### USFM and note structure

Use separate validated operations for marker changes, note edits/moves, verse-bridge changes, and verse-content exchanges. Preview the affected USFM and preserve unrelated content. A footnote edit must not accidentally replace the same words in the verse body; an introduction edit must not target chapter 1 verse 1 merely because the note is anchored there.

The notes include marker reclassification, moving a footnote call, and exchanging verse contents while relocating a cross-reference. These cannot be represented safely as a generic word replacement. Unsupported structural operations remain review-only with the limitation stated; they are not marked applied.

### Instructions, no-change notes, and legacy comments

Suggestions such as “no change proposed,” requests to consider a source reading, or prose describing a verse exchange are not literal replacement strings. Show **Review note** or a dedicated structured edit form. No-change notes have no Apply action.

Older human notes use inconsistent labels, including `sug`, `Sugg`, `Suggestion`, and `Changed to`, as well as free prose. AI Bridge notes use another XML schema and may contain discussion decisions. Do not mistake a historical “Changed to” report for an outstanding edit. Fall back to source-note review when an explicit current proposal cannot be established.

## Matching and overlapping notes

There are 1,485 QA comments containing an explicit fallback-anchor warning, 718 whose Location mentions an introduction, and 1,747 whose Original or Suggested contains a backslash. These are overlapping diagnostic counts, not validated operation counts. They demonstrate why matching only a verse number or using raw string substitution is insufficient.

There are 6,657 explicit reviewer-decision Yes flags and 10,248 No flags. Neither flag alone establishes that the Suggested value is executable replacement text.

R2 and P3 can both propose changes at the same passage. Do not prefer P3 automatically. Group candidates by actual target and compare them: equivalent proposals can share an action with provenance, while competing edits must expose a choice. After any edit, invalidate and revalidate overlapping suggestions; never batch them against stale text. Extending an edit across a book requires a separate explicit scope and preview.

## Verification before release

- Fixtures covering all 16 categories and both passes, using synthetic content rather than committed project notes.
- Cross-cutting tests for body/footnote/introduction regions, alternatives, no-change instructions, rich-text field boundaries, fallback anchors, stale text, repeated words, and conflicting proposals.
- Structural tests for marker preservation and separately supported intentional marker changes.
- Exact applied USFM diff, persistence after reopening, normal export output, and an authorized controlled Send/Receive check.
- UI checks for Apply, Choose correction, Review proposal, Review note, and Not applicable states, with no misleading success indication.
