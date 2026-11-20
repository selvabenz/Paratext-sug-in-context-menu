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
    // One footnote insertion as previewed: the composer shows it, the write must reproduce it exactly.
    public sealed class FootnotePlan
    {
        public string Text = "", Template = "", Source = "", Usfm = "", Target = "";
        public bool AfterOriginal;
        public int Chapter, Verse;
        public PassageEvidence Preview;
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
        public SyncReferenceGroup NavigationGroup { get; set; }
        public NoteService(IPluginObject pluginObject, IPluginHost pluginHost, IParatextChildState state)
        {
            plugin = pluginObject; host = pluginHost; project = state.Project; projectId = project.ID;
            capturedContext = ContextKey(state);
            NavigationGroup = state.SyncReferenceGroup;
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
        public string QueueBookCode(int number) { var info = project.AvailableBooks.FirstOrDefault(b => b.Number == number); return info == null ? "Book " + number + " (unavailable)" : info.Code; }
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
        {
            HighlightTarget(item);
            host.OpenTextWindowFor(project, OpenWindowBehavior.UseExisting, item.TargetReference);
            ScrollProjectTo(item);
        }
        public void ScrollProjectTo(NoteCandidate item)
        {
            // None means no broadcast. Never move a second group selected by an
            // unrelated/previously active project window.
            if (NavigationGroup != SyncReferenceGroup.None) host.SetReferenceForSyncGroup(item.TargetReference, NavigationGroup);
        }
        // The host ignores a reference its editor already has, even after the user scrolled
        // that verse out of view. Visit a neighbouring verse first so the following Navigate
        // is a real reference change. Returns false when the project has no such neighbour.
        public bool BeginReturnToPassage(NoteCandidate item)
        {
            HighlightTarget(item);
            var adjacent = Adjacent(item);
            if (adjacent == null) return false;
            // The project window's own scroll group follows this host call. No other group
            // is sent to the neighbour: every extra window that hops echoes its own reference
            // change back through the host and can overtake the final target.
            host.OpenTextWindowFor(project, OpenWindowBehavior.UseExisting, adjacent);
            return true;
        }
        IVerseRef Adjacent(NoteCandidate item)
        {
            // Stay inside the chapter: crossing it makes a by-chapter editor load another
            // chapter only to leave it again on the next UI tick.
            var target = item.TargetReference;
            var previous = target.GetPreviousVerse(project); var following = target.GetNextVerse(project);
            if (previous != null && SameReference(previous, target)) previous = null;
            if (following != null && SameReference(following, target)) following = null;
            return previous != null && SameChapter(previous, target) ? previous :
                following != null && SameChapter(following, target) ? following : previous ?? following;
        }
        // Fast review keeps the keyboard in the queue: an editor in a scroll group follows
        // SetReferenceForSyncGroup without being activated, unlike OpenTextWindowFor.
        public bool EditorGrouped() { return ProjectWindows().Any(w => w.SyncReferenceGroup != SyncReferenceGroup.None); }
        public void QuietNavigate(NoteCandidate item) { HighlightTarget(item); ReassertNavigation(item, false); }
        public bool BeginQuietReturn(NoteCandidate item)
        {
            HighlightTarget(item);
            var adjacent = Adjacent(item);
            if (adjacent == null) return false;
            foreach (var group in ProjectWindows().Select(w => w.SyncReferenceGroup).Where(g => g != SyncReferenceGroup.None).Distinct().ToList())
                host.SetReferenceForSyncGroup(adjacent, group);
            return true;
        }
        // Reference texts are read-only, shown beside a note and never stored, exported or reported.
        // Paratext may refuse resource text to plugins without a resource access token.
        public List<string> ReferenceSourceNames()
        {
            try { return host.GetAllProjects(true).Where(p => p.ID != projectId).Select(p => p.ShortName).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception) { return new List<string>(); }
        }
        public string ReadReference(string name, NoteCandidate item, out string label)
        {
            var source = host.GetAllProjects(true).FirstOrDefault(p => p.ShortName == name);
            if (source == null) throw new InvalidOperationException(name + " is not available to plugins.");
            var references = new List<IVerseRef>();
            for (int v = item.FirstVerse; item.Chapter > 0 && v <= Math.Min(item.LastVerse, item.FirstVerse + 4); v++)
            {
                // The source may number verses differently; Paratext maps the reference.
                var mapped = project.Versification.CreateReference(item.Book, item.Chapter, v).ChangeVersification(source.Versification);
                foreach (var r in mapped.RepresentsMultipleVerses ? (IEnumerable<IVerseRef>)mapped.AllVerses : new[] { mapped })
                    if (!references.Any(x => SameReference(x, r))) references.Add(r);
            }
            label = name + " " + String.Join(", ", references.Select(r => r.ToString()));
            return String.Join(" ", references.Select(r => String.Concat(TokenMapping.Parts(source.GetUSFMTokens(r.BookNum, r.ChapterNum, r.VerseNum).ToList())
                .Where(t => t.Region == ContentRegion.Body).Select(t => t.Text)).Trim())).Trim();
        }
        // Fallback when plugins cannot read resources: Paratext's own windows, which follow the scroll group.
        public List<string> OpenReferenceWindows(IEnumerable<string> names, NoteCandidate item)
        {
            var lines = new List<string>();
            foreach (string name in names)
                try
                {
                    var enhanced = host.AllEnhancedResources.FirstOrDefault(r => r.ShortName == name);
                    if (enhanced != null) { host.OpenEnhancedResourceWindowFor(enhanced, OpenWindowBehavior.UseExisting, item.TargetReference); lines.Add(name + ": opened as an enhanced resource window."); continue; }
                    IReadOnlyProject source = host.AllResources.FirstOrDefault(r => r.ShortName == name) ?? host.GetAllProjects(true).FirstOrDefault(p => p.ShortName == name);
                    if (source == null) { lines.Add(name + ": not installed."); continue; }
                    host.OpenTextWindowFor(source, OpenWindowBehavior.UseExisting, item.TargetReference); lines.Add(name + ": opened.");
                }
                catch (Exception ex) { lines.Add(name + ": could not be opened (" + ex.Message + ")."); }
            return lines;
        }
        public List<string> TargetTexts(NoteCandidate item, ContentRegion region)
        {
            var tokens = project.GetUSFMTokens(item.Book, region == ContentRegion.Introduction || item.CrossChapter ? 0 : item.Chapter).ToList();
            return TokenMapping.Parts(tokens).Where(t => t.Region == region && (region == ContentRegion.Introduction || (t.Chapter == item.Chapter && t.Verse >= item.FirstVerse && t.Verse <= item.LastVerse)))
                .Select(t => t.Text).Where(t => t.Length > 0).ToList();
        }
        // What Paratext lets this plugin do, for fast review set-up. Logs sizes and outcomes, never resource text.
        public List<string> ProbeHost(NoteCandidate item)
        {
            var lines = new List<string> { "Paratext " + host.ApplicationVersion + ", project " + project.ShortName + "." };
            int book = item == null ? 1 : item.Book, chapter = item == null ? 1 : Math.Max(1, item.Chapter), verse = item == null ? 1 : Math.Max(1, item.FirstVerse);
            try
            {
                foreach (var source in host.GetAllProjects(true).Where(p => p.ID != projectId))
                {
                    string outcome;
                    try
                    {
                        int length = TokenMapping.Parts(source.GetUSFMTokens(book, chapter, verse).ToList()).Where(t => t.Region == ContentRegion.Body).Sum(t => t.Text.Length);
                        outcome = length > 0 ? "readable (" + length + " characters at this verse; text not logged)" : "no verse text returned";
                    }
                    catch (Exception ex) { outcome = "refused (" + ex.GetType().Name + ")"; }
                    lines.Add("Source " + source.ShortName + (source.IsResource ? " [resource, " : " [project, ") + source.Type + "]: " + outcome);
                }
            }
            catch (Exception ex) { lines.Add("Listing projects and resources failed: " + ex.GetType().Name + ": " + ex.Message); }
            try { lines.Add("Enhanced resources (no plugin text access): " + String.Join(", ", host.AllEnhancedResources.Select(r => r.ShortName))); }
            catch (Exception ex) { lines.Add("Enhanced resources unavailable: " + ex.GetType().Name); }
            try
            {
                foreach (var w in host.AllOpenWindows.Where(w => w != null && w.Project != null))
                {
                    var selected = w.Selections == null ? null : w.Selections.OfType<IScriptureTextSelection>().FirstOrDefault();
                    lines.Add("Window " + w.Project.ShortName + (w.Project.IsResource ? " (resource)" : "") + ", scroll group " + w.SyncReferenceGroup + ", selection " +
                        (selected == null ? "none reported" : (selected.SelectedText ?? "").Length + " characters"));
                }
            }
            catch (Exception ex) { lines.Add("Open windows unavailable: " + ex.GetType().Name); }
            if (item != null)
                foreach (bool afterOriginal in new[] { true, false })
                {
                    string where = afterOriginal ? "after Original" : "end of verse";
                    try { var plan = ForQueueItem(item).PreviewFootnote(item, "சோதனை", afterOriginal); lines.Add("Footnote dry run, " + where + " (nothing written): accepted at " + plan.Target + "; Paratext reparsed the chapter identically."); }
                    catch (Exception ex) { lines.Add("Footnote dry run, " + where + " (nothing written): refused: " + ex.Message); }
                }
            return lines;
        }
        public string EditorSelectionText()
        {
            var selection = ProjectWindows().Where(w => w.Selections != null).SelectMany(w => w.Selections.OfType<IScriptureTextSelection>()).FirstOrDefault(s => !String.IsNullOrEmpty(s.SelectedText));
            return selection == null ? "" : selection.SelectedText;
        }
        static bool SameReference(IVerseRef left, IVerseRef right)
        { return SameChapter(left, right) && left.VerseNum == right.VerseNum; }
        static bool SameChapter(IVerseRef left, IVerseRef right)
        { return left.BookNum == right.BookNum && left.ChapterNum == right.ChapterNum; }
        QueueTargetHighlight Highlight()
        {
            var suggestions = plugin as NoteSuggestionsPlugin;
            return suggestions == null ? null : suggestions.HighlightFor(project);
        }
        public void HighlightTarget(NoteCandidate item)
        {
            var highlight = Highlight();
            if (highlight != null) highlight.SetTarget(item);
        }
        // Every annotation change makes the host re-render the editors of this project. When
        // the editor must load another book anyway, that load renders the new target; asking
        // for a re-render of the book being left would only delay the navigation.
        public void PrepareHighlight(NoteCandidate item)
        {
            var highlight = Highlight();
            if (highlight != null) highlight.SetTarget(item, ProjectWindows().Any(w => w.VerseRef.BookNum == item.Book));
        }
        public void RefreshHighlight()
        {
            var highlight = Highlight();
            if (highlight != null) highlight.Refresh();
        }
        public bool HighlightPending { get { var highlight = Highlight(); return highlight != null && highlight.Pending; } }
        public string HighlightStatus { get { var highlight = Highlight(); return highlight == null ? "" : highlight.Status; } }
        public void SetInlineMenuHandler(Action<NoteCandidate, System.Drawing.Point> handler)
        {
            var highlight = Highlight();
            if (highlight != null) highlight.ContextMenuRequested = handler;
        }
        // Scripture editors report a selection; a notes list or another tool of the same
        // project does not, and its reference says nothing about where the editor is.
        List<IParatextChildState> ProjectWindows()
        {
            var all = host.AllOpenWindows.Where(w => w != null && w.Project != null && w.Project.ID == projectId && w.VerseRef != null).ToList();
            var editors = all.Where(w => w.Selections != null && w.Selections.OfType<IScriptureTextSelection>().Any()).ToList();
            return editors.Count > 0 ? editors : all;
        }
        public bool NavigationVisible(NoteCandidate item)
        {
            return ProjectWindows().Any(w => w.VerseRef.BookNum == item.Book && w.VerseRef.ChapterNum == item.Chapter &&
                (w.VerseRef.VerseNum == item.FirstVerse || (w.VerseRef.RepresentsMultipleVerses && w.VerseRef.AllVerses.Any(v => v.VerseNum == item.FirstVerse))));
        }
        // Sends the pinned reference again without activating a window, so a reviewer typing
        // in the queue keeps the keyboard. The host applies it only to editors that are not
        // already there. reopen permits the activating host call for an ungrouped editor that
        // cannot be reached any other way.
        public void ReassertNavigation(NoteCandidate item, bool reopen)
        {
            var windows = ProjectWindows();
            var groups = windows.Select(w => w.SyncReferenceGroup).Where(g => g != SyncReferenceGroup.None).Distinct().ToList();
            if (NavigationGroup != SyncReferenceGroup.None && !groups.Contains(NavigationGroup)) groups.Add(NavigationGroup);
            foreach (var group in groups) host.SetReferenceForSyncGroup(item.TargetReference, group);
            if (windows.Count > 0 && windows.All(w => w.SyncReferenceGroup != SyncReferenceGroup.None)) return;
            if (reopen) { host.OpenTextWindowFor(project, OpenWindowBehavior.UseExisting, item.TargetReference); return; }
            // For the None group the host moves the active window alone, so this is used only
            // while that window belongs to the pinned project.
            IParatextChildState active;
            try { active = host.ActiveWindowState; } catch (Exception) { active = null; } // The host throws when no window is active.
            if (active != null && active.Project != null && active.Project.ID == projectId && active.SyncReferenceGroup == SyncReferenceGroup.None)
                host.SetReferenceForSyncGroup(item.TargetReference, SyncReferenceGroup.None);
        }
        public void ValidateQueueDecision(NoteCandidate candidate, Action decision)
        {
            if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("Editing permission is unavailable for this passage.");
            bool revoked = false;
            using (var noteLock = project.RequestWriteLock(plugin, delegate { revoked = true; }, WriteLockScope.ProjectNotes))
            {
                if (noteLock == null) throw new InvalidOperationException("A project-notes lock is unavailable. The decision has not been recorded.");
                var fresh = ForQueueItem(candidate).ReadCandidates(true).Where(c => c.StableId == candidate.StableId).ToList();
                if (fresh.Count != 1 || fresh[0].Unreadable || fresh[0].IsResolved || fresh[0].Key != candidate.Key || revoked)
                    throw new InvalidOperationException("The source changed, was resolved, or is unavailable. Refresh the queue before recording a decision.");
                decision();
            }
        }
        public string CapturePassage(NoteCandidate item, ContentRegion region)
        {
            var numbers = item.EndBook > item.Book ? project.AvailableBooks.Select(b => b.Number).Where(b => b >= item.Book && b <= item.EndBook) : new[] { item.Book };
            return Passage(numbers.SelectMany(b => project.GetUSFMTokens(b, region == ContentRegion.Introduction || item.CrossChapter ? 0 : item.Chapter)).ToList(), item, region);
        }
        string Passage(List<IUSFMToken> tokens, NoteCandidate item, ContentRegion region)
        {
            // Include heading tokens carrying the preceding verse's reference in
            // approved before/after evidence as well as in exact target matching.
            if (region == ContentRegion.Heading) return project.ConvertToUSFMString(tokens);
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
                    if (source.Index != comments.FindLastIndex(c => !IsDecisionReply(c))) {
                        proposal.ReviewedBulkLiteral = false; proposal.Paste.Clear(); proposal.Fragment = false;
                        if (proposal.Route == ProposalRoute.Apply) proposal.Route = ProposalRoute.Review;
                    }
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
                    foreach (var c in group) {
                        c.Proposal.Conflict = String.Join(" | ", group.Select(o => o.Proposal.Suggested).Where(s => s != c.Proposal.Suggested).Distinct(StringComparer.Ordinal));
                        c.Proposal.ReviewedBulkLiteral = false;
                        if (c.Proposal.Route == ProposalRoute.Apply) c.Proposal.Route = ProposalRoute.Review;
                    }
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
                String.Join("|", note.Comments.Where(c => !IsDecisionReply(c)).Select(c => c.Created.ToString("O") + ":" + (c.Author == null ? "" : c.Author.Name) + ":" + Plain(c))));
        }
        static bool IsDecisionReply(IComment comment)
        { return Regex.IsMatch(Plain(comment), @"\A\[NoteSuggestions decision [A-F0-9]{64}\]\r?\n"); }
        static string ReplyHead(ReviewRecord record)
        {
            return "[NoteSuggestions decision " + NoteParser.Hash(record.Id + "|" + record.Outcome + "|" + record.DecisionUtc + "|" + record.Comment) + "]\r\n" +
                "Proposal: " + record.Id + "\r\nDecision: " + record.Outcome + "\r\nReviewer: " + record.Reviewer +
                "\r\nUTC: " + record.DecisionUtc + "\r\nCategory: " + record.Category;
        }
        static string ReplyComment(ReviewRecord record) { return "\r\nComment: " + (String.IsNullOrWhiteSpace(record.Comment) ? "Accepted as suggested." : record.Comment); }
        // The reply as written before rejections and manual fixes recorded the verse text. Still recognised, so an
        // earlier reply is never posted a second time.
        public static string LegacyReply(ReviewRecord record)
        { return ReplyHead(record) + "\r\nOriginal: " + record.Original + "\r\nReplacement: " + record.Replacement + ReplyComment(record); }
        // The reply is rebuilt from the saved record to find it again, so it uses nothing but the record.
        public static string DecisionReply(ReviewRecord record)
        {
            if (record.Outcome != "Rejected" && record.Outcome != "Completed manually") return LegacyReply(record);
            // The plugin wrote no Scripture for these: say so, and record the verse text the reviewer left.
            Func<string, string> line = text => Regex.Replace((text ?? "").Trim(), @"\s*[\r\n]+\s*", " ");
            string before = line(record.BeforeContext), after = line(record.AfterContext);
            bool known = after.Length > 0 && after.Length <= 600 && before.Length <= 600, changed = record.Outcome == "Completed manually" && before.Length > 0 && before != after;
            string result = record.Outcome == "Rejected" ? "Suggestion rejected. The plugin changed no text; the verse stays as it was." :
                changed ? "Fixed by hand in Paratext. The plugin did not apply the note's suggestion; the reviewer's own edit is shown below." :
                "No change made. The reviewer confirmed the text as it stands.";
            string verse = !known ? "\r\nVerse text: long passage; see the review report." :
                changed ? "\r\nVerse before: " + before + "\r\nVerse after: " + after : "\r\nVerse text: " + after;
            return ReplyHead(record) + "\r\nNote original: " + record.Original + "\r\nNote suggested: " + record.Replacement + "\r\nResult: " + result + verse + ReplyComment(record);
        }
        static bool HasReply(IProjectNote note, ReviewRecord record)
        { string reply = DecisionReply(record), legacy = LegacyReply(record); return note.Comments.Any(c => { string text = Plain(c); return text == reply || text == legacy; }); }
        static string DiscussionKey(IProjectNote note)
        { return NoteParser.Hash(String.Join("|", note.Comments.Where(c => !IsDecisionReply(c)).Select(c => c.Created.ToString("O") + ":" + (c.Author == null ? "" : c.Author.Name) + ":" + Plain(c)))); }
        static string SourceId(IProjectNote note, IComment comment)
        { return NoteParser.Hash(note.Anchor.VerseRefStart + "|" + note.Anchor.VerseRefEnd + "|" + comment.Created.ToString("O") + "|" + (comment.Author == null ? "" : comment.Author.Name) + "|" + Plain(comment)); }
        List<IProjectNote> FindSourceNotes(NoteCandidate candidate)
        {
            return project.GetNotes(candidate.Book, candidate.Chapter, false).Where(n => n.Anchor != null && n.Anchor.VerseRefStart != null && n.Anchor.VerseRefEnd != null &&
                n.Comments.Where(c => c.Created.ToString("O") == candidate.SourceCreated && (c.Author == null ? "" : c.Author.Name) == candidate.SourceAuthor).Any(c => SourceId(n, c) == candidate.StableId)).ToList();
        }
        // Separate from the Scripture transaction: the durable decision must already exist.
        public bool ResolveReviewedThread(NoteCandidate candidate, string reviewedDiscussion, Func<string, bool> completed, out List<string> sourceIds, ReviewRecord decision = null, Func<string, ReviewRecord> records = null)
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
                if (DiscussionKey(note) != reviewedDiscussion) throw new InvalidOperationException("The source discussion changed after the decision. Inspect and resolve the note manually in Paratext, then retry note resolution.");
                if (decision != null)
                {
                    if (!decision.Completed || decision.Id != candidate.StableId) throw new InvalidOperationException("A matching saved decision is required.");
                    string reply = DecisionReply(decision);
                    if (!HasReply(note, decision))
                    {
                        if (host.UserInfo == null || String.IsNullOrWhiteSpace(decision.Reviewer) || Reviewer != decision.Reviewer)
                            throw new InvalidOperationException("Sign in as the original reviewer (" + decision.Reviewer + ") to save this reply. The saved decision keeps its original attribution; do not repeat the Scripture edit.");
                        if (note.IsResolved) throw new InvalidOperationException("The note is already resolved without this decision reply. Inspect it in Paratext before recovery.");
                        if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter) || revoked) throw new InvalidOperationException("Project-notes permission or lock is unavailable.");
                        note.AddNewComment(writeLock, reply.Split(new[] { "\r\n" }, StringSplitOptions.None).Select(line => new CommentParagraph(new[] { new FormattedString(line) })), project.Language, host.UserInfo);
                        writeLock.SendNotifications();
                        var verified = FindSourceNotes(candidate);
                        if (revoked || verified.Count != 1 || !verified[0].Comments.Any(c => Plain(c) == reply))
                            throw new InvalidOperationException("Decision reply was not verified. Retry note resolution; do not repeat the Scripture edit.");
                        note = verified[0];
                    }
                }
                if (records != null && sourceIds.All(completed))
                    foreach (string id in sourceIds)
                    {
                        var peer = records(id);
                        if (peer == null || !HasReply(note, peer))
                            throw new InvalidOperationException("A proposal is missing its saved decision reply. Inspect the source note before resolving; local history alone cannot resolve this thread.");
                    }
                if (note.IsResolved) return true;
                if (!sourceIds.All(completed)) return false; // Do not hide other unreviewed proposals in the same note.
                if (revoked || !project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("The project-notes write lock or permission was withdrawn. Retry note resolution.");
                if (DiscussionKey(note) != reviewedDiscussion) throw new InvalidOperationException("The source discussion changed while saving the reply. Inspect the note before resolving.");
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
        // Read-only preview; Apply revalidates again under the Scripture write lock.
        public PassageEvidence PreviewBulkSandhi(NoteCandidate candidate)
        { return PreviewBulkCorrection(candidate, "Sandhi / word-joining"); }
        public PassageEvidence PreviewBulkConfirmedTypo(NoteCandidate candidate)
        { return PreviewBulkCorrection(candidate, "Confirmed typo"); }
        public static bool SupportsReviewedBulk(string category)
        { return new[] { "Sandhi / word-joining", "Confirmed typo", "Possible typo", "Plural agreement", "Punctuation", "Grammar" }.Contains(category); }
        static void ValidateBulkCorrection(NoteCandidate fresh, string category, bool reviewedSelection)
        {
            var p = fresh.Proposal;
            bool literal = reviewedSelection ? p.ReviewedBulkLiteral &&
                TamilLiteral(p.Original, category == "Punctuation") && TamilLiteral(p.Suggested, category == "Punctuation") :
                p.Route == ProposalRoute.Apply && !p.ReviewerDecision && TamilWords(p.Original) && TamilWords(p.Suggested);
            if (p.Category != category || !literal || p.Region == ContentRegion.Structure ||
                (!reviewedSelection && p.Region != ContentRegion.Body) || p.AnchorWarning ||
                fresh.Book != fresh.EndBook || fresh.CrossChapter || fresh.FirstVerse != fresh.LastVerse || fresh.IsResolved ||
                (p.Route != ProposalRoute.Apply && p.Route != ProposalRoute.Review))
                throw new InvalidOperationException("Bulk " + category + " requires one literal Tamil correction in a supported region, without ambiguous instructions, competing proposals or anchor warnings. Non-body corrections require the selected Scripture-edits preview. Review this proposal individually.");
        }
        public PassageEvidence PreviewBulkCorrection(NoteCandidate candidate, string category, bool reviewedSelection = false)
        {
            CheckContext();
            if (reviewedSelection ? !SupportsReviewedBulk(category) : category != "Sandhi / word-joining" && category != "Confirmed typo")
                throw new InvalidOperationException("Unsupported bulk correction category.");
            var fresh = Fresh(candidate);
            var p = fresh.Proposal;
            ValidateBulkCorrection(fresh, category, reviewedSelection);
            int scope = p.Region == ContentRegion.Introduction ? 0 : candidate.Chapter;
            if (!project.CanEdit(plugin, candidate.Book, scope)) throw new InvalidOperationException("Editing permission is unavailable.");
            var tokens = project.GetUSFMTokens(candidate.Book, scope).ToList();
            var match = ExactEditor.Locate(TokenMapping.Parts(tokens), p.Region, candidate.Chapter, candidate.FirstVerse, candidate.LastVerse,
                p.Original, p.Suggested, p.Original == fresh.AnchorText ? fresh.Before : "", p.Original == fresh.AnchorText ? fresh.After : "");
            var old = tokens[match.TokenIndex] as IUSFMTextToken;
            bool wordStart = WordCharacter(p.Original, 0), wordEnd = WordCharacter(p.Original, p.Original.Length - 1);
            if (old == null || (wordStart && match.Start > 0 && WordCharacter(old.Text, match.Start - 1)) ||
                (wordEnd && match.Start + match.Length < old.Text.Length && WordCharacter(old.Text, match.Start + match.Length)))
                throw new InvalidOperationException("Bulk corrections must match complete words; review this target individually.");
            // A formatting boundary can split a word without putting whitespace in either token.
            var regionParts = TokenMapping.Parts(tokens).Where(t => t.Region == p.Region &&
                (p.Region == ContentRegion.Introduction || (t.Chapter == candidate.Chapter && t.Verse == candidate.FirstVerse)) && t.Text.Length > 0).ToList();
            var previous = regionParts.LastOrDefault(t => t.TokenIndex < match.TokenIndex);
            var next = regionParts.FirstOrDefault(t => t.TokenIndex > match.TokenIndex);
            if ((wordStart && match.Start == 0 && previous != null && InlineAdjacent(tokens, previous.TokenIndex, match.TokenIndex, p.Region) && WordCharacter(previous.Text, previous.Text.Length - 1)) ||
                (wordEnd && match.Start + match.Length == old.Text.Length && next != null && InlineAdjacent(tokens, match.TokenIndex, next.TokenIndex, p.Region) && WordCharacter(next.Text, 0)))
                throw new InvalidOperationException("A word may cross a formatting boundary; review this target individually.");
            string before = Passage(tokens, candidate, p.Region);
            tokens[match.TokenIndex] = new ChangedTextToken(old, ExactEditor.Replace(old.Text, match));
            return new PassageEvidence { Before = before, After = Passage(tokens, candidate, p.Region), Scope = EvidenceScope(p.Region) };
        }
        static bool InlineAdjacent(IList<IUSFMToken> tokens, int left, int right, ContentRegion region)
        {
            // Separate paragraphs/notes are independent. In body text, an embedded
            // note does not establish a word boundary; keep split-word protection.
            for (int i = left + 1; i < right; i++) {
                var marker = tokens[i] as IUSFMMarkerToken;
                if (marker == null) continue;
                if (marker.Type == MarkerType.Paragraph || marker.Type == MarkerType.Verse || marker.Type == MarkerType.Chapter) return false;
                if ((region == ContentRegion.Footnote || region == ContentRegion.CrossReference) &&
                    Regex.IsMatch(marker.Marker.TrimStart('\\'), @"^(?:f|fe|ef|x|ex)\*?$")) return false;
            }
            return true;
        }
        static bool TamilWords(string text)
        {
            return !String.IsNullOrWhiteSpace(text) && text == text.Trim() && text.Any(c => c >= '\u0b80' && c <= '\u0bff') &&
                text.All(c => c == ' ' || (c >= '\u0b80' && c <= '\u0bff' && (Char.IsLetter(c) || Char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark || Char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.SpacingCombiningMark)));
        }
        internal static bool TamilLiteral(string text, bool punctuationOnly)
        {
            const string punctuation = ".,;:!?\u2018\u2019\u201c\u201d\u2013\u2014-'\"";
            return !String.IsNullOrWhiteSpace(text) && text == text.Trim() &&
                (punctuationOnly || text.Any(c => c >= '\u0b80' && c <= '\u0bff' && Char.IsLetter(c))) &&
                text.All(c => c == ' ' || (c >= '0' && c <= '9') || punctuation.IndexOf(c) >= 0 ||
                    (c >= '\u0b80' && c <= '\u0bff' && (Char.IsLetter(c) || Char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark || Char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.SpacingCombiningMark)));
        }
        static bool WordCharacter(string text, int index)
        {
            var kind = Char.GetUnicodeCategory(text, index);
            return Char.IsLetterOrDigit(text, index) || kind == System.Globalization.UnicodeCategory.NonSpacingMark || kind == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }
        static void ValidateReplacement(NoteCandidate candidate, string replacement, ContentRegion region, bool reviewed, bool explicitDeletion)
        {
            if (region == ContentRegion.Structure) throw new InvalidOperationException("Choose the structural marker review action for this proposal.");
            if (candidate.Proposal.Route == ProposalRoute.NoChange) throw new InvalidOperationException("This is a no-change proposal.");
            if (candidate.Proposal.Route == ProposalRoute.Structure) throw new InvalidOperationException("Structural proposals cannot be applied through a text action.");
            if (String.IsNullOrEmpty(replacement) && !(reviewed && explicitDeletion)) throw new InvalidOperationException("An empty replacement requires the explicit deletion option and review.");
            if (replacement != null && Regex.IsMatch(replacement, @"[A-Za-z]|\s/\s|[\u2026]|\.\.\."))
                throw new InvalidOperationException("Choose one literal Tamil correction without English commentary, alternative separators, or omission placeholders. This preview does not apply Latin-text replacements; review those directly in Paratext.");
        }
        static string EvidenceScope(ContentRegion region)
        { return region == ContentRegion.Introduction ? "Full book introduction (USFM)" : region == ContentRegion.Heading ? "Full chapter heading/passage (USFM)" : "Full anchored passage (USFM)"; }
        // Read-only: the exact passage before and after a reviewed Apply of this text. Apply repeats
        // every check under its write lock; fast review requires the two results to be identical.
        public PassageEvidence PreviewApply(NoteCandidate candidate, string original, string replacement, ContentRegion region)
        {
            CheckContext();
            ValidateReplacement(candidate, replacement, region, true, false);
            var fresh = Fresh(candidate);
            int scope = region == ContentRegion.Introduction ? 0 : candidate.Chapter;
            if (!project.CanEdit(plugin, candidate.Book, scope)) throw new InvalidOperationException("You do not have editing permission for this passage.");
            var tokens = project.GetUSFMTokens(candidate.Book, scope).ToList();
            var match = ExactEditor.Locate(TokenMapping.Parts(tokens), region, candidate.Chapter, candidate.FirstVerse, candidate.LastVerse, original, replacement,
                original == fresh.AnchorText ? fresh.Before : "", original == fresh.AnchorText ? fresh.After : "");
            var old = tokens[match.TokenIndex] as IUSFMTextToken;
            if (old == null) throw new InvalidOperationException("The selected target is not a Scripture text token.");
            string before = Passage(tokens, candidate, region);
            tokens[match.TokenIndex] = new ChangedTextToken(old, ExactEditor.Replace(old.Text, match));
            return new PassageEvidence { Before = before, After = Passage(tokens, candidate, region), Scope = EvidenceScope(region) };
        }
        public string Normalize(string text) { return project.NormalizeText(text); }
        // Read-only: where the Original is, when the note names a place that does not have it. A note often says
        // nothing about a heading or footnote it quotes. Another place is used only when it holds the one occurrence.
        public ContentRegion LocateRegion(NoteCandidate candidate, string original, ContentRegion expected)
        {
            if (String.IsNullOrEmpty(original) || expected == ContentRegion.Introduction || expected == ContentRegion.Structure || candidate.CrossChapter || candidate.Book != candidate.EndBook) return expected;
            var parts = TokenMapping.Parts(project.GetUSFMTokens(candidate.Book, candidate.Chapter).ToList())
                .Where(t => t.Chapter == candidate.Chapter && t.Verse >= candidate.FirstVerse && t.Verse <= candidate.LastVerse).ToList();
            Func<ContentRegion, int> count = r => parts.Where(t => t.Region == r).Sum(t => { int n = 0; for (int i = t.Text.IndexOf(original, StringComparison.Ordinal); i >= 0; i = t.Text.IndexOf(original, i + 1, StringComparison.Ordinal)) n++; return n; });
            if (count(expected) > 0) return expected;
            // Never fall back to the verse text: a note about a heading or footnote may quote verse words only to say where it belongs.
            var others = new[] { ContentRegion.Body, ContentRegion.Heading, ContentRegion.Footnote, ContentRegion.CrossReference }.Where(r => r != expected && count(r) > 0).ToList();
            return others.Count == 1 && count(others[0]) == 1 && others[0] != ContentRegion.Body ? others[0] : expected;
        }
        public static bool MarkerRename(Proposal p)
        {
            var a = Regex.Match(p.Original, @"^\\(ms[123]?|s[1234]?|cl|cd)(\s+[^\\\r\n]+)$"); var b = Regex.Match(p.Suggested, @"^\\(ms[123]?|s[1234]?|cl|cd)(\s+[^\\\r\n]+)$");
            return a.Success && b.Success && a.Groups[2].Value == b.Groups[2].Value && a.Groups[1].Value != b.Groups[1].Value;
        }
        // Read-only: the exact change a marker-bearing correction would make now. region tells which passage the
        // evidence covers: Body for the note's verses, Heading for the whole chapter when a heading before the verse changes.
        public PassageEvidence PreviewFragment(NoteCandidate candidate, string original, string replacement, out ContentRegion region)
        {
            CheckContext();
            Fresh(candidate);
            if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("You do not have editing permission for this chapter.");
            List<IUSFMToken> reparsed; string expected;
            return BuildFragment(project.GetUSFMTokens(candidate.Book, candidate.Chapter).ToList(), candidate, original, replacement, out reparsed, out expected, out region);
        }
        PassageEvidence BuildFragment(List<IUSFMToken> tokens, NoteCandidate candidate, string original, string replacement, out List<IUSFMToken> reparsed, out string expected, out ContentRegion region)
        {
            string problem = UsfmFragment.Problem(original, replacement);
            if (problem != null) throw new InvalidOperationException(problem + " Nothing was written.");
            if (candidate.CrossChapter || candidate.Book != candidate.EndBook || candidate.Chapter < 1) throw new InvalidOperationException("The note is not anchored in one chapter. Make this change in Paratext.");
            if (project.NormalizeText(replacement) != replacement) throw new InvalidOperationException("The suggested text is not in the project's Unicode normalization. Make this change in Paratext.");
            // A note or cross-reference origin the note introduces must name the note's own verse.
            foreach (Match origin in Regex.Matches(replacement, @"\\(?:fr|xo) (\d+)[:.](\d+)"))
                if (!original.Contains(origin.Value) && (origin.Groups[1].Value != candidate.Chapter.ToString() || Int32.Parse(origin.Groups[2].Value) < candidate.FirstVerse || Int32.Parse(origin.Groups[2].Value) > candidate.LastVerse))
                    throw new InvalidOperationException("The suggested note refers to " + origin.Groups[1].Value + ":" + origin.Groups[2].Value + ", not the note's verse. Nothing was written; make this change in Paratext.");
            string before = project.ConvertToUSFMString(tokens);
            var positions = UsfmFragment.Occurrences(before, original);
            if (positions.Count == 0) throw new InvalidOperationException("The exact Original, with its markers and spacing, was not found in this chapter. It may be stale or already corrected. No text was changed.");
            if (positions.Count > 12) throw new InvalidOperationException("The Original occurs " + positions.Count + " times in this chapter; the note does not identify one of them. No text was changed.");
            reparsed = null; expected = null; region = ContentRegion.Body;
            bool roundTrip = false; int fits = 0;
            // First the note's own verses; then also the verse before, whose reference a heading above the verse carries.
            foreach (bool wide in new[] { false, true })
            {
                foreach (int at in positions)
                {
                    string after = before.Substring(0, at) + replacement + before.Substring(at + original.Length);
                    var parsed = project.ConvertToUSFMTokens(after, candidate.Book, candidate.Chapter, 0).ToList();
                    if (project.ConvertToUSFMString(parsed) != after) { roundTrip = true; continue; }
                    if (!Confined(tokens, parsed, candidate, wide)) continue;
                    fits++; reparsed = parsed; expected = after;
                }
                if (fits > 0) { region = wide ? ContentRegion.Heading : ContentRegion.Body; break; }
            }
            if (fits == 0) throw new InvalidOperationException(roundTrip ? "Paratext's USFM round trip changed the suggested markers. Nothing was written; make this change in Paratext." :
                "The exact Original is in this chapter but not at the note's verse. No text was changed.");
            if (fits > 1) throw new InvalidOperationException("The Original occurs more than once at the note's verse. No text was changed.");
            CheckFragmentStructure(tokens, reparsed);
            // A note may quote verse words only to say where a heading or footnote belongs. Adding, removing or renaming
            // markers never changes the verse wording; only a change inside unchanged markers may.
            Func<List<IUSFMToken>, string> body = ts => Regex.Replace(String.Concat(TokenMapping.Parts(ts).Where(t => t.Region == ContentRegion.Body).Select(t => t.Text)), @"\s+", " ").Trim();
            if (UsfmFragment.MarkersDiffer(original, replacement) && body(tokens) != body(reparsed))
                throw new InvalidOperationException("The Suggested text changes markers and would also change the verse wording. It reads as an instruction or partial text, not an exact replacement. Nothing was written; make this change in Paratext.");
            return new PassageEvidence { Before = Passage(tokens, candidate, region), After = Passage(reparsed, candidate, region), Scope = EvidenceScope(region) };
        }
        // Everything before and after the note's verses must serialize exactly as it did.
        bool Confined(List<IUSFMToken> before, List<IUSFMToken> after, NoteCandidate candidate, bool wide)
        {
            int b0, b1, a0, a1;
            if (!Window(before, candidate, wide, out b0, out b1) || !Window(after, candidate, wide, out a0, out a1)) return false;
            return project.ConvertToUSFMString(before.Take(b0).ToList()) == project.ConvertToUSFMString(after.Take(a0).ToList()) &&
                project.ConvertToUSFMString(before.Skip(b1 + 1).ToList()) == project.ConvertToUSFMString(after.Skip(a1 + 1).ToList());
        }
        // The tokens of the note's verses. Wide adds only the heading directly above the first verse: heading text and
        // paragraph-level markers, back to the previous verse's own text, notes or the chapter marker, which stay outside.
        static bool Window(List<IUSFMToken> tokens, NoteCandidate candidate, bool wide, out int first, out int last)
        {
            Predicate<IUSFMToken> inside = t => t.VerseRef != null && t.VerseRef.ChapterNum == candidate.Chapter && t.VerseRef.VerseNum >= candidate.FirstVerse && t.VerseRef.VerseNum <= candidate.LastVerse;
            first = tokens.FindIndex(inside); last = tokens.FindLastIndex(inside);
            if (first < 0) return false;
            if (!wide) return true;
            var headings = new HashSet<int>(TokenMapping.Parts(tokens).Where(t => t.Region == ContentRegion.Heading).Select(t => t.TokenIndex));
            int start = first;
            while (start > 0)
            {
                var marker = tokens[start - 1] as IUSFMMarkerToken;
                if (marker != null ? marker.Type != MarkerType.Paragraph : !headings.Contains(start - 1)) break;
                start--;
            }
            if (start == first) return false;
            first = start; return true;
        }
        // Chapter and verse numbering must be untouched, and every footnote and cross-reference must open and close properly.
        static void CheckFragmentStructure(List<IUSFMToken> before, List<IUSFMToken> after)
        {
            Func<List<IUSFMToken>, string> numbering = ts => String.Join("|", ts.OfType<IUSFMMarkerToken>().Where(m => m.Type == MarkerType.Verse || m.Type == MarkerType.Chapter).Select(m => m.Marker + " " + m.Data));
            if (numbering(before) != numbering(after)) throw new InvalidOperationException("The change would alter chapter or verse numbering. Nothing was written; make this change in Paratext.");
            string open = null;
            foreach (var marker in after.OfType<IUSFMMarkerToken>())
            {
                string m = marker.Marker.TrimStart('\\');
                bool opens = m == "f" || m == "fe" || m == "ef" || m == "x" || m == "ex", closes = m == "f*" || m == "fe*" || m == "ef*" || m == "x*" || m == "ex*";
                bool breaks = marker.Type == MarkerType.Paragraph || marker.Type == MarkerType.Verse || marker.Type == MarkerType.Chapter;
                if ((opens && open != null) || (closes && open + "*" != m) || (breaks && open != null))
                    throw new InvalidOperationException("The change would leave a footnote or cross-reference without its proper opening or closing marker. Nothing was written; make this change in Paratext.");
                if (opens) open = m; else if (closes) open = null;
            }
            if (open != null) throw new InvalidOperationException("The change would leave a footnote or cross-reference unclosed. Nothing was written; make this change in Paratext.");
        }
        // Second general structural writer: one exact USFM replacement at the note's verse, proven by Paratext's own round trip.
        public void ApplyFragment(NoteCandidate candidate, string original, string replacement, Action beforeSubmit = null, Action<PassageEvidence> captureEvidence = null)
        {
            CheckContext();
            string problem = UsfmFragment.Problem(original, replacement);
            if (problem != null) throw new InvalidOperationException(problem + " Nothing was written.");
            if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("You do not have editing permission for this chapter.");
            // History checkpoints can release outstanding locks: take it before requesting ours.
            project.MakeHistoryPoint("Before Note Suggestions marker correction", true);
            bool revoked = false;
            using (var writeLock = project.RequestWriteLock(plugin, delegate { revoked = true; }, candidate.Book, candidate.Chapter))
            {
                if (writeLock == null) throw new InvalidOperationException("Paratext could not grant a Scripture write lock. Try again when the chapter is available.");
                Fresh(candidate);
                if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("Editing permission was withdrawn. No edit was submitted.");
                var tokens = project.GetUSFMTokens(candidate.Book, candidate.Chapter).ToList();
                string before = project.ConvertToUSFMString(tokens);
                List<IUSFMToken> reparsed; string expected; ContentRegion region;
                var evidence = BuildFragment(tokens, candidate, original, replacement, out reparsed, out expected, out region);
                if (captureEvidence != null) captureEvidence(evidence);
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn. No edit was submitted.");
                if (beforeSubmit != null) beforeSubmit();
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn before submission.");
                Recovery[projectId] = new RecoveryRecord { ProjectId = projectId, Book = candidate.Book, Chapter = candidate.Chapter, Before = before, After = expected };
                project.PutUSFMTokens(writeLock, reparsed, candidate.Book);
                writeLock.SendNotifications();
                Verify(candidate.Book, candidate.Chapter, expected);
            }
            HistoryAfterWrite("Note Suggestions marker correction");
        }
        public void Apply(NoteCandidate candidate, string original, string replacement, ContentRegion region, bool reviewed, bool explicitDeletion = false, Action beforeSubmit = null, Action<PassageEvidence> captureEvidence = null, bool reviewedBulkCorrection = false)
        {
            CheckContext();
            ValidateReplacement(candidate, replacement, region, reviewed, explicitDeletion);
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
                if (reviewedBulkCorrection) {
                    if (!reviewed || !SupportsReviewedBulk(fresh.Proposal.Category) || original != fresh.Proposal.Original || replacement != fresh.Proposal.Suggested || region != fresh.Proposal.Region)
                        throw new InvalidOperationException("A reviewed bulk correction must use the exact approved proposal.");
                    ValidateBulkCorrection(fresh, candidate.Proposal.Category, true);
                }
                if (!project.CanEdit(plugin, candidate.Book, scope)) throw new InvalidOperationException("Editing permission was withdrawn. No edit was submitted.");
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
                if (captureEvidence != null) captureEvidence(new PassageEvidence { Before = passageBefore, After = Passage(tokens, candidate, region), Scope = EvidenceScope(region) });
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
                if (captureEvidence != null) captureEvidence(new PassageEvidence { Before = passageBefore, After = Passage(tokens, candidate, ContentRegion.Heading), Scope = "Full chapter heading/passage (USFM)" });
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
        // A private-use character marks the insertion point while Paratext serializes the chapter.
        const char FootnoteMark = '';
        public static void ValidateFootnoteText(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The footnote text is empty.");
            if (text != text.Trim()) throw new InvalidOperationException("The footnote text starts or ends with a space.");
            if (Regex.IsMatch(text, @"[\\\r\n\0{}|" + FootnoteMark + "]")) throw new InvalidOperationException("A footnote cannot contain a backslash, line break, brace or bar; those would change USFM structure. Nothing was written.");
            if (Regex.IsMatch(text, "[A-Za-z]")) throw new InvalidOperationException("The footnote text contains Latin letters. Only Tamil footnotes are inserted here; add others in Paratext.");
        }
        // Read-only: the exact chapter change a footnote insertion would make now.
        public FootnotePlan PreviewFootnote(NoteCandidate candidate, string text, bool afterOriginal)
        {
            CheckContext();
            ValidateFootnoteText(text);
            var fresh = Fresh(candidate);
            if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("You do not have editing permission for this chapter.");
            List<IUSFMToken> reparsed; string expected;
            return BuildFootnote(project.GetUSFMTokens(candidate.Book, candidate.Chapter).ToList(), candidate, fresh, text, afterOriginal, out reparsed, out expected);
        }
        FootnotePlan BuildFootnote(List<IUSFMToken> tokens, NoteCandidate candidate, NoteCandidate fresh, string text, bool afterOriginal, out List<IUSFMToken> reparsed, out string expected)
        {
            if (project.NormalizeText(text) != text) throw new InvalidOperationException("The footnote text is not in the project's Unicode normalization. Retype it, or paste it from the project text.");
            string before = project.ConvertToUSFMString(tokens);
            if (before.IndexOf(FootnoteMark) >= 0) throw new InvalidOperationException("This chapter already contains a private-use character the footnote check relies on. Add the footnote in Paratext.");
            var body = TokenMapping.Parts(tokens).Where(t => t.Region == ContentRegion.Body && t.Chapter == candidate.Chapter && t.Verse >= candidate.FirstVerse && t.Verse <= candidate.LastVerse && t.Text.Length > 0).ToList();
            int index, offset, verse; string target;
            if (afterOriginal)
            {
                var p = candidate.Proposal;
                var match = ExactEditor.Find(body, ContentRegion.Body, candidate.Chapter, candidate.FirstVerse, candidate.LastVerse, p.Original,
                    p.Original == fresh.AnchorText ? fresh.Before : "", p.Original == fresh.AnchorText ? fresh.After : "");
                index = match.TokenIndex; offset = match.Start + match.Length; verse = body.First(t => t.TokenIndex == index).Verse; target = p.Original;
            }
            else
            {
                var last = body.LastOrDefault(t => t.Verse == candidate.LastVerse && t.Text.Trim().Length > 0);
                if (last == null) throw new InvalidOperationException("No verse text was found for the end-of-verse footnote. Add it in Paratext.");
                index = last.TokenIndex; offset = last.Text.TrimEnd().Length; verse = last.Verse;
                target = "[end of verse " + candidate.Chapter + ":" + verse + "]";
            }
            var old = tokens[index] as IUSFMTextToken;
            if (old == null || (old.VerseRef != null && old.VerseRef.RepresentsMultipleVerses)) throw new InvalidOperationException("The footnote position is in a bridged verse or not plain verse text. Add it in Paratext.");
            if (candidate.Chapter < 1 || verse < 1) throw new InvalidOperationException("The note is not anchored in a numbered verse. Add the footnote in Paratext.");
            if (offset > 0 && offset < old.Text.Length && WordCharacter(old.Text, offset - 1) && WordCharacter(old.Text, offset))
                throw new InvalidOperationException("The footnote call would split a word. Choose end of verse, or add it in Paratext.");
            // A character style can continue the same word in the next token without a space.
            var following = body.FirstOrDefault(t => t.TokenIndex > index && t.Verse == verse);
            if (offset == old.Text.Length && offset > 0 && WordCharacter(old.Text, offset - 1) && following != null &&
                InlineAdjacent(tokens, index, following.TokenIndex, ContentRegion.Body) && WordCharacter(following.Text, 0))
                throw new InvalidOperationException("The footnote call would split a word that continues in formatted text. Choose end of verse, or add it in Paratext.");
            if (TokenMapping.Parts(tokens).Any(t => t.Region == ContentRegion.Footnote && t.Chapter == candidate.Chapter && t.Verse == verse && t.Text.Trim() == text))
                throw new InvalidOperationException("This verse already has a footnote with exactly this text. Nothing was written.");
            string literal = "\\f + \\fr " + candidate.Chapter + ":" + verse + " \\ft " + text + "\\f*";
            var marked = tokens.ToList(); marked[index] = new ChangedTextToken(old, old.Text.Insert(offset, FootnoteMark.ToString()));
            string markedUsfm = project.ConvertToUSFMString(marked);
            if (markedUsfm.Count(c => c == FootnoteMark) != 1 || markedUsfm.Replace(FootnoteMark.ToString(), "") != before)
                throw new InvalidOperationException("Paratext changed other text while preparing the footnote. Nothing was written; add it in Paratext.");
            expected = markedUsfm.Replace(FootnoteMark.ToString(), literal);
            reparsed = project.ConvertToUSFMTokens(expected, candidate.Book, candidate.Chapter, 0).ToList();
            if (project.ConvertToUSFMString(reparsed) != expected) throw new InvalidOperationException("Paratext's USFM round trip changed the footnote insertion. Nothing was written; add it in Paratext.");
            CheckOneFootnoteAdded(tokens, reparsed, candidate.Chapter + ":" + verse + " " + text);
            return new FootnotePlan { Text = text, Usfm = literal, Target = target, AfterOriginal = afterOriginal, Chapter = candidate.Chapter, Verse = verse,
                Preview = new PassageEvidence { Before = Passage(tokens, candidate, ContentRegion.Body), After = Passage(reparsed, candidate, ContentRegion.Body), Scope = EvidenceScope(ContentRegion.Body) } };
        }
        // The reparsed chapter must differ from the original only by one \f + \fr \ft \f* note with the expected text.
        static void CheckOneFootnoteAdded(List<IUSFMToken> before, List<IUSFMToken> after, string noteText)
        {
            Func<List<IUSFMToken>, List<string>> markers = ts => ts.OfType<IUSFMMarkerToken>().Select(m => m.Marker.TrimStart('\\')).ToList();
            var a = markers(before); var b = markers(after); int k = 0;
            while (k < a.Count && k < b.Count && a[k] == b[k]) k++;
            bool shape = b.Count == a.Count + 4 && b.Skip(k).Take(4).SequenceEqual(new[] { "f", "fr", "ft", "f*" }) && b.Skip(k + 4).SequenceEqual(a.Skip(k));
            Func<List<IUSFMToken>, string> text = ts => String.Join("\u0001", TokenMapping.Parts(ts).Where(t => t.Region != ContentRegion.Footnote && t.Region != ContentRegion.CrossReference)
                .GroupBy(t => t.Region + ":" + t.Chapter + ":" + t.Verse).Select(g => g.Key + "=" + String.Concat(g.Select(t => t.Text))));
            var notes = TokenMapping.Parts(after).Where(t => t.Region == ContentRegion.Footnote).Select(t => t.Text).ToList();
            foreach (var existing in TokenMapping.Parts(before).Where(t => t.Region == ContentRegion.Footnote)) notes.Remove(existing.Text);
            Func<string, string> collapse = s => Regex.Replace(s, @"\s+", " ").Trim();
            if (!shape || text(before) != text(after) || collapse(String.Concat(notes)) != collapse(noteText))
                throw new InvalidOperationException("The prepared chapter would change more than one footnote. Nothing was written; add it in Paratext.");
        }
        public void InsertFootnote(NoteCandidate candidate, FootnotePlan plan, Action beforeSubmit = null, Action<PassageEvidence> captureEvidence = null)
        {
            CheckContext();
            ValidateFootnoteText(plan.Text);
            if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("You do not have editing permission for this chapter.");
            // History checkpoints can release outstanding locks: take it before requesting ours.
            project.MakeHistoryPoint("Before Note Suggestions footnote", true);
            bool revoked = false;
            using (var writeLock = project.RequestWriteLock(plugin, delegate { revoked = true; }, candidate.Book, candidate.Chapter))
            {
                if (writeLock == null) throw new InvalidOperationException("Paratext could not grant a Scripture write lock. Try again when the chapter is available.");
                var fresh = Fresh(candidate);
                if (!project.CanEdit(plugin, candidate.Book, candidate.Chapter)) throw new InvalidOperationException("Editing permission was withdrawn. No edit was submitted.");
                var tokens = project.GetUSFMTokens(candidate.Book, candidate.Chapter).ToList();
                string before = project.ConvertToUSFMString(tokens);
                List<IUSFMToken> reparsed; string expected;
                var built = BuildFootnote(tokens, candidate, fresh, plan.Text, plan.AfterOriginal, out reparsed, out expected);
                if (built.Usfm != plan.Usfm || built.Target != plan.Target) throw new InvalidOperationException("The footnote position changed after the preview. Nothing was written; preview it again.");
                if (captureEvidence != null) captureEvidence(built.Preview);
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn. No edit was submitted.");
                if (beforeSubmit != null) beforeSubmit();
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn before submission.");
                Recovery[projectId] = new RecoveryRecord { ProjectId = projectId, Book = candidate.Book, Chapter = candidate.Chapter, Before = before, After = expected };
                project.PutUSFMTokens(writeLock, reparsed, candidate.Book);
                writeLock.SendNotifications();
                Verify(candidate.Book, candidate.Chapter, expected);
            }
            HistoryAfterWrite("Note Suggestions footnote");
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
        public void Revert() { Revert(null); }
        void Revert(Action beforeSubmit)
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
                if (beforeSubmit != null) beforeSubmit();
                if (revoked) throw new InvalidOperationException("The write lock was withdrawn before the revert was submitted.");
                project.PutUSFMTokens(writeLock, tokens, record.Book); writeLock.SendNotifications(); Verify(record.Book, record.Chapter, record.Before);
                Recovery.Remove(projectId);
            }
            HistoryAfterWrite("Reverted Note Suggestions correction");
        }
        // Undo of the queue's last decision while its note reply is still held. Reverts only this
        // plugin's last edit, and only while the passage is exactly what that decision wrote.
        public void RevertDecision(NoteCandidate item, ReviewRecord record, Action beforeSubmit)
        {
            RecoveryRecord recovery; ContentRegion region;
            if (!Enum.TryParse(record.Region, out region)) region = ContentRegion.Body;
            int scope = region == ContentRegion.Introduction || item.CrossChapter ? 0 : item.Chapter;
            // The plugin's last edit must be this decision's edit: same scope, and the same passage before and after.
            // Another plugin edit made since (e.g. from the context menu) is never reverted in its place.
            if (!Recovery.TryGetValue(projectId, out recovery) || recovery.Book != item.Book || recovery.Chapter != scope || record.AfterContext.Length == 0 || record.BeforeContext.Length == 0 ||
                Passage(project.ConvertToUSFMTokens(recovery.After, recovery.Book, recovery.Chapter, 0).ToList(), item, region) != record.AfterContext ||
                Passage(project.ConvertToUSFMTokens(recovery.Before, recovery.Book, recovery.Chapter, 0).ToList(), item, region) != record.BeforeContext)
                throw new InvalidOperationException("The plugin's last edit in this session is not this decision. Nothing was reverted; use Paratext project history.");
            if (CapturePassage(item, region) != record.AfterContext)
                throw new InvalidOperationException("The passage changed after this decision. Undo would overwrite that change, so nothing was reverted. Use Paratext project history.");
            Revert(beforeSubmit);
            if (CapturePassage(item, region) != record.BeforeContext)
                throw new InvalidOperationException("Paratext restored the chapter, but the passage differs from the text before this decision. Check it in project history before continuing.");
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
                var reference = token.VerseRef;
                // Section headings precede the verse marker and can inherit the
                // previous verse's reference in the host token stream.
                if (region == ContentRegion.Heading) {
                    for (int next = i + 1; next < tokens.Count; next++) {
                        var following = tokens[next] as IUSFMMarkerToken;
                        if (following == null) continue;
                        if (following.Type == MarkerType.Verse) { reference = following.VerseRef ?? reference; break; }
                        if (following.Type == MarkerType.Chapter) break;
                    }
                }
                result.Add(new TextPart { TokenIndex = i, Region = region, Chapter = reference.ChapterNum, Verse = reference.VerseNum, Text = text.Text });
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
