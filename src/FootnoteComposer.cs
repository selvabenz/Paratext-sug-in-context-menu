using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;

namespace ParatextNoteSuggestions
{
    // Footnote wording templates. {text} is replaced by the quoted words; the rest is fixed Tamil.
    public static class FootnotePresets
    {
        public static readonly string[] Defaults = {
            "சில பிரதிகளில் {text} இவ்வாறு உள்ளது.",
            "சில பிரதிகளில் {text} காணப்படவில்லை.",
            "சில பிரதிகளில் {text} என்று உள்ளது.",
            "சில பிரதிகளில் {text} என்பது காணப்படவில்லை.",
            "சில பிரதிகளில் இந்த வசனம் காணப்படவில்லை."
        };
        static string PathIn(string directory) { return Path.Combine(directory, "footnote-presets.xml"); }
        public static void Validate(string template)
        {
            if (String.IsNullOrWhiteSpace(template)) throw new InvalidOperationException("A footnote template is empty.");
            if (Regex.Matches(template, Regex.Escape("{text}")).Count > 1) throw new InvalidOperationException("A footnote template may contain {text} only once: " + template);
            NoteService.ValidateFootnoteText(template.Replace("{text}", "க").Trim());
        }
        public static List<string> Load(string directory)
        {
            string path = PathIn(directory);
            if (!File.Exists(path)) return Defaults.ToList();
            var valid = XElement.Load(path).Elements("template").Select(t => t.Value.Trim()).Where(t => { try { Validate(t); return true; } catch (InvalidOperationException) { return false; } }).ToList();
            return valid.Count > 0 ? valid : Defaults.ToList();
        }
        public static void Save(string directory, IList<string> templates)
        {
            if (templates.Count == 0) throw new InvalidOperationException("Keep at least one footnote template.");
            foreach (string t in templates) Validate(t);
            ReviewProgress.AtomicWrite(PathIn(directory), new XElement("footnotePresets", templates.Select(t => new XElement("template", t))));
        }
        public static string Render(string template, string text)
        { return template.Contains("{text}") ? template.Replace("{text}", (text ?? "").Trim()).Trim() : template.Trim(); }
    }
    // Composes one footnote for the current note and shows the exact USFM change before anything is written.
    sealed class FootnoteComposer : Form
    {
        static int S(int pixels) { return ParatextTheme.Scale(pixels); }
        readonly ListBox templates = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, MinimumSize = new Size(0, ParatextTheme.Scale(60)) };
        readonly TextBox quoted = new TextBox { Dock = DockStyle.Top };
        readonly RadioButton afterOriginal = new RadioButton { Text = "Right after the note's Original", AutoSize = true, Checked = true },
            endOfVerse = new RadioButton { Text = "At the end of the verse", AutoSize = true };
        readonly Label rendered = new Label(), reason = new Label { Tag = ParatextTheme.KeepColour };
        readonly DiffBox before = new DiffBox(), after = new DiffBox();
        readonly Button insert = ParatextTheme.CommandButton("Insert footnote"), edit = ParatextTheme.CommandButton("Edit templates..."), cancel = ParatextTheme.CommandButton("Cancel");
        readonly Timer debounce = new Timer { Interval = 250 };
        readonly Func<string, bool, FootnotePlan> preview;
        readonly Func<string> editorSelection, referenceSelection;
        readonly Action<IList<string>> saveTemplates;
        readonly NoteCandidate item;
        readonly Dictionary<Keys, Func<string>> sources = new Dictionary<Keys, Func<string>>();
        string source = "typed";
        List<string> list;
        public FootnotePlan Plan { get; private set; }
        public FootnoteComposer(NoteCandidate note, List<string> presets, Func<string, bool, FootnotePlan> previewFootnote, Func<string> editor, Func<string> reference, Action<IList<string>> save, Font tamil)
        {
            item = note; list = presets; preview = previewFootnote; editorSelection = editor; referenceSelection = reference; saveTemplates = save;
            Text = "Add footnote - " + note.DisplayReference;
            ParatextTheme.Window(this); MinimizeBox = false; ShowInTaskbar = false;
            Size = new Size(S(720), S(700)); MinimumSize = new Size(S(400), S(480)); StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(10), S(10), S(10), 0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<Control, SizeType, float> add = delegate(Control c, SizeType type, float height) { root.RowStyles.Add(new RowStyle(type, height)); root.Controls.Add(c, 0, root.RowCount++); };
            add(ParatextTheme.Wrap(new Label { Text = note.Proposal.Category + " | Original: " + NoteParser.Preview(note.Proposal.Original, 80) }, root), SizeType.AutoSize, 0);
            add(new Label { Text = "Template (Alt+1-9)", AutoSize = true, Margin = new Padding(0, S(6), 0, S(2)) }, SizeType.AutoSize, 0);
            add(templates, SizeType.Percent, 22);
            add(new Label { Text = "Quoted text for {text}", AutoSize = true, Margin = new Padding(0, S(6), 0, S(2)) }, SizeType.AutoSize, 0);
            add(quoted, SizeType.AutoSize, 0);
            var pull = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0) };
            AddSource(pull, "Original (Alt+O)", Keys.O, "note Original", () => note.Proposal.Original);
            AddSource(pull, "Suggested (Alt+S)", Keys.S, "note Suggested", () => NoteService.TamilLiteral(note.Proposal.Suggested, false) ? note.Proposal.Suggested : "");
            AddSource(pull, "Editor selection (Alt+P)", Keys.P, "Paratext editor selection", editorSelection);
            AddSource(pull, "Reference text (Alt+B)", Keys.B, "reference text", referenceSelection);
            AddSource(pull, "Clipboard (Alt+C)", Keys.C, "clipboard", () => Clipboard.ContainsText() ? Clipboard.GetText() : "");
            add(pull, SizeType.AutoSize, 0);
            var place = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, S(4), 0, 0) };
            place.Controls.Add(afterOriginal); place.Controls.Add(endOfVerse); add(place, SizeType.AutoSize, 0);
            rendered.Font = tamil; add(ParatextTheme.Wrap(rendered, root), SizeType.AutoSize, 0);
            before.Font = after.Font = tamil;
            add(new Label { Text = "Chapter USFM now", AutoSize = true, Margin = new Padding(0, S(6), 0, S(2)) }, SizeType.AutoSize, 0); add(before, SizeType.Percent, 39);
            add(new Label { Text = "After inserting", AutoSize = true, Margin = new Padding(0, S(6), 0, S(2)) }, SizeType.AutoSize, 0); add(after, SizeType.Percent, 39);
            reason.ForeColor = ParatextTheme.ColorOf("text_error"); add(ParatextTheme.Wrap(reason, root), SizeType.AutoSize, 0);
            FillTemplates(0);
            // The note's Original is the usual quotation; the end of the verse when it cannot be placed.
            quoted.Text = note.Proposal.Original; source = "note Original";
            templates.SelectedIndexChanged += delegate { Schedule(); };
            // Typing marks the text as the reviewer's own; pulled text sets its source itself.
            quoted.TextChanged += delegate { if (!quoted.Focused) return; source = "typed"; Schedule(); };
            afterOriginal.CheckedChanged += delegate { Schedule(); };
            debounce.Tick += delegate { debounce.Stop(); RefreshPreview(); };
            insert.Enabled = false; insert.Click += delegate { if (!insert.Enabled || Plan == null) return; DialogResult = DialogResult.OK; Close(); };
            cancel.DialogResult = DialogResult.Cancel;
            edit.Click += delegate { EditTemplates(); };
            Controls.Add(root); Controls.Add(ParatextTheme.DialogButtons(insert, edit, cancel)); AcceptButton = insert; CancelButton = cancel;
            Shown += delegate { RefreshPreview(); templates.Focus(); };
        }
        void AddSource(FlowLayoutPanel row, string caption, Keys key, string name, Func<string> value)
        {
            var button = ParatextTheme.CommandButton(caption);
            Func<string> read = () => { try { return (value() ?? "").Trim(); } catch (Exception) { return ""; } };
            sources[key] = () => { string text = read(); if (text.Length > 0) { quoted.Text = Regex.Replace(text, @"\s+", " "); source = name; Schedule(); } else reason.Text = "Nothing to take from the " + name + "."; return text; };
            button.Click += delegate { sources[key](); };
            row.Controls.Add(button);
        }
        void FillTemplates(int select)
        {
            templates.Items.Clear();
            for (int i = 0; i < list.Count; i++) templates.Items.Add((i < 9 ? (i + 1) + "  " : "    ") + list[i]);
            if (templates.Items.Count > 0) templates.SelectedIndex = Math.Max(0, Math.Min(select, templates.Items.Count - 1));
        }
        void Schedule() { Plan = null; insert.Enabled = false; debounce.Stop(); debounce.Start(); }
        void RefreshPreview()
        {
            Plan = null; insert.Enabled = false; reason.Text = "";
            if (templates.SelectedIndex < 0) return;
            string template = list[templates.SelectedIndex];
            if (template.Contains("{text}") && quoted.Text.Trim().Length == 0) { rendered.Text = "Footnote: " + template; reason.Text = "Enter or pull the quoted text."; return; }
            string text = FootnotePresets.Render(template, quoted.Text);
            rendered.Text = "Footnote: " + text;
            try
            {
                var plan = preview(text, afterOriginal.Checked);
                plan.Template = template; plan.Source = template.Contains("{text}") ? source : "none";
                before.ShowChange(plan.Preview.Before, plan.Preview.After, false, ParatextTheme.ColorOf("background_light_red"));
                after.ShowChange(plan.Preview.After, plan.Preview.Before, true, ParatextTheme.ColorOf("background_light_green"));
                Plan = plan; insert.Enabled = true;
            }
            catch (InvalidOperationException ex) { before.Text = ""; after.Text = ""; reason.Text = ex.Message; }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var code = keyData & Keys.KeyCode;
            if ((keyData & Keys.Modifiers) == Keys.Alt)
            {
                Func<string> pull;
                if (sources.TryGetValue(code, out pull)) { pull(); return true; }
                if (code >= Keys.D1 && code <= Keys.D9 && code - Keys.D1 < templates.Items.Count) { templates.SelectedIndex = code - Keys.D1; return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        void EditTemplates()
        {
            using (var dialog = new Form { Text = "Footnote templates", StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, Size = new Size(S(560), S(360)), MinimumSize = new Size(S(340), S(240)) })
            {
                ParatextTheme.Window(dialog);
                var box = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, Dock = DockStyle.Fill, Font = before.Font, Text = String.Join("\r\n", list) };
                var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(10), S(10), S(10), 0) };
                body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                body.Controls.Add(ParatextTheme.Wrap(new Label { Text = "One template per line. {text} marks where the quoted words go; it may appear once." }, body)); body.Controls.Add(box);
                var save = ParatextTheme.CommandButton("Save"); var close = ParatextTheme.CommandButton("Cancel"); close.DialogResult = DialogResult.Cancel;
                save.Click += delegate {
                    var lines = box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    try { saveTemplates(lines); list = lines; FillTemplates(templates.SelectedIndex); dialog.DialogResult = DialogResult.OK; dialog.Close(); }
                    catch (InvalidOperationException ex) { MessageBox.Show(dialog, ex.Message, "Template not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                dialog.Controls.Add(body); dialog.Controls.Add(ParatextTheme.DialogButtons(save, close)); dialog.CancelButton = close;
                dialog.ShowDialog(this);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) debounce.Dispose(); base.Dispose(disposing); }
    }
}
