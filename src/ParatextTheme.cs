using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;
using Paratext.PluginInterfaces;

namespace ParatextNoteSuggestions
{
    // Gives every plugin window the look of Paratext's own windows. Inside Paratext the colours,
    // icons and toolbar renderer come from the theme Paratext is running (Default, Bright or a
    // custom one); elsewhere, such as in tests, from the theme files installed with Paratext.
    // Per-control colours follow the rules of Paratext's ThemeExtender.
    public static class ParatextTheme
    {
        static readonly Dictionary<string, Color> Fallback = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase) {
            { "background_control", Hex("f2f2f2") }, { "background_window", Hex("fcfcfc") }, { "background_gray", Hex("dedede") },
            { "background_light_gray", Hex("ededed") }, { "background_blue", Hex("c5d2e5") }, { "background_help", Hex("ffffd4") },
            { "background_light_green", Hex("c2f2c2") }, { "background_light_red", Hex("ffd9d9") }, { "background_light_bluer", Hex("e8f1ff") },
            { "infoBox_background", Hex("c5d2e5") }, { "listItem_selected_background", Hex("c5d2e5") },
            { "text_normal", Hex("191919") }, { "text_medium", Hex("303030") }, { "text_dimmed", Hex("8c8c8c") }, { "text_disabled", Hex("a1a1a1") },
            { "text_link", Hex("1252b3") }, { "text_error", Hex("cc1e14") }, { "text_warning", Hex("910700") }, { "text_correct", Hex("008006") },
            { "line_gray", Hex("dedede") }, { "line_dark_gray", Hex("b5b5b5") }, { "line_darker_gray", Hex("aaaaaa") }, { "line_blue", Hex("1252b3") },
            { "line_bluer", Hex("a6c9ff") }, { "background_dark_bluer", Hex("a6c9ff") }, { "background_bluer", Hex("cce0ff") }, { "progress", Hex("47cc4e") }
        };
        static readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Image> images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, Color> fileColors;
        static bool probed;
        static object liveTheme;
        static MethodInfo liveColor, liveImage;
        static Type liveRenderer;
        static Icon appIcon;
        static bool iconProbed;
        static Color Hex(string value) { return Color.FromArgb(Convert.ToInt32(value, 16) | unchecked((int)0xFF000000)); }

        // Paratext's own menus, toolbars and docking tabs use the Windows message font.
        public static Font Font { get { return SystemFonts.MessageBoxFont; } }
        public static Font Bold(Font font) { return new Font(font, FontStyle.Bold); }
        public static Font Larger(Font font, float factor) { return new Font(font.FontFamily, font.SizeInPoints * factor, FontStyle.Bold, GraphicsUnit.Point); }

