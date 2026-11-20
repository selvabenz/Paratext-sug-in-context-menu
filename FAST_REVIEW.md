# Fast review (development build)

Fast review lets one reviewer go through the remaining AI-QA notes with one key press per note. It is a tab in the existing **Review note queue**. You decide every note. Nothing is applied on a timer, and every safety check of the normal queue still applies.

## Turning it on

1. Open **Review note queue…** and click **Fast review…** on its toolbar.
2. Tick **Use fast review**.
3. Choose what happens to notes whose Suggested text gives no exact Tamil to paste (English instructions, questions, partial text; see "Which notes arm the apply key"):
   - **Stop for me** (default): the note waits. Edit the verse in Paratext, then press **F7**.
   - **Later round**: these notes are left open and untouched in this run. Interrupted work in Recovery is never skipped.
   - **Prepared wording**: an imported wording sheet supplies exact Tamil. It is shown labelled **PREPARED WORDING - not from the note** and is applied only when you press **F5**. Notes without a valid sheet entry behave as **Stop for me**.
4. Put the IRVTam editor in the queue's scroll group (Tools > Scroll group). The queue then moves the editor without taking the keyboard away.

The settings are kept for this Windows account and project.

## Keys

Keys act only while the **Fast review** tab is open and the queue has the keyboard. The band at the top says **KEYS ACTIVE** in green; if it does not, click the band.

| Key | Action |
| --- | --- |
| F5 | Apply the change shown in "After the apply key", post the reply, resolve the note, go to the next note |
| F6 | Reject, with the automatic comment |
| F7 | Fixed by hand in Paratext. Comment 2 if the saved verse changed. If nothing changed, the first press warns; a second press records "no change needed" |
| F8 | Add a footnote (see below) |
| F9 | Skip: leave the note open for now |
| F4 | Show the note's other wording, when the note offers alternatives |
| PgUp / PgDn | Previous / next note, without deciding |
| Home | Return the editor to the note's verse |
| Alt+1 to Alt+5 | Use that comment preset for this note; Alt+0 returns to automatic |
| F2 / Esc | Type your own comment / return to the keys |
| F11 | Undo the last decision (see below) |

While you type your own comment (F2), letters and caret keys go into the comment box; the F-keys still decide, so type the comment and then press F6, for example. A key pressed while the previous decision was still being written is ignored and noted in the Log tab: each note is decided only by a key pressed after its before/after was on screen.

Function keys are the default because they cannot type into the Scripture editor if the keyboard is unexpectedly there. Single letters (A, R, M, F, S, W, P, N, H, U, 0-5) can be switched on in the settings; use them only after a whole book shows no "LOST the keyboard" line in the Log tab. Holding a key down never decides a second note.

## Which notes arm the apply key

The apply key is armed only with Tamil that the note itself gives. The plugin never composes wording. The line under the before/after says where the wording came from; anything other than the note's Suggested line as written is shown in the warning colour.

| The note's Suggested line | What is offered |
| --- | --- |
| Exact Tamil | That text |
| Tamil followed by an English remark in brackets, e.g. `… (for reviewer)`, `… (reviewer to decide)` | The Tamil without the remark |
| Alternatives: `A / B`, `A (or B)` | Each one separately; F4 shows the next. Never both together |
| An instruction that names the replacement: `follow the ESV: …`, `follow the reference text (…)`, `or read …`, `(reviewer: '…')` | The one Tamil phrase it names |
| Brackets in the Tamil, e.g. `(சேலா)` | Accepted only when the Original has the same brackets |

Not armed, because the Tamil is not a complete replacement for the Original: `e.g. …` examples, footnote wording (`add a footnote such as …`, `some copies read …`), headings to add, remarks about moving text or verse alignment, text with `…`, and English-only instructions. For these use F7 after editing in Paratext, F8 for a footnote, F6 or F9.

A note that said its Original "was not found" when it was written is armed only if the live verse now has that Original exactly once. When another open note suggests different wording for the same words, both stay available and the other suggestion is shown.

