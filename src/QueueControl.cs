using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Paratext.PluginInterfaces;

namespace ParatextNoteSuggestions
{
    // The review queue as a Paratext child window: Paratext owns its tab, docking, floating,
    // auto-hide and saved layout. The queue itself is the same QueueForm, hosted inside.
    public sealed class QueueControl : EmbeddedPluginControl
    {
        // Returned to Paratext for its saved layout. A window restored at startup waits for
        // the reviewer instead of scanning every book's notes while Paratext is opening.
        const string RestoredState = "queue";
        readonly NoteSuggestionsPlugin plugin;
        IPluginChildWindow window;
        IWindowPluginHost host;
        QueueForm form;
        // Tests keep progress and reports out of the reviewer's profile and project folder.
        public string ProgressRoot { get; set; }
        public string ReportDirectory { get; set; }
        public QueueForm Queue { get { return form; } }
        // Paratext creates the control itself when it restores a saved layout.
        public QueueControl() : this(null) { }
        public QueueControl(NoteSuggestionsPlugin owner)
        {
            plugin = owner ?? NoteSuggestionsPlugin.Current ?? new NoteSuggestionsPlugin();
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 250);
        }
        public override void OnAddedToParent(IPluginChildWindow parent, IWindowPluginHost windowHost, string state)
        {
            window = parent; host = windowHost;
            parent.SetTitle("Note review queue", false, true);
            // The queue pins the project it was opened for; another project gets its own window.
            parent.AllowUserToChangeProject = false;
            parent.WindowClosing += Closing;
            if (state == RestoredState) Offer("Review queue for this project.", "Open review queue");
            else Start();
        }
        public override string GetState() { return RestoredState; }
        public override void DoLoad(IProgressInfo progress) { }
        void Offer(string message, string action)
        {
            Controls.Clear();
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), AutoScroll = true };
            panel.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 3, 3, 10) });
            var open = new Button { Name = "openQueue", Text = action, AutoSize = true };
            open.Click += delegate { Start(); };
            panel.Controls.Add(open); Controls.Add(panel);
        }
        void Start()
        {
            try
            {
                var state = window.CurrentState;
                if (state == null || state.Project == null || state.VerseRef == null) throw new InvalidOperationException("Open the Scripture project whose notes you want to review first.");
                if (host.ApplicationVersion.Major != 9 || host.ApplicationVersion.Minor != 5) throw new InvalidOperationException("This preview targets Paratext 9.5.");
                var queue = new QueueForm(new NoteService(plugin, host, state), ProgressRoot, ReportDirectory);
                Controls.Clear(); form = queue;
                NoteSuggestionsPlugin.Track(state.Project.ID, queue);
                queue.Embed(this);
            }
            catch (Exception ex) { Offer(ex.Message, "Try again"); }
        }
        void Closing(IPluginChildWindow sender, CancelEventArgs e)
        {
            if (form == null || form.IsDisposed) return;
            // The queue saves its draft and session report on close, and refuses while a
            // decision is being written.
            form.Close();
            if (!form.IsDisposed) e.Cancel = true;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (window != null) window.WindowClosing -= Closing;
                if (form != null && !form.IsDisposed) form.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
