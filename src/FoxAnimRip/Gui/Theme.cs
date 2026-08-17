// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace FoxAnimRip.Gui;

public enum ThemeMode { System, Dark, Light }

/// <summary>
/// A dark palette for WinForms, which has no built-in dark mode.
///
/// Colours are set recursively on the control tree, and the title bar is
/// switched with DwmSetWindowAttribute. A few pieces cannot be recoloured
/// without owner-drawing them -- the progress bar and scroll bars keep the
/// system look. Everything here is best-effort: a theming failure must never
/// stop the tool from working, so every call is guarded.
/// </summary>
public static class Theme
{
    // Palette: slightly warm greys so it does not look like a dead pixel field.
    public static readonly Color Background = Color.FromArgb(32, 33, 36);
    public static readonly Color Surface = Color.FromArgb(42, 43, 47);
    public static readonly Color Field = Color.FromArgb(24, 25, 28);
    public static readonly Color Border = Color.FromArgb(64, 66, 72);
    public static readonly Color Text = Color.FromArgb(228, 229, 231);
    public static readonly Color TextDim = Color.FromArgb(150, 152, 158);
    public static readonly Color Accent = Color.FromArgb(96, 140, 220);

    public static bool IsDark { get; private set; }

    public static ThemeMode Parse(string value) => value?.ToLowerInvariant() switch
    {
        "light" => ThemeMode.Light,
        "system" => ThemeMode.System,
        _ => ThemeMode.Dark,
    };

    public static string Name(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.System => "system",
        _ => "dark",
    };