### Words in a heading, footnote or cross-reference

Many notes quote a heading or a footnote without saying so. When the Original is not in the verse text but is found exactly once in one heading, footnote or cross-reference of that verse, the change is offered there, and the line under the before/after says so ("Found in the heading, not the verse text the note names"). The reverse never happens: a note about a heading or footnote is never applied to verse text.

### Marker changes

A note whose Original or Suggested contains USFM markers (`\f … \f*`, `\x … \x*`, `\s`, `\q`, `\d` …) is applied as one exact replacement in the chapter's USFM. Examples: correcting the text or spacing of a footnote or cross-reference, a verse word next to a footnote call, a heading's text or level, a poetry line's indent, removing a whole footnote, adding the footnote the note spells out.

It is refused, with nothing written, unless all of these hold:

- the Original, with its markers and spacing, occurs exactly once at the note's verse (or in the heading directly above it; never in the previous verse's own text or notes);
- the match starts and ends on whole words and whole markers, so no word is split;
- a footnote or cross-reference origin the note introduces (`\fr`, `\xo`) names the note's own verse, and character styles stay opened and closed as before;
- only note, heading, poetry, paragraph and character markers are involved; chapter, verse and book markers are never changed;
- the text has no English, alternatives or `…`;
- Paratext reparses the prepared chapter to exactly the previewed text;
- chapter and verse numbering is identical, every footnote and cross-reference still opens and closes properly, and nothing outside the note's verses changed;
- when the markers themselves change, nothing of the Original is left out and the verse wording is unchanged. A Suggested line that gives only the changed part (for example just the new reference list) is refused, because writing it as it stands would delete the rest.

A marker change is written only as the note gives it. If you need different wording, edit in Paratext and press F7.

Not handled: an Original that runs across a footnote call or a line break, and changes to chapter or verse numbers. These stay manual.

## Automatic comments

- Apply with the note's own wording, including a marker change and wording read from the note as described above: "Accepted as suggested."
- Apply with prepared or edited wording: preset 2.
- Reject: preset 3 ("the existing wording is correct") for wording categories (grammar, punctuation, plural, case marker, names, footnotes, markers); preset 4 ("would change the intended meaning") for meaning categories. Both are configurable.
- Fixed by hand: preset 2 when the verse changed, preset 5 when nothing changed.

The comments that each key will post are shown on the tab before you press.

## What one key press guarantees

- The apply key writes exactly the before/after that is on screen. If the passage changed after it was shown, nothing is written, the reason is shown, and the note stays.
- Everything the normal Apply checks still holds: edit permission, history point before the lock, write lock, fresh note, one exact match in the right region, no USFM markers or line breaks, no English, alternatives or ellipses, saved USFM verified.
- A refusal before anything is written never pauses the queue. A failure after the write was submitted stops as before and goes to Recovery.

## Undo

By default the reply and note resolution for a fast decision are posted at your next action (next decision, skip, browse, pause, scope change or closing). Until then **F11** undoes the decision completely:

- An applied edit or footnote is reverted only if the plugin's last edit is this decision's own edit and the saved passage is still exactly what it wrote. An edit made since, by you or by the plugin's other commands, is never reverted in its place. The restored passage is verified.
- The note reopens, showing the note's own wording again. The session report shows the decision as Undone.
- If an undo is interrupted after the revert was submitted, the decision moves to Recovery as an interrupted write and its reply is never posted.
- If an applied edit is reverted some other way (for example with Revert last plugin edit) before its reply is posted, the reply is not posted and the note reopens.
- Undo waits while the queue is paused.

Once the reply is posted and the note resolved, plugins cannot reopen it or delete the reply. Restore text through Paratext's project history and reopen the note there. If the queue closes unexpectedly while a reply is held, the decision appears in **Recovery**; **Retry note resolution** posts it without repeating the edit.

## Footnotes (F8)

For Source comparison and Textual-basis notes. The composer:

