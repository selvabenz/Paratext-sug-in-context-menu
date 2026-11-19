# Fast review (development build)

Fast review lets one reviewer go through the remaining AI-QA notes with one key press per note. It is a tab in the existing **Review note queue**. You decide every note. Nothing is applied on a timer, and every safety check of the normal queue still applies.

## Turning it on

1. Open **Review note queue…** and click **Fast review…** on its toolbar.
2. Tick **Use fast review**.
3. Choose what happens to notes whose Suggested text is not ready-to-paste Tamil (alternatives, English instructions):
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
| PgUp / PgDn | Previous / next note, without deciding |
| Home | Return the editor to the note's verse |
| Alt+1 to Alt+5 | Use that comment preset for this note; Alt+0 returns to automatic |
| F2 / Esc | Type your own comment / return to the keys |
| F11 | Undo the last decision (see below) |

While you type your own comment (F2), letters and caret keys go into the comment box; the F-keys still decide, so type the comment and then press F6, for example. A key pressed while the previous decision was still being written is ignored and noted in the Log tab: each note is decided only by a key pressed after its before/after was on screen.

Function keys are the default because they cannot type into the Scripture editor if the keyboard is unexpectedly there. Single letters (A, R, M, F, S, P, N, H, U, 0-5) can be switched on in the settings; use them only after a whole book shows no "LOST the keyboard" line in the Log tab. Holding a key down never decides a second note.

## Automatic comments

- Apply with the note's own wording: "Accepted as suggested."
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
- Structural notes, deletions and edits spanning formatting stay in the Review tab.
- Global hotkeys are not used. To come back after editing in Paratext, click the Fast review band.
