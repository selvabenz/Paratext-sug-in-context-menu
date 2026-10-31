using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Paratext.PluginInterfaces;

namespace ParatextNoteSuggestions
{
    public sealed class NoteCandidate
    {
        public Proposal Proposal;
        public string Key, StableId, DiscussionKey, NoteText, AnchorText, Before, After, DisplayReference, SourceCreated, SourceAuthor;
        public IVerseRef TargetReference;
        public int Book, Chapter, FirstVerse, LastVerse, EndBook, EndChapter;
        public bool CrossChapter, IsResolved, Unreadable;
    }
    public sealed class PassageEvidence
    {
        public string Before, After, Scope;
    }
    sealed class RecoveryRecord
    {
        public string ProjectId, Before, After;
        public int Book, Chapter;
    }
    public sealed class NoteService
    {
        readonly IPluginObject plugin;
        readonly IPluginHost host;
        readonly IProject project;
        readonly string projectId;
        readonly int book, chapter, firstVerse, lastVerse;
        readonly string selected;
        readonly string capturedContext;
        readonly bool queueMode;
        static readonly Dictionary<string, RecoveryRecord> Recovery = new Dictionary<string, RecoveryRecord>();
        public string ProjectName { get { return project.ShortName; } }
        public string ProjectId { get { return projectId; } }
        public string Reviewer { get { return host.UserInfo == null ? "Unknown Paratext user" : host.UserInfo.Name; } }
        public string Reference { get; private set; }
        public NoteService(IPluginObject pluginObject, IPluginHost pluginHost, IParatextChildState state)
        {
            plugin = pluginObject; host = pluginHost; project = state.Project; projectId = project.ID;
            capturedContext = ContextKey(state);
            book = state.VerseRef.BookNum; chapter = state.VerseRef.ChapterNum;
            firstVerse = state.VerseRef.VerseNum; lastVerse = firstVerse;
            var selection = state.Selections == null ? null : state.Selections.OfType<IScriptureTextSelection>().FirstOrDefault();
            if (selection != null && selection.VerseRefStart.BookNum == book && selection.VerseRefStart.ChapterNum == chapter &&
                selection.VerseRefEnd.BookNum == book && selection.VerseRefEnd.ChapterNum == chapter)
            { firstVerse = selection.VerseRefStart.VerseNum; lastVerse = selection.VerseRefEnd.VerseNum; selected = selection.SelectedText; }
            Reference = state.VerseRef.ToString();
        }
        // A queue pins the project and passage instead of following the active reference window.
        NoteService(NoteService owner, int targetBook, int targetChapter, int start, int end)
        {
            plugin = owner.plugin; host = owner.host; project = owner.project; projectId = owner.projectId;
            queueMode = true; book = targetBook; chapter = targetChapter; firstVerse = start; lastVerse = end;
        }
        public IEnumerable<int> QueueBooks() { return project.AvailableBooks.Select(b => b.Number).OrderBy(b => b); }
        public List<NoteCandidate> ReadQueueBook(int number, bool includeResolved = false)
        { return new NoteService(this, number, 0, 0, Int32.MaxValue).ReadCandidates(includeResolved).Where(c => c.Unreadable || c.Proposal.FullNote.TrimStart().StartsWith("[AI-QA ", StringComparison.Ordinal)).ToList(); }
        public NoteService ForQueueItem(NoteCandidate item)
        { return new NoteService(this, item.Book, item.Chapter, item.FirstVerse, item.LastVerse); }
        public NoteCandidate ReloadQueueItem(NoteCandidate item, bool includeResolved = false)
        {
            var matches = ForQueueItem(item).ReadCandidates(includeResolved).Where(c => c.StableId == item.StableId).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("The queued source changed, was resolved, or has duplicate identities. Review this item in Paratext before continuing.");
            return matches[0];
        }
        public void Navigate(NoteCandidate item)
        { host.OpenTextWindowFor(project, OpenWindowBehavior.UseExisting, item.TargetReference); }
        public string CapturePassage(NoteCandidate item, ContentRegion region)
        {
            var numbers = item.EndBook > item.Book ? project.AvailableBooks.Select(b => b.Number).Where(b => b >= item.Book && b <= item.EndBook) : new[] { item.Book };
            return Passage(numbers.SelectMany(b => project.GetUSFMTokens(b, region == ContentRegion.Introduction || item.CrossChapter ? 0 : item.Chapter)).ToList(), item, region);
        }
        string Passage(List<IUSFMToken> tokens, NoteCandidate item, ContentRegion region)
        {
            var context = region == ContentRegion.Introduction
                ? tokens.TakeWhile(t => !(t is IUSFMMarkerToken) || ((IUSFMMarkerToken)t).Type != MarkerType.Chapter).ToList()
                : tokens.Where(t => t.VerseRef != null &&
                    (t.VerseRef.BookNum > item.Book || (t.VerseRef.BookNum == item.Book && (t.VerseRef.ChapterNum > item.Chapter || (t.VerseRef.ChapterNum == item.Chapter && t.VerseRef.VerseNum >= item.FirstVerse)))) &&
                    (t.VerseRef.BookNum < item.EndBook || (t.VerseRef.BookNum == item.EndBook && (t.VerseRef.ChapterNum < item.EndChapter || (t.VerseRef.ChapterNum == item.EndChapter && t.VerseRef.VerseNum <= item.LastVerse))))).ToList();
            if (context.Count == 0) throw new InvalidOperationException("Full passage context could not be captured. No correction was submitted.");
            return project.ConvertToUSFMString(context);
        }
        void CheckContext()
        {
            if (queueMode) return; // All writes still re-read the source under the target project write lock.
            var active = host.ActiveWindowState;
            if (active == null || active.Project == null || active.Project.ID != projectId || active.VerseRef == null || ContextKey(active) != capturedContext)
                throw new InvalidOperationException("The active project, passage, or selection changed. Close this window and open suggestions again.");
        }
        static string ContextKey(IParatextChildState state)
        {
            return state.Project.ID + "|" + state.VerseRef + "|" + (state.Selections == null ? "" : String.Join("|", state.Selections.OfType<IScriptureTextSelection>()
                .Select(s => s.VerseRefStart + ":" + s.VerseRefEnd + ":" + s.Offset + ":" + s.SelectedText + ":" + s.BeforeContext + ":" + s.AfterContext)));
        }
        public List<NoteCandidate> ReadCandidates(bool includeResolved = false)
        {
            CheckContext();
            var result = new List<NoteCandidate>();
            foreach (var note in project.GetNotes(book, chapter, !includeResolved))
            {
                if ((!includeResolved && note.IsResolved) || note.Anchor == null || note.Anchor.VerseRefStart == null || note.Anchor.VerseRefEnd == null) continue;
                var a = note.Anchor;
                if (a.VerseRefStart.BookNum != book || (chapter != 0 && (a.VerseRefStart.ChapterNum > chapter || a.VerseRefEnd.ChapterNum < chapter))) continue;
                int start = a.VerseRefStart.ChapterNum == chapter ? a.VerseRefStart.VerseNum : 0;
                int end = a.VerseRefEnd.ChapterNum == chapter ? a.VerseRefEnd.VerseNum : Int32.MaxValue;
                if (chapter != 0 && (start > lastVerse || end < firstVerse)) continue;
                try
                {
                var comments = note.Comments.ToList();
                if (comments.Count == 0) continue;
                string threadKey = ThreadKey(note);
                var structured = comments.Select((c, i) => new { Text = Plain(c), Index = i }).Where(c => c.Text.TrimStart().StartsWith("[AI-QA ", StringComparison.Ordinal)).ToList();
                if (structured.Count == 0)
                {
                    string text = Plain(comments[comments.Count - 1]);
                    result.Add(Candidate(note, NoteParser.Parse(text), threadKey, text));
                }
                else foreach (var source in structured)
                {
                    var proposal = NoteParser.Parse(source.Text);
                    if (proposal.Route == ProposalRoute.Apply && source.Index != comments.Count - 1) proposal.Route = ProposalRoute.Review;
                    var candidate = Candidate(note, proposal, threadKey + ":" + source.Index, String.Join("\r\n\r\n--- Comment ---\r\n\r\n", comments.Select(Plain)));
                    var comment = comments[source.Index];
                    candidate.StableId = SourceId(note, comment);
                    candidate.SourceCreated = comment.Created.ToString("O"); candidate.SourceAuthor = comment.Author == null ? "" : comment.Author.Name;
                    result.Add(candidate);
                }
                }
                catch (ArgumentException ex)
                {
                    // Legacy rich-text language tags can fail in the host's lazy Contents getter.
                    // Keep an explicit blocked item/warning; do not lose the rest of the book.
                    string warning = "Paratext could not read this note discussion. Inspect its original note and language formatting in Paratext; leave it for later to continue. " + ex.Message;
                    var item = Candidate(note, NoteParser.Parse(warning), "", warning, false);
                    item.Unreadable = true;
                    item.Key = item.StableId = NoteParser.Hash("UNREADABLE|" + projectId + "|" + a.VerseRefStart + "|" + a.VerseRefEnd + "|" +
                        String.Join("|", note.Comments.Select(c => c.Created.ToString("O") + ":" + (c.Author == null ? "" : c.Author.Name))));
                    result.Add(item);
                }
            }
            foreach (var group in result.Where(c => c.Proposal.Original.Length > 0).GroupBy(c => c.Book + ":" + c.Chapter + ":" + c.FirstVerse + ":" + c.LastVerse + ":" + c.Proposal.Region + ":" + c.Proposal.Original))
                if (group.Select(c => c.Proposal.Suggested).Distinct(StringComparer.Ordinal).Count() > 1)
                    foreach (var c in group) if (c.Proposal.Route == ProposalRoute.Apply) c.Proposal.Route = ProposalRoute.Review;
            return result.OrderByDescending(c => !String.IsNullOrEmpty(selected) && (c.AnchorText == selected || c.Proposal.Original == selected))
                .ThenBy(c => c.Proposal.Category).ToList();
        }
        static string Plain(IComment comment)
        {
            if (comment.Contents == null) return "";
            return String.Join("\r\n", comment.Contents.Select(p => String.Concat(p.Spans.Select(s => s.Text))));
        }
        static string ThreadKey(IProjectNote note)
        {
            var a = note.Anchor;
            return NoteParser.Hash(a.VerseRefStart + "|" + a.VerseRefEnd + "|" + a.Offset + "|" + a.SelectedText + "|" + a.BeforeContext + "|" + a.AfterContext + "|" +
                String.Join("|", note.Comments.Select(c => c.Created.ToString("O") + ":" + (c.Author == null ? "" : c.Author.Name) + ":" + Plain(c))));
        }
        static string DiscussionKey(IProjectNote note)
        { return NoteParser.Hash(String.Join("|", note.Comments.Select(c => c.Created.ToString("O") + ":" + (c.Author == null ? "" : c.Author.Name) + ":" + Plain(c)))); }
        static string SourceId(IProjectNote note, IComment comment)
        { return NoteParser.Hash(note.Anchor.VerseRefStart + "|" + note.Anchor.VerseRefEnd + "|" + comment.Created.ToString("O") + "|" + (comment.Author == null ? "" : comment.Author.Name) + "|" + Plain(comment)); }
        List<IProjectNote> FindSourceNotes(NoteCandidate candidate)
        {
            return project.GetNotes(candidate.Book, candidate.Chapter, false).Where(n => n.Anchor != null && n.Anchor.VerseRefStart != null && n.Anchor.VerseRefEnd != null &&
                n.Comments.Where(c => c.Created.ToString("O") == candidate.SourceCreated && (c.Author == null ? "" : c.Author.Name) == candidate.SourceAuthor).Any(c => SourceId(n, c) == candidate.StableId)).ToList();
        }
        // Separate from the Scripture transaction: the durable decision must already exist.
        public bool ResolveReviewedThread(NoteCandidate candidate, string reviewedDiscussion, Func<string, bool> completed, out List<string> sourceIds)
        {
            sourceIds = new List<string>();
            bool revoked = false;
            using (var writeLock = project.RequestWriteLock(plugin, delegate { revoked = true; }, WriteLockScope.ProjectNotes))
            {
                if (writeLock == null) throw new InvalidOperationException("Decision saved locally, but a project-notes write lock is unavailable. Retry note resolution; do not apply the correction again.");
                var matches = FindSourceNotes(candidate);
                if (matches.Count != 1) throw new InvalidOperationException("Decision saved, but the source note is missing or ambiguous. Resolve it manually in Paratext after checking the source.");
                var note = matches[0];
                sourceIds = note.Comments.Where(c => Plain(c).TrimStart().StartsWith("[AI-QA ", StringComparison.Ordinal)).Select(c => SourceId(note, c)).ToList();
                if (sourceIds.Distinct().Count() != sourceIds.Count) throw new InvalidOperationException("Duplicate proposals prevent automatic note resolution.");
                if (note.IsResolved) return true; // Idempotent recovery after a successful resolve but failed local acknowledgement.
                if (DiscussionKey(note) != reviewedDiscussion) throw new InvalidOperationException("The source discussion changed after the decision. Inspect and resolve the note manually in Paratext, then retry note resolution.");
                if (!sourceIds.All(completed)) return false; // Do not hide other unreviewed proposals in the same note.
                if (revoked) throw new InvalidOperationException("The project-notes write lock was withdrawn. Retry note resolution.");
                note.Resolve(writeLock); writeLock.SendNotifications();
                var saved = FindSourceNotes(candidate);
                if (saved.Count != 1 || !saved[0].IsResolved) throw new InvalidOperationException("Note resolution was submitted but not verified. Retry note resolution; the Scripture correction will not be repeated.");
            }
            return true;
        }
        static NoteCandidate Candidate(IProjectNote note, Proposal proposal, string key, string text, bool readDiscussion = true)
        {
            var a = note.Anchor;
            return new NoteCandidate { Proposal = proposal, Key = key, DiscussionKey = readDiscussion ? DiscussionKey(note) : "", IsResolved = note.IsResolved, TargetReference = a.VerseRefStart, NoteText = text, AnchorText = a.SelectedText, Before = a.BeforeContext, After = a.AfterContext,
                Book = a.VerseRefStart.BookNum, Chapter = a.VerseRefStart.ChapterNum, FirstVerse = a.VerseRefStart.VerseNum, LastVerse = a.VerseRefEnd.VerseNum,
                EndBook = a.VerseRefEnd.BookNum, EndChapter = a.VerseRefEnd.ChapterNum,
                CrossChapter = a.VerseRefStart.BookNum != a.VerseRefEnd.BookNum || a.VerseRefStart.ChapterNum != a.VerseRefEnd.ChapterNum,
                DisplayReference = a.VerseRefStart + (a.VerseRefStart.ToString() == a.VerseRefEnd.ToString() ? "" : " – " + a.VerseRefEnd) };
        }
        NoteCandidate Fresh(NoteCandidate candidate)
        {
            var matches = ReadCandidates().Where(c => c.Key == candidate.Key).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("The source note changed, was resolved, or is ambiguous. Refresh the notes before editing.");
            if (matches[0].Unreadable) throw new InvalidOperationException("Paratext could not read the source discussion. No automatic edit is permitted; handle the note in Paratext.");
            if (matches[0].CrossChapter) throw new InvalidOperationException("This preview does not apply cross-chapter or cross-book edits. Review the note in Paratext.");
            return matches[0];
        }
        public void Apply(NoteCandidate candidate, string original, string replacement, ContentRegion region, bool reviewed, bool explicitDeletion = false, Action beforeSubmit = null, Action<PassageEvidence> captureEvidence = null)
        {
            CheckContext();
            if (region == ContentRegion.Structure) throw new InvalidOperationException("Choose the structural marker review action for this proposal.");
            if (candidate.Proposal.Route == ProposalRoute.NoChange) throw new InvalidOperationException("This is a no-change proposal.");
            if (candidate.Proposal.Route == ProposalRoute.Structure) throw new InvalidOperationException("Structural proposals cannot be applied through a text action.");
            if (String.IsNullOrEmpty(replacement) && !(reviewed && explicitDeletion)) throw new InvalidOperationException("An empty replacement requires the explicit deletion option and review.");
            if (replacement != null && Regex.IsMatch(replacement, @"[A-Za-z]|\s/\s|[\u2026]|\.\.\."))
                throw new InvalidOperationException("Choose one literal Tamil correction without English commentary, alternative separators, or omission placeholders. This preview does not apply Latin-text replacements; review those directly in Paratext.");
            int scope = region == ContentRegion.Introduction ? 0 : candidate.Chapter;
            if (!project.CanEdit(plugin, candidate.Book, scope)) throw new InvalidOperationException("You do not have editing permission for this passage.");
            // In the real host, history checkpoints can release outstanding editor/plugin locks.
            // Take the checkpoint before requesting our lock, then re-read all data under the new lock.
            project.MakeHistoryPoint("Before Note Suggestions correction", true);
            bool revoked = false;
            using (var writeLock = project.RequestWriteLock(plugin, delegate { revoked = true; }, candidate.Book, scope))
            {
                if (writeLock == null) throw new InvalidOperationException("Paratext could not grant a Scripture write lock. Try again when the passage is available.");
                var fresh = Fresh(candidate);
                if (!reviewed && (fresh.Proposal.Route != ProposalRoute.Apply || original != fresh.Proposal.Original || replacement != fresh.Proposal.Suggested || region != fresh.Proposal.Region))
                    throw new InvalidOperationException("This correction requires review before applying.");
                var tokens = project.GetUSFMTokens(candidate.Book, scope).ToList();
                string before = project.ConvertToUSFMString(tokens);
                string passageBefore = captureEvidence == null ? null : Passage(tokens, candidate, region);
                var match = ExactEditor.Locate(TokenMapping.Parts(tokens), region, candidate.Chapter, candidate.FirstVerse, candidate.LastVerse, original, replacement,
                    original == fresh.AnchorText ? fresh.Before : "", original == fresh.AnchorText ? fresh.After : "");
                var old = tokens[match.TokenIndex] as IUSFMTextToken;
                if (old == null) throw new InvalidOperationException("The selected target is not a Scripture text token.");
                tokens[match.TokenIndex] = new ChangedTextToken(old, ExactEditor.Replace(old.Text, match));
                string expected = project.ConvertToUSFMString(tokens);
                if (captureEvidence != null) captureEvidence(new PassageEvidence { Before = passageBefore, After = Passage(tokens, candidate, region), Scope = region == ContentRegion.Introduction ? "Full book introduction (USFM)" : "Full anchored passage (USFM)" });
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn. No edit was submitted.");
                if (beforeSubmit != null) beforeSubmit();
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn before submission.");
                var recovery = new RecoveryRecord { ProjectId = projectId, Book = candidate.Book, Chapter = scope, Before = before, After = expected };
                Recovery[projectId] = recovery;
                project.PutUSFMTokens(writeLock, tokens, candidate.Book);
                writeLock.SendNotifications();
                Verify(candidate.Book, scope, expected);
            }
            HistoryAfterWrite("Note Suggestions correction");
        }
        // Deliberately narrow structural operation: marker rename with identical associated text.
        // Verse/chapter numbering, note boundaries, attributes and verse exchanges remain review-only.
        public void ApplyMarkerRename(NoteCandidate candidate, Action beforeSubmit = null, Action<PassageEvidence> captureEvidence = null)
        {
            CheckContext();
            var p = candidate.Proposal;
            var a = Regex.Match(p.Original, @"^\\(ms[123]?|s[1234]?|cl|cd)(\s+[^\\\r\n]+)$");
            var b = Regex.Match(p.Suggested, @"^\\(ms[123]?|s[1234]?|cl|cd)(\s+[^\\\r\n]+)$");
            if (!a.Success || !b.Success || a.Groups[2].Value != b.Groups[2].Value || a.Groups[1].Value == b.Groups[1].Value)
                throw new InvalidOperationException("Only an exact heading/chapter-label marker rename with unchanged label text is supported in this preview. This structural proposal requires manual review in Paratext.");
            if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("You do not have editing permission for this chapter.");
            project.MakeHistoryPoint("Before Note Suggestions marker correction", true);
            bool revoked = false;
            using (var writeLock = project.RequestWriteLock(plugin, delegate { revoked = true; }, candidate.Book, candidate.Chapter))
            {
                if (writeLock == null) throw new InvalidOperationException("A Scripture write lock is unavailable.");
                Fresh(candidate);
                var tokens = project.GetUSFMTokens(candidate.Book, candidate.Chapter).ToList();
                // Find a whole label, not a substring of a longer heading, and only at the anchored verse.
                var indexes = new List<int>();
                for (int i = 0; i + 1 < tokens.Count; i++)
                {
                    var marker = tokens[i] as IUSFMMarkerToken; var label = tokens[i + 1] as IUSFMTextToken;
                    if (marker == null || label == null || marker.Marker != a.Groups[1].Value || marker.VerseRef == null) continue;
                    if (marker.VerseRef.ChapterNum != candidate.Chapter || marker.VerseRef.VerseNum < candidate.FirstVerse || marker.VerseRef.VerseNum > candidate.LastVerse) continue;
                    if (label.Text.TrimEnd('\r', '\n') == a.Groups[2].Value.TrimStart() && !(marker.Attributes ?? Enumerable.Empty<IUSFMAttribute>()).Any()) indexes.Add(i);
                }
                if (indexes.Count != 1) throw new InvalidOperationException("The exact structural label is missing or ambiguous at the note anchor. No marker was changed.");
                string before = project.ConvertToUSFMString(tokens);
                string passageBefore = captureEvidence == null ? null : Passage(tokens, candidate, ContentRegion.Heading);
                int index = indexes[0];
                var oldMarker = (IUSFMMarkerToken)tokens[index];
                tokens[index] = new ChangedMarkerToken(oldMarker, b.Groups[1].Value);
                string expected = project.ConvertToUSFMString(tokens);
                if (captureEvidence != null) captureEvidence(new PassageEvidence { Before = passageBefore, After = Passage(tokens, candidate, ContentRegion.Heading), Scope = "Full anchored heading/passage (USFM)" });
                // Reparse using the host to validate the generated USFM before writing.
                var reparsed = project.ConvertToUSFMTokens(expected, candidate.Book, candidate.Chapter, 0).ToList();
                if (project.ConvertToUSFMString(reparsed) != expected) throw new InvalidOperationException("Paratext's USFM round-trip changed the structural proposal; review it manually.");
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn.");
                if (beforeSubmit != null) beforeSubmit();
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn before submission.");
                Recovery[projectId] = new RecoveryRecord { ProjectId = projectId, Book = candidate.Book, Chapter = candidate.Chapter, Before = before, After = expected };
                project.PutUSFMTokens(writeLock, reparsed, candidate.Book);
                writeLock.SendNotifications(); Verify(candidate.Book, candidate.Chapter, expected);
            }
            HistoryAfterWrite("Note Suggestions marker correction");
        }
        void HistoryAfterWrite(string message)
        {
            try { project.MakeHistoryPoint(message, true); }
            catch (Exception ex) { ReportFailure(new InvalidOperationException("The USFM write was verified, but the post-edit history checkpoint failed.", ex)); }
        }
        public void ReportFailure(Exception error)
        {
            try { host.Log(plugin, "Note Suggestions error: {0}", error.ToString()); host.FlushLog(); }
            catch { /* Diagnostics must not hide the actual edit result. */ }
        }
        void Verify(int bookNumber, int chapterNumber, string expected)
        {
            string saved = project.ConvertToUSFMString(project.GetUSFMTokens(bookNumber, chapterNumber).ToList());
            if (saved != expected) throw new InvalidOperationException("An edit was submitted, but saved USFM does not match the expected result. Do not retry blindly. Review Paratext history and use Revert last plugin edit only if its current-text check succeeds.");
        }
        public void Revert()
        {
            CheckContext(); RecoveryRecord record;
            if (!Recovery.TryGetValue(projectId, out record)) throw new InvalidOperationException("No edit from this plugin session is available to revert. Use Paratext project history for older changes.");
            if (!project.CanEdit(plugin, record.Book, record.Chapter)) throw new InvalidOperationException("Editing permission is unavailable.");
            project.MakeHistoryPoint("Before reverting Note Suggestions correction", true);
            bool revoked = false;
            using (var writeLock = project.RequestWriteLock(plugin, delegate { revoked = true; }, record.Book, record.Chapter))
            {
                if (writeLock == null) throw new InvalidOperationException("A write lock is unavailable.");
                string current = project.ConvertToUSFMString(project.GetUSFMTokens(record.Book, record.Chapter).ToList());
                if (current != record.After) throw new InvalidOperationException("The saved passage changed after the plugin edit. Reverting would overwrite another edit, so nothing was changed. Review project history.");
                var tokens = project.ConvertToUSFMTokens(record.Before, record.Book, record.Chapter, 0).ToList();
                if (project.ConvertToUSFMString(tokens) != record.Before || revoked) throw new InvalidOperationException("The original USFM could not be restored exactly; no revert was submitted.");
                project.PutUSFMTokens(writeLock, tokens, record.Book); writeLock.SendNotifications(); Verify(record.Book, record.Chapter, record.Before);
                Recovery.Remove(projectId);
            }
            HistoryAfterWrite("Reverted Note Suggestions correction");
        }
    }

