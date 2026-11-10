using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Paratext.PluginInterfaces;

namespace ParatextNoteSuggestions
{
    // Paratext owns enabling/rendering the annotation layer. This never changes editor text.
    public sealed class QueueTargetHighlight : IPluginAnnotationSource
    {
        readonly IProject project;
        sealed class Target
        {
            public int Book, Chapter, Verse;
            public string Text, Before, After;
        }
        Target target;
        public QueueTargetHighlight(IProject project) { this.project = project; }
        public event EventHandler AnnotationsChanged;
        public bool MaintainSelectionsOnWordBoundaries { get { return false; } }
        public void SetTarget(NoteCandidate item)
        {
            target = item == null || item.Unreadable || item.CrossChapter || item.FirstVerse != item.LastVerse ||
                item.Book != item.EndBook || String.IsNullOrWhiteSpace(item.Proposal.Original) ? null :
                new Target { Book = item.Book, Chapter = item.Chapter, Verse = item.FirstVerse, Text = item.Proposal.Original,
                    Before = item.Proposal.Original == item.AnchorText ? item.Before : "",
                    After = item.Proposal.Original == item.AnchorText ? item.After : "" };
            var changed = AnnotationsChanged;
            if (changed != null) changed(this, EventArgs.Empty);
        }
        public IReadOnlyList<AnnotationStyle> GetStyleInfo(double zoom)
        { return new[] { new AnnotationStyle("queuedTarget", "background-color:#FFE082;color:#111111;") }; }
        public IReadOnlyList<IPluginAnnotation> GetAnnotations(IVerseRef verseRef, string usfm)
        {
            var pinned = target;
            var empty = new IPluginAnnotation[0];
            if (pinned == null || verseRef == null || verseRef.BookNum != pinned.Book ||
                verseRef.ChapterNum != pinned.Chapter || verseRef.VerseNum != pinned.Verse || String.IsNullOrEmpty(usfm)) return empty;
            try
            {
                var matches = project.FindMatchingScriptureSelections(verseRef, pinned.Text, usfm, false, false);
                if (matches == null) return empty;
                var exact = matches.Where(s => s.SelectedText == pinned.Text &&
                    SameVerse(s.VerseRefStart, pinned) && SameVerse(s.VerseRefEnd, pinned) &&
                    (String.IsNullOrEmpty(pinned.Before) || (s.BeforeContext ?? "").EndsWith(pinned.Before, StringComparison.Ordinal)) &&
                    (String.IsNullOrEmpty(pinned.After) || (s.AfterContext ?? "").StartsWith(pinned.After, StringComparison.Ordinal))).ToList();
                return exact.Count == 1 ? new IPluginAnnotation[] { new Highlight(exact[0]) } : empty;
            }
            // Rendering is optional. A host selection failure must not interrupt a saved decision.
            catch (Exception) { return empty; }
        }
        static bool SameVerse(IVerseRef reference, Target pinned)
        { return reference != null && reference.BookNum == pinned.Book && reference.ChapterNum == pinned.Chapter && reference.VerseNum == pinned.Verse; }
        sealed class Highlight : IPluginAnnotation
        {
            readonly IScriptureTextSelection selection;
            public Highlight(IScriptureTextSelection selection) { this.selection = selection; }
            public IScriptureTextSelection ScriptureSelection { get { return selection; } }
            public string StyleName { get { return "queuedTarget"; } }
            public string Icon { get { return null; } }
            public string IconToolTipText { get { return "Original target of the current queued QA note"; } }
            public bool Click(MouseButton button, bool onIcon, Point location) { return false; }
        }
    }
}
