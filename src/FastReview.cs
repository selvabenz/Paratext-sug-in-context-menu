using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace ParatextNoteSuggestions
{
    public enum NoLiteralMode { StopForMe, LaterRound, PreparedWording }
    public enum FastAction { None, Apply, Reject, Manual, Footnote, Skip, Previous, Next, Return, Preset, EditComment, BackToKeys, Undo, Wording }
    // Fast review settings, kept per Windows account and project beside the queue's other local state.
    public sealed class FastSettings
    {
        public bool Enabled, LetterKeys, HoldLastReply = true;
        public NoLiteralMode NoLiteral = NoLiteralMode.StopForMe;
        public string TamilFont = "Nirmala UI";
        public float TamilSize = 16;
        // One-based CommentPresets used by the reject key, by kind of category.
        public int RejectWording = 3, RejectMeaning = 4;
        public List<string> References = new List<string> { "ESVUS11", "TAOVBSI" };
        public Dictionary<FastAction, Keys> KeyMap = FastKeys.Defaults();
        static readonly string[] WordingCategories = { "Grammar", "Plural agreement", "Case/object marker", "Punctuation", "Name consistency", "Footnote / cross-reference",
            "USFM marker", "Sandhi / word-joining", "Confirmed typo", "Possible typo" };
        public int RejectPreset(string category) { return WordingCategories.Contains(category) ? RejectWording : RejectMeaning; }
        static string PathIn(string directory) { return Path.Combine(directory, "fast-settings.xml"); }
        public static FastSettings Load(string directory)
        {
            var s = new FastSettings(); string path = PathIn(directory);
            if (!File.Exists(path)) return s;
            var e = XElement.Load(path); NoLiteralMode mode;
            s.Enabled = (bool?)e.Attribute("enabled") ?? false; s.LetterKeys = (bool?)e.Attribute("letters") ?? false;
            s.HoldLastReply = (bool?)e.Attribute("holdLastReply") ?? true;
            if (Enum.TryParse((string)e.Attribute("noLiteral"), out mode) && Enum.IsDefined(typeof(NoLiteralMode), mode)) s.NoLiteral = mode;
            s.TamilFont = (string)e.Attribute("tamilFont") ?? s.TamilFont;
            s.TamilSize = Math.Max(8, Math.Min(40, (float?)e.Attribute("tamilSize") ?? s.TamilSize));
            s.RejectWording = Preset((int?)e.Attribute("rejectWording"), s.RejectWording); s.RejectMeaning = Preset((int?)e.Attribute("rejectMeaning"), s.RejectMeaning);
            if (e.Attribute("references") != null) s.References = e.Elements("reference").Select(r => (string)r.Attribute("name")).Where(n => !String.IsNullOrEmpty(n)).Distinct().ToList();
            // Only keys that cannot type text are accepted, and never two actions on one key.
            foreach (var k in e.Elements("key"))
            {
                FastAction action; Keys key;
                if (!Enum.TryParse((string)k.Attribute("action"), out action) || !s.KeyMap.ContainsKey(action) || !Enum.TryParse((string)k.Attribute("value"), out key) || !FastKeys.Safe(key)) continue;
                if (s.KeyMap.Any(p => p.Key != action && p.Value == key)) continue;
                s.KeyMap[action] = key;
            }
            return s;
        }
        static int Preset(int? value, int fallback) { return value.HasValue && value.Value >= 1 && value.Value <= CommentPresets.Values.Length ? value.Value : fallback; }
        public void Save(string directory)
        {
            ReviewProgress.AtomicWrite(PathIn(directory), new XElement("fast", new XAttribute("enabled", Enabled), new XAttribute("noLiteral", NoLiteral), new XAttribute("letters", LetterKeys),
                new XAttribute("holdLastReply", HoldLastReply), new XAttribute("tamilFont", TamilFont), new XAttribute("tamilSize", TamilSize),
                new XAttribute("rejectWording", RejectWording), new XAttribute("rejectMeaning", RejectMeaning), new XAttribute("references", "set"),
                References.Select(r => new XElement("reference", new XAttribute("name", r))),
                KeyMap.Select(p => new XElement("key", new XAttribute("action", p.Key), new XAttribute("value", p.Value)))));
        }
        public FastSettings Copy()
        {
            var c = (FastSettings)MemberwiseClone();
            c.References = References.ToList(); c.KeyMap = new Dictionary<FastAction, Keys>(KeyMap);
            return c;
        }
    }
    // What one fast key press would write, fixed when the note is shown. The write must reproduce Preview.
    public sealed class FastChoice
    {
        // The proposal this choice was worked out for; a decision for any other note is refused.
        public string Id = "";
        public string Original = "", Replacement = "", Comment = "", Source = "";
        public ContentRegion Region;
        public PassageEvidence Preview;
        public FootnotePlan Footnote;
        public bool OwnWording;
        // Fragment: a marker-bearing change written by ApplyFragment. Derived: the note's Tamil was read out of a
        // longer Suggested line, or found in another place than the note names.
        public bool Fragment, Derived;
    }
    public static class FastKeys
    {
        // Function and navigation keys cannot type into the Scripture editor if focus is there unexpectedly.
        public static Dictionary<FastAction, Keys> Defaults()
        {
            return new Dictionary<FastAction, Keys> { { FastAction.Apply, Keys.F5 }, { FastAction.Reject, Keys.F6 }, { FastAction.Manual, Keys.F7 }, { FastAction.Footnote, Keys.F8 },
                { FastAction.Skip, Keys.F9 }, { FastAction.Previous, Keys.PageUp }, { FastAction.Next, Keys.PageDown }, { FastAction.Return, Keys.Home },
                { FastAction.EditComment, Keys.F2 }, { FastAction.BackToKeys, Keys.Escape }, { FastAction.Undo, Keys.F11 }, { FastAction.Wording, Keys.F4 } };
        }
        static readonly Dictionary<Keys, FastAction> Letters = new Dictionary<Keys, FastAction> { { Keys.A, FastAction.Apply }, { Keys.R, FastAction.Reject }, { Keys.M, FastAction.Manual },
            { Keys.F, FastAction.Footnote }, { Keys.S, FastAction.Skip }, { Keys.P, FastAction.Previous }, { Keys.N, FastAction.Next }, { Keys.H, FastAction.Return }, { Keys.U, FastAction.Undo }, { Keys.W, FastAction.Wording } };
        public static bool Safe(Keys key)
        { return (key >= Keys.F1 && key <= Keys.F24) || key == Keys.PageUp || key == Keys.PageDown || key == Keys.Home || key == Keys.End || key == Keys.Insert || key == Keys.Escape; }
        public static FastAction Action(Keys keyData, FastSettings settings, out int preset)
        {
            preset = -1;
            var code = keyData & Keys.KeyCode; var modifiers = keyData & Keys.Modifiers;
            if (modifiers == Keys.Alt && code >= Keys.D0 && code <= Keys.D5) { preset = code - Keys.D0; return FastAction.Preset; }
            if (modifiers != Keys.None) return FastAction.None;
            foreach (var pair in settings.KeyMap) if (pair.Value == code) return pair.Key;
            if (!settings.LetterKeys) return FastAction.None;
            if (code >= Keys.D0 && code <= Keys.D5) { preset = code - Keys.D0; return FastAction.Preset; }
            FastAction letter;
            return Letters.TryGetValue(code, out letter) ? letter : FastAction.None;
        }
        // Keys that type or move the caret belong to an editable box while it has focus.
        public static bool TypesInText(Keys keyData)
        {
            var code = keyData & Keys.KeyCode;
            if ((keyData & Keys.Modifiers) == Keys.Alt) return false;
            return (code >= Keys.A && code <= Keys.Z) || (code >= Keys.D0 && code <= Keys.D9) || code == Keys.PageUp || code == Keys.PageDown || code == Keys.Home || code == Keys.End;
        }
        public static string Label(Keys key) { return key == Keys.PageUp ? "PgUp" : key == Keys.PageDown ? "PgDn" : key == Keys.Escape ? "Esc" : key.ToString(); }
        public static string Legend(FastSettings s)
        {
            Func<FastAction, string> k = a => Label(s.KeyMap[a]) + (s.LetterKeys && Letters.ContainsValue(a) ? "/" + Letters.First(p => p.Value == a).Key : "");
            return k(FastAction.Apply) + " apply   " + k(FastAction.Reject) + " reject   " + k(FastAction.Manual) + " fixed by hand   " + k(FastAction.Footnote) + " footnote   " +
                k(FastAction.Skip) + " skip   " + k(FastAction.Wording) + " other wording   " + k(FastAction.Previous) + "/" + k(FastAction.Next) + " browse   " + k(FastAction.Return) + " passage   Alt+1-5 comment   " +
                k(FastAction.EditComment) + " edit comment   " + k(FastAction.Undo) + " undo";
        }
    }
    // Large read-only passage with the changed text elements highlighted.
    public sealed class DiffBox : RichTextBox
    {
        public DiffBox()
        {
            ReadOnly = true; Multiline = true; WordWrap = true; DetectUrls = false; TabStop = false; HideSelection = false;
            ScrollBars = RichTextBoxScrollBars.Vertical; Dock = DockStyle.Fill; MinimumSize = new Size(0, ParatextTheme.Scale(60));
        }
        // RichTextBox stores line breaks as "\n"; compare in the same form so offsets match.
        public void ShowChange(string text, string other, bool afterSide, Color highlight)
        {
            text = (text ?? "").Replace("\r\n", "\n"); other = other == null ? null : other.Replace("\r\n", "\n");
            Text = text;
            if (other == null || other == text) { Select(0, 0); return; }
            int start, length;
            TextDiff.Changed(afterSide ? other : text, afterSide ? text : other, afterSide, out start, out length);
            if (length > 0) { Select(start, length); SelectionBackColor = highlight; }
            Select(Math.Min(start, TextLength), 0); ScrollToCaret();
        }
    }
    // Focus target for fast review keys; a click returns the keyboard to the queue.
    sealed class KeyPanel : Panel
    {
        public KeyPanel() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    }
    sealed class FastReviewDialog : Form
    {
        static int S(int pixels) { return ParatextTheme.Scale(pixels); }
        readonly FastSettings settings;
        readonly CheckBox enabled = new CheckBox { Text = "Use fast review: one key per note, comment filled in automatically" };
        readonly RadioButton stop = new RadioButton { Text = "Stop for me: I edit in Paratext, then press the fixed-by-hand key" },
            later = new RadioButton { Text = "Later round: leave them open and untouched in this run" },
            prepared = new RadioButton { Text = "Prepared wording: use an imported wording sheet, labelled as not from the note; otherwise stop for me" };
        readonly CheckBox hold = new CheckBox { Text = "Hold each note reply until the next decision, so a wrong key press can be undone completely" };
        readonly CheckBox letters = new CheckBox { Text = "Also accept single letters (A R M F S W P N H U, 0-5). Use only after a whole book showed no unexpected focus loss in the Log tab: a letter typed into the editor changes Scripture." };
        readonly TextBox font = new TextBox { Dock = DockStyle.Top };
        readonly NumericUpDown size = new NumericUpDown { Minimum = 8, Maximum = 40, DecimalPlaces = 0, Width = ParatextTheme.Scale(70) };
        readonly ComboBox rejectWording = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top },
            rejectMeaning = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly CheckedListBox references = new CheckedListBox { Dock = DockStyle.Top, CheckOnClick = true, Height = ParatextTheme.Scale(110), MinimumSize = new Size(0, ParatextTheme.Scale(60)) };
        public FastSettings Result { get; private set; }
        public FastReviewDialog(FastSettings current, IList<string> sources)
        {
            settings = current.Copy(); Text = "Fast review";
            ParatextTheme.Window(this); MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Size = new Size(S(560), S(700)); MinimumSize = new Size(S(380), S(420)); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(S(10), S(10), S(10), 0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<Control> add = delegate(Control c) { root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.Controls.Add(c, 0, root.RowCount++); };
            Action<string> heading = delegate(string title) { add(new Label { Text = title, AutoSize = true, Font = ParatextTheme.Bold(Font), Margin = new Padding(0, root.RowCount == 0 ? 0 : S(10), 0, S(4)) }); };
            add(ParatextTheme.Wrap(enabled, root));
            heading("Notes without ready-to-paste Tamil");
            foreach (var option in new Control[] { stop, later, prepared }) add(ParatextTheme.Wrap(option, root));
            heading("Safety");
            add(ParatextTheme.Wrap(hold, root)); add(ParatextTheme.Wrap(letters, root));
            heading("Tamil text");
            var fontRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
            fontRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fontRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fontRow.Controls.Add(font, 0, 0); fontRow.Controls.Add(size, 1, 0); add(fontRow);
            heading("Reject comment");
            add(ParatextTheme.Wrap(new Label { Text = "Wording categories (grammar, punctuation, plural, case marker, names, footnotes, markers)" }, root)); add(rejectWording);
            add(ParatextTheme.Wrap(new Label { Text = "Meaning categories (source comparison, textual basis, theology, missed or added words, reviewer decision)" }, root)); add(rejectMeaning);
            heading("Reference texts");
            add(ParatextTheme.Wrap(new Label { Text = "Shown beside each note when Paratext lets plugins read them; otherwise opened as Paratext windows from Tools." }, root));
            add(references);
            heading("Keys");
            add(ParatextTheme.Wrap(new Label { Text = FastKeys.Legend(settings) + ". Keys act only while the Fast review tab is open and the queue has the keyboard.", Tag = ParatextTheme.KeepColour, ForeColor = ParatextTheme.ColorOf("text_dimmed") }, root));
            enabled.Checked = settings.Enabled; hold.Checked = settings.HoldLastReply; letters.Checked = settings.LetterKeys;
            (settings.NoLiteral == NoLiteralMode.LaterRound ? later : settings.NoLiteral == NoLiteralMode.PreparedWording ? prepared : stop).Checked = true;
            font.Text = settings.TamilFont; size.Value = (decimal)Math.Max(8, Math.Min(40, settings.TamilSize));
            for (int i = 0; i < CommentPresets.Values.Length; i++) { string item = (i + 1) + ". " + CommentPresets.Values[i]; rejectWording.Items.Add(item); rejectMeaning.Items.Add(item); }
            rejectWording.SelectedIndex = settings.RejectWording - 1; rejectMeaning.SelectedIndex = settings.RejectMeaning - 1;
            foreach (string name in (sources ?? new List<string>()).Concat(settings.References).Distinct())
                references.Items.Add(name, settings.References.Contains(name));
            var ok = ParatextTheme.CommandButton("OK"); var cancel = ParatextTheme.CommandButton("Cancel"); cancel.DialogResult = DialogResult.Cancel;
            ok.Click += delegate {
                settings.Enabled = enabled.Checked; settings.HoldLastReply = hold.Checked; settings.LetterKeys = letters.Checked;
                settings.NoLiteral = later.Checked ? NoLiteralMode.LaterRound : prepared.Checked ? NoLiteralMode.PreparedWording : NoLiteralMode.StopForMe;
                settings.TamilFont = font.Text.Trim().Length == 0 ? "Nirmala UI" : font.Text.Trim(); settings.TamilSize = (float)size.Value;
                settings.RejectWording = rejectWording.SelectedIndex + 1; settings.RejectMeaning = rejectMeaning.SelectedIndex + 1;
                settings.References = references.CheckedItems.Cast<string>().ToList();
                Result = settings; DialogResult = DialogResult.OK; Close();
            };
            Controls.Add(root); Controls.Add(ParatextTheme.DialogButtons(ok, cancel)); AcceptButton = ok; CancelButton = cancel;
        }
    }
}