- offers templates, chosen with Alt+1-9, seeded with:
  - சில பிரதிகளில் {text} இவ்வாறு உள்ளது.
  - சில பிரதிகளில் {text} காணப்படவில்லை.
  - சில பிரதிகளில் {text} என்று உள்ளது.
  - சில பிரதிகளில் {text} என்பது காணப்படவில்லை.
  - சில பிரதிகளில் இந்த வசனம் காணப்படவில்லை.
- fills {text} without typing: Alt+O note Original (the default), Alt+S note Suggested if it is plain Tamil, Alt+P selection in the Paratext editor, Alt+B the reference text box (selection or Tamil text), Alt+C clipboard. The text stays editable.
- places the footnote right after the note's Original, or at the end of the verse.
- shows the exact chapter USFM before and after. Enter inserts; Esc cancels.

**Edit templates…** changes the list. Templates may contain {text} once and no backslash, line break, brace, bar or Latin letters.

The footnote is written as `\f + \fr C:V \ft text\f*`, the form used by the project's existing 1,440 footnotes. Before writing, Paratext must reparse the prepared chapter to exactly the expected text, and the only difference from the current chapter must be that one footnote. An identical footnote already in the verse, a word split, a bridged verse or any other difference is refused with nothing written. The decision is recorded as Applied with the comment "Footnote added; verse wording unchanged: …".

## Reference texts

Chosen in the settings (ESVUS11 and TAOVBSI by default). Paratext normally refuses plugins access to resource text unless the plugin has a resource access token from the Paratext team. When it refuses, the box says so once per session; use **Tools > Open reference windows**, which opens them as Paratext windows. Put those windows in the queue's scroll group so they follow each note. Reference text is shown only; it is never saved, exported or put in reports.

## Wording sheets (Prepared wording)

1. **Tools > Export for wording…** writes the open, undecided notes of the current scope. For each note the file holds its id, all note fields, the current passage USFM and its hash, and the text pieces the wording can match. Nothing in the project changes.
2. The wording is prepared outside the plugin as a sheet:

   ```xml
   <wordingSheet version="1" projectId="(the export's projectId)">
     <wording id="(proposal id from the export)" verseHash="(verseHash from the export)">
       <original>exact Tamil inside one text piece</original>
       <replacement>exact Tamil replacement</replacement>
       <rationale>short reason</rationale>
     </wording>
   </wordingSheet>
   ```

3. **Tools > Import wording sheet…** checks every entry against the live project. An entry is ignored, with its reason in the Log tab, if:
   - its id is unknown or repeated;
   - the note is no longer open, has changed, or is already decided;
   - the passage changed since the export;
   - the Original is not found exactly once;
   - the replacement is empty, not plain Tamil, contains markup, or is not in the project's normalization.

   A sheet for another project is refused whole.

Prepared wording is a proposal for you to check. It is never applied without your key press.

## Host probe

**Tools > Host probe** records in the Log tab, without writing anything:

- which projects and resources the plugin can read at the current verse (sizes only, never text);
- the open windows, their scroll groups and selection lengths;
- a footnote dry run for the current note.

While the probe runs, every key that reaches the queue and every navigation's keyboard result is logged. Run it once in the disposable NSTest project with the queue docked and once floating; its results decide whether the key defaults need changing.

## Limits

- Not yet tested inside Paratext. Key handling in the docked window, the timing guard for keys pressed during a decision, focus after navigation, resource access, the footnote round trip, and undo of a footnote are proven only with synthetic doubles. If Paratext numbers the reparsed and the saved tokens differently, undo of a footnote will be refused; that is safe, and Paratext history still restores it. Use the host probe and the NSTest checklist in [VALIDATION.md](VALIDATION.md) before any real book.
- Marker changes, wording read out of a longer Suggested line, and text found in another place than the note names are also proven only with synthetic doubles. In particular, Paratext's own USFM text (line breaks, spacing after markers) may differ from what a note quotes; such a note is refused, not guessed.
- Deletions, structural instructions without exact USFM, and edits spanning a footnote call or line break are not applied by a key.
- Global hotkeys are not used. To come back after editing in Paratext, click the Fast review band.