        static void Probe()
        {
            if (probed) return;
            probed = true;
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var data = assemblies.FirstOrDefault(a => a.GetName().Name == "ParatextData");
                var host = assemblies.FirstOrDefault(a => a.GetName().Name == "ParatextBase");
                if (data == null || host == null) return;
                var theme = data.GetType("Paratext.Data.Themes.Theme");
                var extensions = host.GetType("Paratext.Base.Themes.ThemeEx");
                if (theme == null || extensions == null) return;
                var current = theme.GetField("Current", BindingFlags.Public | BindingFlags.Static);
                liveTheme = current == null ? null : current.GetValue(null);
                liveColor = extensions.GetMethod("GetColor", new[] { theme, typeof(string) });
                liveImage = extensions.GetMethod("GetImage", new[] { theme, typeof(string), typeof(bool) });
                liveRenderer = host.GetType("Paratext.Base.Themes.ThemeToolStripRenderer");
            }
            catch (Exception) { liveTheme = null; } // The theme is cosmetic; the installed files are used instead.
        }
        // Paratext's installation, which holds the Themes folder and Paratext.exe.
        static IEnumerable<string> InstallDirectories()
        {
            string location = null;
            try { location = Path.GetDirectoryName(typeof(IPluginHost).Assembly.Location); } catch (Exception) { }
            if (!String.IsNullOrEmpty(location)) yield return location;
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Paratext 9");
        }
        static string ThemeFile(params string[] parts)
        {
            foreach (string directory in InstallDirectories())
            {
                string path = Path.Combine(new[] { directory, "Themes", "Default" }.Concat(parts).ToArray());
                if (File.Exists(path)) return path;
            }
            return null;
        }
        static Dictionary<string, Color> FileColors()
        {
            if (fileColors != null) return fileColors;
            fileColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = ThemeFile("ColorMapping.xml");
                if (path == null) return fileColors;
                var mapping = XElement.Load(path);
                var named = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
                foreach (var color in mapping.Descendants("Color")) named[(string)color.Attribute("Name") ?? ""] = Hex((string)color.Attribute("ColorHexCode") ?? "000000");
                foreach (var item in mapping.Descendants("Item"))
                {
                    Color value;
                    if (named.TryGetValue((string)item.Attribute("Color") ?? "", out value)) fileColors[(string)item.Attribute("Name") ?? ""] = value;
                }
            }
            catch (Exception) { } // Built-in values below remain available.
            return fileColors;
        }
        // A colour by Paratext's logical name, such as "background_control" or "text_normal".
        public static Color ColorOf(string name)
        {
            Color value;
            if (colors.TryGetValue(name, out value)) return value;
            Probe();
            bool found = false;
            if (liveTheme != null && liveColor != null)
            {
                try { value = (Color)liveColor.Invoke(null, new[] { liveTheme, name }); found = true; } catch (Exception) { }
            }
            if (!found && !FileColors().TryGetValue(name, out value) && !Fallback.TryGetValue(name, out value)) value = SystemColors.Control;
            colors[name] = value; return value;
        }
        // A Paratext theme icon by file name without extension, such as "prev_left" or
        // "prev_left_mouseover". Shared: callers must not dispose it.
        public static Image ImageOf(string name)
        {
            Image value;
            if (images.TryGetValue(name, out value)) return value;
            Probe();
            if (liveTheme != null && liveImage != null)
            {
                try { value = (Image)liveImage.Invoke(null, new[] { liveTheme, (object)name, false }); } catch (Exception) { value = null; }
            }
            if (value == null)
            {
                try
                {
                    string path = ThemeFile("Images", name + ".png");
                    // Copied into memory so the installed file stays unlocked.
                    if (path != null) using (var stream = new MemoryStream(File.ReadAllBytes(path))) using (var file = System.Drawing.Image.FromStream(stream)) value = new Bitmap(file);
                }
                catch (Exception) { value = null; }
            }
            images[name] = value; return value;
        }
        // The Paratext application icon, so plugin windows carry the same icon as Paratext's own dialogs.
        public static Icon AppIcon
        {
            get
            {
                if (iconProbed) return appIcon;
                iconProbed = true;
                foreach (string directory in InstallDirectories())
                {
                    try { string exe = Path.Combine(directory, "Paratext.exe"); if (File.Exists(exe)) { appIcon = Icon.ExtractAssociatedIcon(exe); break; } }
                    catch (Exception) { }
                }
                return appIcon;
            }
        }
        // Image from a theme icon, for a docked window's tab.
        public static Icon IconFrom(string imageName)
        {
            var image = ImageOf(imageName) as Bitmap;
            if (image == null) return AppIcon;
            try { using (var copy = new Bitmap(image, Scale(16), Scale(16))) return Icon.FromHandle(copy.GetHicon()); }
            catch (Exception) { return AppIcon; }
        }
        public static ToolStripRenderer Renderer()
        {
            Probe();
            if (liveRenderer != null)
            {
                try { var renderer = Activator.CreateInstance(liveRenderer) as ToolStripRenderer; if (renderer != null) return renderer; } catch (Exception) { }
            }
            return new ToolStripProfessionalRenderer(new ThemeColorTable()) { RoundedEdges = false };
        }
        sealed class ThemeColorTable : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return ParatextTheme.ColorOf("background_control"); } }
            public override Color ToolStripGradientMiddle { get { return ParatextTheme.ColorOf("background_control"); } }
            public override Color ToolStripGradientEnd { get { return ParatextTheme.ColorOf("background_control"); } }
            public override Color ToolStripBorder { get { return ParatextTheme.ColorOf("line_gray"); } }
            public override Color StatusStripGradientBegin { get { return ParatextTheme.ColorOf("background_control"); } }
            public override Color StatusStripGradientEnd { get { return ParatextTheme.ColorOf("background_control"); } }
            public override Color ButtonSelectedHighlight { get { return ParatextTheme.ColorOf("background_blue"); } }
            public override Color ButtonSelectedGradientBegin { get { return ParatextTheme.ColorOf("background_blue"); } }
            public override Color ButtonSelectedGradientMiddle { get { return ParatextTheme.ColorOf("background_blue"); } }
            public override Color ButtonSelectedGradientEnd { get { return ParatextTheme.ColorOf("background_blue"); } }
            public override Color ButtonSelectedBorder { get { return ParatextTheme.ColorOf("line_blue"); } }
            public override Color ButtonPressedGradientBegin { get { return ParatextTheme.ColorOf("background_bluer"); } }
            public override Color ButtonPressedGradientMiddle { get { return ParatextTheme.ColorOf("background_bluer"); } }
            public override Color ButtonPressedGradientEnd { get { return ParatextTheme.ColorOf("background_bluer"); } }
            public override Color ButtonCheckedGradientBegin { get { return ParatextTheme.ColorOf("background_bluer"); } }
            public override Color ButtonCheckedGradientEnd { get { return ParatextTheme.ColorOf("background_bluer"); } }
            public override Color MenuItemSelected { get { return ParatextTheme.ColorOf("background_blue"); } }
            public override Color MenuItemBorder { get { return ParatextTheme.ColorOf("line_blue"); } }
            public override Color MenuBorder { get { return ParatextTheme.ColorOf("line_dark_gray"); } }
            public override Color ToolStripDropDownBackground { get { return ParatextTheme.ColorOf("background_window"); } }
            public override Color ImageMarginGradientBegin { get { return ParatextTheme.ColorOf("background_window"); } }
            public override Color ImageMarginGradientMiddle { get { return ParatextTheme.ColorOf("background_window"); } }
            public override Color ImageMarginGradientEnd { get { return ParatextTheme.ColorOf("background_window"); } }
            public override Color SeparatorDark { get { return ParatextTheme.ColorOf("line_dark_gray"); } }
            public override Color SeparatorLight { get { return ParatextTheme.ColorOf("background_window"); } }
            public override Color OverflowButtonGradientBegin { get { return ParatextTheme.ColorOf("background_control"); } }
            public override Color OverflowButtonGradientMiddle { get { return ParatextTheme.ColorOf("background_control"); } }
            public override Color OverflowButtonGradientEnd { get { return ParatextTheme.ColorOf("background_control"); } }
        }

        // Pixel sizes at 96 DPI, scaled to the display.
        static int dpi;
        public static int Scale(int pixels)
        {
            if (dpi == 0)
            {
                try { using (var screen = Graphics.FromHwnd(IntPtr.Zero)) dpi = (int)Math.Round(screen.DpiX); } catch (Exception) { dpi = 96; }
                if (dpi <= 0) dpi = 96;
            }
            return (int)Math.Round(pixels * dpi / 96.0);
        }
        // The command button size used by Paratext's dialogs (91 x 24 at 96 DPI); longer captions grow.
        public static Button CommandButton(string text)
        {
            return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly, MinimumSize = new Size(Scale(91), Scale(24)),
                Padding = new Padding(Scale(4), 0, Scale(4), 0), Margin = new Padding(Scale(2)), UseMnemonic = false };
        }
        // Toolbar command with Paratext's normal, mouse-over and disabled icon states.
        public static ToolStripButton ToolButton(string text, string imageName, string toolTip)
        {
            var normal = imageName == null ? null : ImageOf(imageName);
            var button = new ToolStripButton(text, normal) { ToolTipText = toolTip ?? text, DisplayStyle = normal == null ? ToolStripItemDisplayStyle.Text : ToolStripItemDisplayStyle.ImageAndText,
                ImageScaling = ToolStripItemImageScaling.SizeToFit, AutoToolTip = false };
            if (normal != null)
            {
                var hover = ImageOf(imageName + "_mouseover"); var disabled = ImageOf(imageName + "_disabled");
                Action refresh = delegate { button.Image = !button.Enabled && disabled != null ? disabled : button.Selected && hover != null ? hover : normal; };
                button.MouseEnter += delegate { refresh(); }; button.MouseLeave += delegate { refresh(); };
                button.EnabledChanged += delegate { refresh(); };
            }
            return button;
        }
        public static ToolStrip ToolBar()
        {
            return new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Renderer = Renderer(), Font = Font, Dock = DockStyle.Top,
                ImageScalingSize = new Size(Scale(16), Scale(16)), Padding = new Padding(Scale(4), Scale(2), Scale(4), Scale(2)), CanOverflow = true };
        }
        // OK/Cancel row at the bottom right, as in Paratext's dialogs; wraps on narrow windows.
        public static FlowLayoutPanel DialogButtons(params Control[] buttons)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true, Padding = new Padding(Scale(8), Scale(6), Scale(8), Scale(8)) };
            foreach (var button in buttons.Reverse()) row.Controls.Add(button);
            return row;
        }
        // Keeps a long label or check box inside its container's width, so narrow windows wrap it instead of clipping.
        public static T Wrap<T>(T label, Control within) where T : Control
        {
            // Labels wrap at their maximum width; Windows check boxes and radio buttons never
            // wrap by themselves, so their height is measured for the available width.
            var box = label is CheckBox || label is RadioButton ? (ButtonBase)(Control)label : null;
            label.AutoSize = box == null;
            EventHandler fit = delegate {
                int width = Math.Max(Scale(120), within.ClientSize.Width - within.Padding.Horizontal - label.Margin.Horizontal - Scale(4));
                if (box == null) { label.MaximumSize = new Size(width, 0); return; }
                int glyph = Scale(20);
                var text = TextRenderer.MeasureText(box.Text, box.Font, new Size(width - glyph, 0), TextFormatFlags.WordBreak);
                box.Size = new Size(Math.Min(width, text.Width + glyph + Scale(4)), Math.Max(Scale(20), text.Height + Scale(4)));
            };
            within.SizeChanged += fit; label.TextChanged += fit; label.FontChanged += fit; fit(within, EventArgs.Empty);
            return label;
        }
        // Splits list and details side by side in a wide window and one above the other in a narrow one.
        public static void Responsive(SplitContainer split, int wideAt, double listShare)
        {
            EventHandler fit = delegate {
                if (split.Width <= 0 || split.Height <= 0) return;
                var orientation = split.Width >= Scale(wideAt) ? Orientation.Vertical : Orientation.Horizontal;
                if (split.Orientation != orientation) split.Orientation = orientation;
                int length = orientation == Orientation.Vertical ? split.Width : split.Height;
                int distance = (int)(length * listShare);
                if (distance > split.Panel1MinSize && distance < length - split.Panel2MinSize - split.SplitterWidth) split.SplitterDistance = distance;
            };
            split.SizeChanged += fit;
        }
        // Prepares a top-level plugin window the way Paratext's dialogs are set up.
        public static void Window(Form form)
        {
            form.Font = Font; form.AutoScaleMode = AutoScaleMode.Font;
            if (AppIcon != null) form.Icon = AppIcon;
            form.BackColor = ColorOf("background_control"); form.ForeColor = ColorOf("text_normal");
            form.Load += delegate { Apply(form); };
        }

        // Colours every control below root by Paratext's ThemeExtender rules.
        public static void Apply(Control root)
        {
            if (root == null) return;
            Style(root);
            foreach (Control child in root.Controls) Apply(child);
            var strip = root as ToolStrip;
            if (strip != null && strip.RenderMode != ToolStripRenderMode.Custom) strip.Renderer = Renderer();
            if (!(root is ToolStrip) && !(root is WebBrowser))
            {
                // Controls added later, such as rebuilt rows, are styled as they arrive.
                root.ControlAdded -= Added; root.ControlAdded += Added;
            }
        }
        static void Added(object sender, ControlEventArgs e) { Apply(e.Control); }
        static bool InTab(Control control)
        {
            for (var parent = control.Parent; parent != null; parent = parent.Parent) if (parent is TabControl) return true;
            return false;
        }
        static void Style(Control control)
        {
            if (control is WebBrowser || control is ProgressBar || control is TabControl) return;
            // Paratext marks emphasised text and panels with their own colours; those are left as set.
            if (control.Tag as string == KeepColour) return;
            if (!(control is ToolStrip)) control.ForeColor = ColorOf("text_normal");
            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Standard; button.UseVisualStyleBackColor = false; button.Cursor = Cursors.Default;
                button.BackColor = ColorOf(button.Enabled ? "background_control" : "background_gray");
                button.EnabledChanged -= ButtonEnabled; button.EnabledChanged += ButtonEnabled;
                return;
            }
            var text = control as TextBoxBase;
            if (text != null) { text.BackColor = ColorOf(text.ReadOnly ? "background_control" : "background_window"); text.BorderStyle = BorderStyle.FixedSingle; WatchReadOnly(text); return; }
            var combo = control as ComboBox;
            if (combo != null) { combo.FlatStyle = FlatStyle.Flat; combo.BackColor = ColorOf(combo.DropDownStyle == ComboBoxStyle.DropDownList && !(combo.Parent is ToolStrip) ? "background_gray" : "background_window"); return; }
            if (control is ListBox || control is ListView || control is TreeView) { control.BackColor = ColorOf("background_window"); return; }
            var page = control as TabPage;
            if (page != null) { page.UseVisualStyleBackColor = false; page.BackColor = ColorOf("background_window"); return; }
            var link = control as LinkLabel;
            if (link != null) link.LinkColor = link.VisitedLinkColor = link.ActiveLinkColor = ColorOf("text_link");
            var check = control as CheckBox;
            if (check != null) check.UseVisualStyleBackColor = false;
            if (control is ToolStrip) { control.BackColor = ColorOf("background_control"); return; }
            control.BackColor = ColorOf(InTab(control) ? "background_window" : "background_control");
        }
        public const string KeepColour = "ParatextTheme.KeepColour";
        static void ButtonEnabled(object sender, EventArgs e) { var b = (Button)sender; b.BackColor = ColorOf(b.Enabled ? "background_control" : "background_gray"); }
        static readonly HashSet<TextBoxBase> watched = new HashSet<TextBoxBase>();
        static void WatchReadOnly(TextBoxBase text)
        {
            if (!watched.Add(text)) return;
            text.ReadOnlyChanged += delegate { text.BackColor = ColorOf(text.ReadOnly ? "background_control" : "background_window"); };
            text.Disposed += delegate { watched.Remove(text); };
        }
    }
}