    public static class TokenMapping
    {
        public static List<TextPart> Parts(IList<IUSFMToken> tokens)
        {
            var result = new List<TextPart>(); ContentRegion region = ContentRegion.Body; ContentRegion saved = region;
            bool inNote = false;
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i]; var marker = token as IUSFMMarkerToken;
                if (marker != null)
                {
                    string m = marker.Marker.TrimStart('\\');
                    if (m == "f" || m == "fe" || m == "ef" || m == "x" || m == "ex") { saved = region; region = m == "x" || m == "ex" ? ContentRegion.CrossReference : ContentRegion.Footnote; inNote = true; }
                    else if (inNote && (m == "f*" || m == "fe*" || m == "ef*" || m == "x*" || m == "ex*")) { region = saved; inNote = false; }
                    else if (!inNote && (marker.Type == MarkerType.Verse || m == "c")) region = ContentRegion.Body;
                    else if (!inNote && Regex.IsMatch(m, @"^i(?:s|p|m|q|li|o|ot|ex|e|mt|s|d)")) region = ContentRegion.Introduction;
                    else if (!inNote && Regex.IsMatch(m, @"^(?:s[1-4]?|ms[1-3]?|mr|sr|r|d|sp|cl|cd|qa)$")) region = ContentRegion.Heading;
                    else if (!inNote && marker.Type == MarkerType.Paragraph) region = ContentRegion.Body;
                    continue;
                }
                var text = token as IUSFMTextToken;
                if (text == null || token.VerseRef == null || token.IsMetadata || token.IsFigure || token.IsSpecial) continue;
                // Unknown footnote context is excluded instead of being treated as Scripture body.
                if (token.IsFootnoteOrCrossReference && !inNote) continue;
                if ((region == ContentRegion.Footnote || region == ContentRegion.CrossReference) && !token.IsFootnoteOrCrossReference) continue;
                if (region == ContentRegion.Body && !token.IsScripture) continue;
                result.Add(new TextPart { TokenIndex = i, Region = region, Chapter = token.VerseRef.ChapterNum, Verse = token.VerseRef.VerseNum, Text = text.Text });
            }
            return result;
        }
    }
    public class ChangedTextToken : IUSFMTextToken
    {
        readonly IUSFMTextToken source; readonly string text;
        public ChangedTextToken(IUSFMTextToken original, string value) { source = original; text = value; }
        public string Text { get { return text; } }
        public IVerseRef VerseRef { get { return source.VerseRef; } }
        public int VerseOffset { get { return source.VerseOffset; } }
        public bool IsSpecial { get { return source.IsSpecial; } }
        public bool IsFigure { get { return source.IsFigure; } }
        public bool IsFootnoteOrCrossReference { get { return source.IsFootnoteOrCrossReference; } }
        public bool IsScripture { get { return source.IsScripture; } }
        public bool IsMetadata { get { return source.IsMetadata; } }
        public bool IsPublishableVernacular { get { return source.IsPublishableVernacular; } }
    }
    sealed class ChangedMarkerToken : IUSFMMarkerToken
    {
        readonly IUSFMMarkerToken source; readonly string marker;
        public ChangedMarkerToken(IUSFMMarkerToken original, string value) { source = original; marker = value; }
        public MarkerType Type { get { return source.Type; } }
        public string Marker { get { return marker; } }
        public IEnumerable<IUSFMAttribute> Attributes { get { return source.Attributes; } }
        public string Data { get { return source.Data; } }
        public string EndMarker { get { return source.EndMarker; } }
        public IVerseRef VerseRef { get { return source.VerseRef; } }
        public int VerseOffset { get { return source.VerseOffset; } }
        public bool IsSpecial { get { return source.IsSpecial; } }
        public bool IsFigure { get { return source.IsFigure; } }
        public bool IsFootnoteOrCrossReference { get { return source.IsFootnoteOrCrossReference; } }
        public bool IsScripture { get { return source.IsScripture; } }
        public bool IsMetadata { get { return source.IsMetadata; } }
        public bool IsPublishableVernacular { get { return source.IsPublishableVernacular; } }
    }
}
