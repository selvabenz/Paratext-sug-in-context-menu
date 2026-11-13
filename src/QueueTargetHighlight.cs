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
            public string Text, Before, After;
            public NoteCandidate Item;
        }
        Target target;
        double scale = 1;
        public Action<NoteCandidate, Point> ContextMenuRequested { get; set; }
        public QueueTargetHighlight(IProject project) { this.project = project; }
        public event EventHandler AnnotationsChanged;
        public bool MaintainSelectionsOnWordBoundaries { get { return false; } }
        public void SetTarget(NoteCandidate item)
        {
            target = item == null || item.Unreadable || item.CrossChapter || item.FirstVerse != item.LastVerse ||
                item.Book != item.EndBook || String.IsNullOrWhiteSpace(item.Proposal.Original) ? null :
                new Target { Book = item.Book, Chapter = item.Chapter, Verse = item.FirstVerse, Text = item.Proposal.Original,
                    Before = item.Proposal.Original == item.AnchorText ? item.Before : "",
                    After = item.Proposal.Original == item.AnchorText ? item.After : "", Item = item };
            var changed = AnnotationsChanged;
            if (changed != null) changed(this, EventArgs.Empty);
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
                return exact.Count == 1 ? new IPluginAnnotation[] { new Highlight(this, pinned, exact[0], InlineLabel(pinned.Item.Proposal.Suggested, scale)) } : empty;
            }
            // Rendering is optional. A host selection failure must not interrupt a saved decision.
            catch (Exception) { return empty; }
        }
        static bool SameVerse(IVerseRef reference, Target pinned)
        { return reference != null && reference.BookNum == pinned.Book && reference.ChapterNum == pinned.Chapter && reference.VerseNum == pinned.Verse; }
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