    /// <summary>Windows' own "apps use dark theme" setting.</summary>
    public static bool SystemPrefersDark()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
#pragma warning disable CA1416
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
#pragma warning restore CA1416
        }
        catch { return false; }
    }

    public static void Apply(Form form, ThemeMode mode)
    {
        IsDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark(),
        };

        try
        {
            form.SuspendLayout();
            ApplyTitleBar(form, IsDark);
            Walk(form, IsDark);
        }
        catch { /* cosmetics are never worth a crash */ }
        finally
        {
            try { form.ResumeLayout(); form.Invalidate(true); } catch { }
        }
    }

    private static void Walk(Control control, bool dark)
    {
        Paint(control, dark);
        foreach (Control child in control.Controls) Walk(child, dark);
    }

    /// <summary>
    /// A dark ListView.
    ///
    /// The first attempt at this owner-drew the column header and left the rows
    /// to the system with <c>DrawDefault</c>. That combination is unreliable in
    /// Details view -- rows come back unpainted or lose their checkboxes -- so
    /// the header is handed to the shell's own dark theme instead, which is what
    /// Explorer itself uses. Nothing here is drawn by hand.
    /// </summary>
    private static void PaintListView(ListView view, bool dark)
    {
        view.OwnerDraw = false;
        view.BackColor = dark ? Field : SystemColors.Window;
        view.ForeColor = dark ? Text : SystemColors.WindowText;
        view.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;

        void ApplyShellTheme()
        {
            try
            {
                SetWindowTheme(view.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                // The header is a child window with its own theme.
                var header = SendMessage(view.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero)
                    SetWindowTheme(header, dark ? "DarkMode_ItemsView" : "ItemsView", null);
            }
            catch { }
        }

        if (view.IsHandleCreated) ApplyShellTheme();
        else view.HandleCreated += (_, _) => ApplyShellTheme();
    }

    /// <summary>A menu that is not painted in system grey on a dark form.</summary>
    private sealed class DarkMenuColours : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuItemSelected => Color.FromArgb(60, 63, 70);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 63, 70);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 63, 70);
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }

    private static readonly ToolStripProfessionalRenderer DarkMenuRenderer =
        new(new DarkMenuColours()) { RoundedEdges = false };

    private static void PaintMenu(ToolStripDropDown menu, bool dark)
    {
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = dark ? DarkMenuRenderer : new ToolStripProfessionalRenderer();
        menu.BackColor = dark ? Surface : SystemColors.Control;
        menu.ForeColor = dark ? Text : SystemColors.ControlText;
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = dark ? Surface : SystemColors.Control;
            item.ForeColor = dark ? Text : SystemColors.ControlText;
        }
    }

    private const int LVM_GETHEADER = 0x1000 + 31;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string app, string idList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message,
                                             IntPtr wParam, IntPtr lParam);

    private static void Paint(Control control, bool dark)
    {
        // A context menu is not in the control tree, so the walk never reaches it.
        if (control.ContextMenuStrip is { } menu) PaintMenu(menu, dark);

        switch (control)
        {
            case TextBox box:
                box.BackColor = dark ? Field : SystemColors.Window;
                box.ForeColor = dark ? Text : SystemColors.WindowText;
                box.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                return;

            case ListBox list:
                list.BackColor = dark ? Field : SystemColors.Window;
                list.ForeColor = dark ? Text : SystemColors.WindowText;
                list.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                return;

            case ListView view:
                PaintListView(view, dark);
                return;

            case ComboBox combo:
                combo.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                combo.BackColor = dark ? Field : SystemColors.Window;
                combo.ForeColor = dark ? Text : SystemColors.WindowText;
                return;

            case NumericUpDown spin:
                spin.BackColor = dark ? Field : SystemColors.Window;
                spin.ForeColor = dark ? Text : SystemColors.WindowText;
                spin.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                return;

            case Button button:
                button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                button.BackColor = dark ? Surface : SystemColors.Control;
                button.ForeColor = dark ? Text : SystemColors.ControlText;
                button.UseVisualStyleBackColor = !dark;
                if (dark)
                {
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(56, 58, 64);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 72, 80);
                }
                return;

            case LinkLabel link:
                link.BackColor = Color.Transparent;
                link.LinkColor = dark ? Accent : SystemColors.HotTrack;
                link.ActiveLinkColor = dark ? Color.FromArgb(140, 175, 240)
                                            : SystemColors.HotTrack;
                link.VisitedLinkColor = link.LinkColor;
                return;

            case Label label:
                label.BackColor = Color.Transparent;
                // Grey labels stay grey, just legible against a dark surface.
                var wasDim = label.ForeColor == SystemColors.GrayText
                             || label.ForeColor == TextDim;
                label.ForeColor = dark ? (wasDim ? TextDim : Text)
                                       : (wasDim ? SystemColors.GrayText
                                                 : SystemColors.ControlText);
                return;

            case GroupBox group:
                group.BackColor = Color.Transparent;
                group.ForeColor = dark ? Text : SystemColors.ControlText;
                return;

            case CheckBox or RadioButton:
                control.BackColor = Color.Transparent;
                control.ForeColor = dark ? Text : SystemColors.ControlText;
                return;

            case Form form:
                form.BackColor = dark ? Background : SystemColors.Control;
                form.ForeColor = dark ? Text : SystemColors.ControlText;
                return;

            default:
                // CheckedListBox derives from ListBox in spirit but not in type.
                if (control is CheckedListBox checkedList)
                {
                    checkedList.BackColor = dark ? Field : SystemColors.Window;
                    checkedList.ForeColor = dark ? Text : SystemColors.WindowText;
                    checkedList.BorderStyle = dark ? BorderStyle.FixedSingle
                                                   : BorderStyle.Fixed3D;
                    return;
                }
                control.BackColor = dark ? Background : SystemColors.Control;
                control.ForeColor = dark ? Text : SystemColors.ControlText;
                return;
        }
    }

    // -- title bar --------------------------------------------------------

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
                                                    ref int value, int size);

    private static void ApplyTitleBar(Form form, bool dark)
    {
        if (!OperatingSystem.IsWindows() || !form.IsHandleCreated) return;
        var value = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode,
                                      ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20H1,
                                      ref value, sizeof(int));
        }
        catch { }
    }
}
