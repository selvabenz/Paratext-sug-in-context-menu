using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
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
            public string Text, Before, After, Suggested;
            public ContentRegion Region;
            public NoteCandidate Item;
            public bool Rendered, Matched;
        }
        Target target;
        bool requested;
        double scale = 1;
        public Action<NoteCandidate, Point> ContextMenuRequested { get; set; }
        public QueueTargetHighlight(IProject project) { this.project = project; }
        public event EventHandler AnnotationsChanged;
        public bool MaintainSelectionsOnWordBoundaries { get { return false; } }
        // The host re-renders every editor of the project for each AnnotationsChanged, and a
        // re-render that lands after a navigation request competes with its scrolling. Raise
        // the event only for a real change. notify: false leaves the new target to the next
        // render the host performs anyway; Refresh covers the case where none follows.
        public void SetTarget(NoteCandidate item, bool notify = true)
        {
            var next = item == null || item.Unreadable || item.CrossChapter || item.FirstVerse != item.LastVerse ||
                item.Book != item.EndBook || String.IsNullOrWhiteSpace(item.Proposal.Original) ? null :
                new Target { Book = item.Book, Chapter = item.Chapter, Verse = item.FirstVerse, Text = item.Proposal.Original,
                    Before = item.Proposal.Original == item.AnchorText ? item.Before : "",
                    After = item.Proposal.Original == item.AnchorText ? item.After : "",
                    Suggested = item.Proposal.Suggested, Region = item.Proposal.Region, Item = item };
            var old = target;
            if (old == null && next == null) return;
            if (old != null && next != null && old.Book == next.Book && old.Chapter == next.Chapter && old.Verse == next.Verse &&
                old.Region == next.Region && old.Text == next.Text && old.Before == next.Before && old.After == next.After && old.Suggested == next.Suggested)
            { old.Item = item; return; } // A reloaded copy of the same proposal keeps its rendered annotation and clicks.
            target = next;
            if (notify) Refresh();
        }
        public void Refresh()
        {
            // Paratext asks for annotations only while this layer is ticked in its menu, and
            // re-renders by itself when it is ticked. Until then a change has nothing to redraw.
            var changed = AnnotationsChanged;
            if (requested && changed != null) changed(this, EventArgs.Empty);
        }
        // True while the host has not asked for the target's verse since the target was set.
        public bool Pending { get { var pinned = target; return pinned != null && !pinned.Rendered; } }
        public string Status
        {
            get
            {
                var pinned = target;
                return pinned == null ? "No text highlight for this proposal (it spans verses or has no exact Original)." :
                    pinned.Matched ? "Original highlighted in the verse." :
                    pinned.Rendered ? "Original not found exactly once in the displayed verse; no text highlight." :
                    "Original not highlighted: check that \"Highlight queued note target\" is ticked in the project window's View menu.";
            }
        }
        // A section heading is displayed with the verse before it and a book introduction
        // before the first verse, while their notes are anchored on the verse that follows.
        static bool ShownIn(Target pinned, IVerseRef displayed)
        {
            if (displayed.BookNum != pinned.Book) return false;
            if (pinned.Region == ContentRegion.Introduction && displayed.VerseNum == 0 && displayed.ChapterNum <= 1) return true;
            return displayed.ChapterNum == pinned.Chapter && (displayed.VerseNum == pinned.Verse ||
                (pinned.Region == ContentRegion.Heading && displayed.VerseNum == pinned.Verse - 1));
        }
        public IReadOnlyList<AnnotationStyle> GetStyleInfo(double zoom)
        {
            scale = Double.IsNaN(zoom) || Double.IsInfinity(zoom) || zoom <= 0 ? 1 : Math.Max(.5, Math.Min(3, zoom));
            return new[] { new AnnotationStyle("queuedTarget", "background-color:#FFE082;color:#111111;") };
        }
        public IReadOnlyList<IPluginAnnotation> GetAnnotations(IVerseRef verseRef, string usfm)
        {
            var pinned = target;
            var empty = new IPluginAnnotation[0];
            requested = true;
            if (pinned == null || verseRef == null || !ShownIn(pinned, verseRef)) return empty;
            pinned.Rendered = true;
            if (String.IsNullOrEmpty(usfm)) return empty;
            try
            {
                var matches = project.FindMatchingScriptureSelections(verseRef, pinned.Text, usfm, false, false);
                if (matches == null) return empty;
                var exact = matches.Where(s => s.SelectedText == pinned.Text &&
                    SameVerse(s.VerseRefStart, verseRef) && SameVerse(s.VerseRefEnd, verseRef)).ToList();
                // As in ExactEditor.Locate: a single occurrence needs no context, and a stale
                // anchor context must not hide it. Context only chooses between repeats.
                if (exact.Count > 1 && (!String.IsNullOrEmpty(pinned.Before) || !String.IsNullOrEmpty(pinned.After)))
                    exact = exact.Where(s => (String.IsNullOrEmpty(pinned.Before) || (s.BeforeContext ?? "").EndsWith(pinned.Before, StringComparison.Ordinal)) &&
                        (String.IsNullOrEmpty(pinned.After) || (s.AfterContext ?? "").StartsWith(pinned.After, StringComparison.Ordinal))).ToList();
                if (exact.Count != 1) return empty;
                pinned.Matched = true;
                return new IPluginAnnotation[] { new Highlight(this, pinned, exact[0], InlineLabel(pinned.Suggested, scale)) };
            }
            // Rendering is optional. A host selection failure must not interrupt a saved decision.
            catch (Exception) { return empty; }
        }
        static bool SameVerse(IVerseRef reference, IVerseRef displayed)
        { return reference != null && reference.BookNum == displayed.BookNum && reference.ChapterNum == displayed.ChapterNum && reference.VerseNum == displayed.VerseNum; }
        // The host supports an image before the annotated text, rather than arbitrary editor text.
        // Render a compact suggestion label there; the tooltip/menu retains the full wording.
        static string InlineLabel(string suggested, double scale)
        {
            try
            {
                string label = "\u2192 " + (String.IsNullOrWhiteSpace(suggested) ? "Review note" : suggested.Replace('\r', ' ').Replace('\n', ' '));
                string directory = Path.Combine(Path.GetTempPath(), "ParatextNoteSuggestions", "InlineLabels");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, NoteParser.Hash(label + "|" + scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture)) + ".png");
                if (File.Exists(path)) return path;
                using (var font = new Font("Segoe UI", (float)(10 * scale)))
                {
                    var size = TextRenderer.MeasureText(label, font);
                    size.Width = Math.Min((int)(360 * scale), size.Width) + 8;
                    size.Height += 4;
                    using (var bitmap = new Bitmap(size.Width, size.Height))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.FromArgb(225, 242, 226));
                        TextRenderer.DrawText(graphics, label, font, new Rectangle(4, 2, size.Width - 8, size.Height - 4), Color.FromArgb(20, 85, 35),
                            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        bitmap.Save(path, ImageFormat.Png);
                    }
                }
                return path;
            }
            catch (Exception) { return null; } // A label failure must not suppress the exact target highlight.
        }
        sealed class Highlight : IPluginAnnotation
        {
            readonly QueueTargetHighlight source;
            readonly Target pinned;
            readonly IScriptureTextSelection selection;
            readonly string icon;
            public Highlight(QueueTargetHighlight source, Target pinned, IScriptureTextSelection selection, string icon)
            { this.source = source; this.pinned = pinned; this.selection = selection; this.icon = icon; }
            public IScriptureTextSelection ScriptureSelection { get { return selection; } }
            public string StyleName { get { return "queuedTarget"; } }
            public string Icon { get { return icon; } }
            public string IconToolTipText { get { return "Original: " + pinned.Text + "\r\nSuggested: " + pinned.Item.Proposal.Suggested + "\r\nRight-click to apply, reject, or ignore for now."; } }
            public bool Click(MouseButton button, bool onIcon, Point location)
            {
                var handler = source.ContextMenuRequested;
                if (button != MouseButton.Right || source.target != pinned || handler == null) return false;
                handler(pinned.Item, location); return true;
            }
        }
    }
}
