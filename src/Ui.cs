// Material-style UI for DPI Override (WinForms, owner-drawn)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

static class Theme
{
    public static float S = 1f;
    public static int Dp(float v) { return (int)Math.Round(v * S); }

    static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

    // Material 3 baseline (purple) palette, light or dark (SetDark)
    public static bool Dark;
    public static Color Primary, OnPrimary, PrimaryContainer, OnPrimaryContainer, SecondaryContainer, OnSecondaryContainer,
                        Surface, SurfaceContainer, SurfaceContainerHigh, Card, OnSurface, OnSurfaceVariant, Outline, OutlineVariant,
                        Divider, InverseSurface, InverseOnSurface, InversePrimary, Error, Running,
                        AppBar, OnAppBar, OnAppBarVariant,   // the purple title bar, which stays deep purple when dark
                        SnackError, Shadow;
    public static Color GoodBg, GoodFg, WarnBg, WarnFg, BadBg, BadFg, InfoBg, InfoFg;

    static Theme() { SetDark(false); }

    public static void SetDark(bool dark)
    {
        Dark = dark;
        if (!dark)
        {
            Primary = C(0x6750A4); OnPrimary = Color.White; PrimaryContainer = C(0xEADDFF); OnPrimaryContainer = C(0x21005D);
            SecondaryContainer = C(0xE8DEF8); OnSecondaryContainer = C(0x1D192B);
            Surface = C(0xFEF7FF); SurfaceContainer = C(0xF3EDF7); SurfaceContainerHigh = C(0xECE6F0); Card = Color.White;
            OnSurface = C(0x1D1B20); OnSurfaceVariant = C(0x49454F); Outline = C(0x79747E); OutlineVariant = C(0xCAC4D0);
            Divider = C(0xEFEAF3); InverseSurface = C(0x322F35); InverseOnSurface = C(0xF5EFF7); InversePrimary = C(0xD0BCFF);
            Error = C(0xB3261E); Running = C(0x2E7D32);
            AppBar = C(0x6750A4); OnAppBar = Color.White; OnAppBarVariant = C(0xF5EFF7);
            SnackError = C(0xF2B8B5); Shadow = C(0x3C2864);
            GoodBg = C(0xDCF3E2); GoodFg = C(0x1B5E20); WarnBg = C(0xFFE9C7); WarnFg = C(0x7A4100);
            BadBg = C(0xFFDAD6); BadFg = C(0x8C1D18); InfoBg = C(0xDCE7FA); InfoFg = C(0x0D3C8C);
        }
        else
        {
            Primary = C(0xD0BCFF); OnPrimary = C(0x381E72); PrimaryContainer = C(0x4F378B); OnPrimaryContainer = C(0xEADDFF);
            SecondaryContainer = C(0x4A4458); OnSecondaryContainer = C(0xE8DEF8);
            Surface = C(0x141218); SurfaceContainer = C(0x211F26); SurfaceContainerHigh = C(0x2B2930); Card = C(0x1D1B20);
            OnSurface = C(0xE6E0E9); OnSurfaceVariant = C(0xCAC4D0); Outline = C(0x938F99); OutlineVariant = C(0x49454F);
            Divider = C(0x2B2930); InverseSurface = C(0xE6E0E9); InverseOnSurface = C(0x322F35); InversePrimary = C(0x6750A4);
            Error = C(0xF2B8B5); Running = C(0x81C784);
            AppBar = C(0x4F378B); OnAppBar = C(0xEADDFF); OnAppBarVariant = C(0xCCC2DC);
            SnackError = C(0xB3261E); Shadow = Color.Black;
            GoodBg = C(0x1F3D26); GoodFg = C(0xA8DAB0); WarnBg = C(0x4A3000); WarnFg = C(0xFFCF8F);
            BadBg = C(0x601410); BadFg = C(0xFFB4AB); InfoBg = C(0x1B2F52); InfoFg = C(0xB3C8F5);
        }
    }

    // Windows' "Choose your app mode" (Settings > Personalization > Colors); ForceDark overrides it (tests, --theme)
    public static bool? ForceDark;

    public static bool SystemDark()
    {
        if (ForceDark.HasValue) return ForceDark.Value;
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                return v is int && (int)v == 0;
            }
        }
        catch { return false; }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

    // Dark or light window frame (title bar buttons, borders) for windows that don't color their caption themselves
    public static void Frame(IntPtr hwnd)
    {
        try { int d = Dark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref d, 4); } catch { }
    }

    // Title bar and border in the given colors (Windows 11; otherwise ignored). Overrides the accent-colored title
    // bar that Windows may use.
    public static void Caption(IntPtr hwnd, Color bg, Color fg)
    {
        try
        {
            int cap = bg.R | (bg.G << 8) | (bg.B << 16), txt = fg.R | (fg.G << 8) | (fg.B << 16);
            DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref cap, 4);
            DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref cap, 4);
            DwmSetWindowAttribute(hwnd, 36 /* DWMWA_TEXT_COLOR */, ref txt, 4);
        }
        catch { }
    }

    // Dark or light scrollbars on a list (DbListView keeps the theme from painting its own selection);
    // null goes back to the default look
    public static void Scrollbars(IntPtr hwnd)
    {
        try { SetWindowTheme(hwnd, Dark ? "DarkMode_Explorer" : null, null); } catch { }
    }

    public const string GFile = "\uE8A5", GFolder = "\uE8B7", GOpenFolder = "\uE838", GCopy = "\uE8C8", GRemove = "\uE711",
                        GMore = "\uE712", GSave = "\uE74E", GOpenFile = "\uE8E5", GUndo = "\uE7A7",
                        GPeople = "\uE716", GPerson = "\uE77B", GShield = "\uEA18";

    public static Font Body, BodyStrong, Small, SmallStrong, Title, Subtitle, Label, Icon, IconSmall, MenuIcon, CaptionIcon;
    public static bool HasIcons;

    // Icon glyphs (Segoe Fluent Icons / Segoe MDL2 Assets)
    public const string GSearch = "", GRefresh = "", GAdd = "", GCheck = "",
                        GClose = "", GUp = "", GDown = "", GError = "", GDone = "";

    public static void Init(float scale)
    {
        S = scale;
        var names = new HashSet<string>(new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        string ui = names.Contains("Segoe UI") ? "Segoe UI" : SystemFonts.MessageBoxFont.FontFamily.Name;
        string uiSemi = names.Contains("Segoe UI Semibold") ? "Segoe UI Semibold" : ui;
        FontStyle semiStyle = uiSemi == ui ? FontStyle.Bold : FontStyle.Regular;
        string icons = names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                     : names.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;

        Body = new Font(ui, 10f);
        BodyStrong = new Font(uiSemi, 10f, semiStyle);
        Small = new Font(ui, 8.75f);
        SmallStrong = new Font(uiSemi, 8.75f, semiStyle);
        Label = new Font(uiSemi, 9.5f, semiStyle);
        Title = new Font(uiSemi, 14f, semiStyle);
        Subtitle = new Font(ui, 9f);
        HasIcons = icons != null;
        Icon = new Font(icons ?? ui, 12f);
        IconSmall = new Font(icons ?? ui, 9f);
        MenuIcon = new Font(icons ?? ui, 13f);   // about 18 dp of glyph in the 24 dp icon box
        CaptionIcon = new Font(icons ?? ui, 7.5f);   // Windows' 10 px caption glyphs
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, float width, RectangleF r, float rad)
    {
        using (var p = Round(r, rad)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
    }

    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    public static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb(255,
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static Color Alpha(Color c, float a) { return Color.FromArgb((int)(255 * a), c.R, c.G, c.B); }

    // Chevron drawn as lines (works without the icon font): pointing right, or down when open
    public static void Chevron(Graphics g, Point c, bool down, Color color)
    {
        float h = Dp(4.5f);
        PointF[] pts = down
            ? new[] { new PointF(c.X - h, c.Y - h / 2), new PointF(c.X, c.Y + h / 2), new PointF(c.X + h, c.Y - h / 2) }
            : new[] { new PointF(c.X - h / 2, c.Y - h), new PointF(c.X + h / 2, c.Y), new PointF(c.X - h / 2, c.Y + h) };
        using (var pen = new Pen(color, Math.Max(1.5f, 1.6f * S)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(pen, pts);
    }

    // An icon-font glyph as a 24 dp bitmap (for menu items, Material's icon size); null without the icon font
    public static Bitmap GlyphImage(string glyph, Color c)
    {
        if (!HasIcons) return null;
        int s = Dp(24), big = s * 2;
        // Drawn on a larger canvas first, then the visible pixels are centered: the icon font's line box
        // doesn't sit centered on the glyph, which put menu icons a few pixels above their text
        using (var tmp = new Bitmap(big, big))
        {
            using (var g = Graphics.FromImage(tmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var br = new SolidBrush(c))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, MenuIcon, br, new RectangleF(0, 0, big, big), sf);
            }
            int x0 = big, y0 = big, x1 = -1, y1 = -1;
            for (int y = 0; y < big; y++)
                for (int x = 0; x < big; x++)
                    if (tmp.GetPixel(x, y).A > 24) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            var b = new Bitmap(s, s);
            if (x1 < 0) return b;
            using (var g = Graphics.FromImage(b))
            {
                var src = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
                g.DrawImage(tmp, new Rectangle((s - src.Width) / 2, (s - src.Height) / 2, src.Width, src.Height), src, GraphicsUnit.Pixel);
            }
            return b;
        }
    }

    const TextFormatFlags Base = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

    public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags extra = 0)
    {
        TextRenderer.DrawText(g, s, f, r, c, Base | TextFormatFlags.EndEllipsis | extra);
    }

    public static void Glyph(Graphics g, string glyph, Font f, Rectangle r, Color c)
    {
        if (!HasIcons) return;
        TextRenderer.DrawText(g, glyph, f, r, c, Base | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }

    public static int Measure(string s, Font f)
    {
        return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
    }

    // Tonal colors for a DPI mode chip
    public static void ModeColors(string mode, out Color bg, out Color fg)
    {
        switch (mode)
        {
            case "Application": bg = GoodBg; fg = GoodFg; break;
            case "System": bg = InfoBg; fg = InfoFg; break;
            case "System (Enhanced)": bg = WarnBg; fg = WarnFg; break;
            default: bg = Color.Empty; fg = Outline; break;
        }
    }
}

class DbPanel : Panel
{
    public DbPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}

class DbListView : ListView
{
    public DbListView() { DoubleBuffered = true; }

    [StructLayout(LayoutKind.Sequential)]
    struct NMHDR { public IntPtr hwndFrom; public UIntPtr idFrom; public int code; }

    // NMHDR stays a nested struct: on 64-bit it's padded to 24 bytes before dwDrawStage
    [StructLayout(LayoutKind.Sequential)]
    struct NMCUSTOMDRAW
    {
        public NMHDR hdr;
        public int dwDrawStage; public IntPtr hdc; public int left, top, right, bottom;
        public IntPtr dwItemSpec; public uint uItemState; public IntPtr lItemlParam;
    }

    // DrawItem paints the whole row, but in details view WinForms lets the list keep drawing after it (for
    // DrawSubItem), and a themed list (dark scrollbars need the Explorer theme) then paints its own translucent
    // selection over the row: a blue flash whenever a row is repainted. Once DrawItem has run, the list is told to
    // skip its own drawing of the item.
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x204E /* WM_REFLECT + WM_NOTIFY */ || !OwnerDraw) return;
        var cd = (NMCUSTOMDRAW)Marshal.PtrToStructure(m.LParam, typeof(NMCUSTOMDRAW));
        if (cd.hdr.code == -12 /* NM_CUSTOMDRAW */ && cd.dwDrawStage == 0x10001 /* CDDS_ITEMPREPAINT */)
            m.Result = (IntPtr)0x4 /* CDRF_SKIPDEFAULT */;
    }
}

// Base for owner-drawn interactive controls with hover/pressed state layers
class MControl : Control
{
    protected bool Hover, Pressed;
    public MControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected float StateAlpha { get { return !Enabled ? 0f : Pressed ? 0.12f : Hover ? 0.08f : 0f; } }
}

enum ButtonKind { Filled, Tonal, Text, Outlined }

class MButton : MControl
{
    public ButtonKind Kind = ButtonKind.Filled;
    public string Glyph;
    public Color? ContentOverride;
    public bool DropDown;   // shows a chevron after the text

    public int PreferredWidth()
    {
        int w = Theme.Measure(Text, Theme.Label) + Theme.Dp(48);
        if (Glyph != null && Theme.HasIcons) w += Theme.Dp(26) - Theme.Dp(8);
        if (DropDown) w += Theme.Dp(18);
        return w;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = r.Height / 2f;
        Color bg, fg;
        switch (Kind)
        {
            case ButtonKind.Filled: bg = Theme.Primary; fg = Theme.OnPrimary; break;
            case ButtonKind.Tonal: bg = Theme.SecondaryContainer; fg = Theme.OnSecondaryContainer; break;
            case ButtonKind.Outlined: bg = Color.Transparent; fg = Theme.OnSurface; break;   // matches the segmented buttons
            default: bg = Color.Transparent; fg = ContentOverride ?? Theme.Primary; break;
        }
        if (Kind == ButtonKind.Outlined)
            Theme.StrokeRound(g, Enabled ? Theme.Outline : Theme.Alpha(Theme.OnSurface, 0.12f), Math.Max(1f, Theme.S), r, rad);
        if (!Enabled)
        {
            bg = Kind == ButtonKind.Text || Kind == ButtonKind.Outlined ? Color.Transparent : Theme.Alpha(Theme.OnSurface, 0.12f);
            fg = Theme.Alpha(Theme.OnSurface, 0.38f);
            fg = Theme.Mix(Theme.SurfaceContainer, Theme.OnSurface, 0.38f);
        }
        if (bg.A > 0) Theme.FillRound(g, bg, r, rad);
        if (StateAlpha > 0) Theme.FillRound(g, Theme.Alpha(fg, StateAlpha), r, rad);

        bool glyph = Glyph != null && Theme.HasIcons;
        int tw = Theme.Measure(Text, Theme.Label);
        int total = tw + (glyph ? Theme.Dp(26) : 0) + (DropDown ? Theme.Dp(18) : 0);
        int x = (Width - total) / 2;
        if (glyph)
        {
            Theme.Glyph(g, Glyph, Theme.IconSmall, new Rectangle(x, 0, Theme.Dp(18), Height), fg);
            x += Theme.Dp(26);
        }
        TextRenderer.DrawText(g, Text, Theme.Label, new Rectangle(x, 0, tw + Theme.Dp(4), Height), fg,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (DropDown) Theme.Chevron(g, new Point(x + tw + Theme.Dp(12), Height / 2), true, fg);
    }
}

// Material 3 menu: rounded container with the system's soft shadow and no border, 8 dp padding at the top and
// bottom, 44 dp items with a 24 dp leading icon (12 dp in, 12 dp before the text), a full-width hover layer,
// dividers with 8 dp around them, and the current choice (Checked) filled with the secondary container color
class MMenu : ContextMenuStrip
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Layout in dp
    internal const int SidePad = 12, IconBox = 24, IconGap = 12, ItemHeight = 44, DividerPad = 8;
    internal static int TextX(bool icons) { return Theme.Dp(icons ? SidePad + IconBox + IconGap : SidePad + 4); }

    public MMenu()
    {
        Renderer = new MenuRenderer();
        DropShadowEnabled = true;
        Style();
    }

    // Menus are often created as fields, before Theme.Init knows the display scale, so the style is applied again
    // every time the menu opens
    // ToolStripDropDownMenu resets its Padding when it lays out (left = its image margin) and takes top and bottom
    // from here
    protected override Padding DefaultPadding { get { return new Padding(0, Theme.Dp(8), 0, Theme.Dp(8)); } }

    // Sizes are set here, before the menu is measured: it measures items itself (ignoring their padding), and
    // changing them while it opens is too late. Items get a fixed Material height, and the menu a minimum width
    // that fits the text at its Material position (TextX) plus the shortcut.
    void Sizes()
    {
        Font = Theme.Body;
        ShowImageMargin = Theme.HasIcons;
        ShowCheckMargin = false;
        ImageScalingSize = new Size(Theme.Dp(IconBox), Theme.Dp(IconBox));
        int width = Theme.Dp(112);
        foreach (ToolStripItem it in Items)
        {
            it.AutoSize = false;
            it.Padding = Padding.Empty;
            if (it is ToolStripSeparator) { it.Height = Theme.Dp(DividerPad * 2 + 1); continue; }
            it.Height = Theme.Dp(ItemHeight);
            if (!it.Available) continue;
            int w = TextX(ShowImageMargin) + TextRenderer.MeasureText(it.Text, Theme.Body).Width + Theme.Dp(SidePad);
            var mi = it as ToolStripMenuItem;
            if (mi != null && mi.ShowShortcutKeys && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString))
                w += Theme.Dp(32) + TextRenderer.MeasureText(mi.ShortcutKeyDisplayString, Theme.Body).Width;
            width = Math.Max(width, Math.Min(w, Theme.Dp(400)));
        }
        MinimumSize = new Size(width, 0);
        foreach (ToolStripItem it in Items) it.Width = width;
    }

    // Shown through these so the sizes fit the current texts (set just before showing)
    public new void Show(Control c, Point p, ToolStripDropDownDirection d) { Sizes(); base.Show(c, p, d); }
    public new void Show(Control c, Point p) { Sizes(); base.Show(c, p); }

    void Style()
    {
        BackColor = Theme.SurfaceContainer;
        // icons are bitmaps in the theme's color: redraw them after a switch between light and dark
        if (iconColor != Theme.OnSurfaceVariant)
        {
            iconColor = Theme.OnSurfaceVariant;
            foreach (ToolStripItem it in Items)
            {
                var glyph = it.Tag as string;
                if (glyph != null) it.Image = Theme.GlyphImage(glyph, iconColor);
            }
        }
    }

    Color iconColor;

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        Style();
        base.OnOpening(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int round = 2 /* DWMWCP_ROUND */;
            DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
            int none = unchecked((int)0xFFFFFFFE) /* DWMWA_COLOR_NONE */;
            DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref none, 4);   // elevation by shadow, not a line
        }
        catch { }
    }

    public ToolStripMenuItem Add(string text, string glyph, Action onClick)
    {
        var it = new ToolStripMenuItem(text) { Image = glyph != null ? Theme.GlyphImage(glyph, Theme.OnSurfaceVariant) : null, Tag = glyph };
        it.Font = Theme.Body;
        it.Click += (s, e) => onClick();
        Items.Add(it);
        Sizes();
        return it;
    }

    public ToolStripSeparator AddSeparator()
    {
        var sep = new ToolStripSeparator();
        Items.Add(sep);
        Sizes();
        return sep;
    }

    class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.SurfaceContainer); }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }   // shown by the fill instead

        static bool IsChecked(ToolStripItem it) { var mi = it as ToolStripMenuItem; return mi != null && mi.Checked; }

        // In a 24 dp box 12 dp from the edge, centered on the text (1 dp lower: Segoe UI's capitals sit about
        // 0.06 em below the center of the line box the text is centered by)
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            var img = e.Image;
            if (img == null) return;
            int box = Theme.Dp(IconBox);
            var dst = new Rectangle(Theme.Dp(SidePad) + (box - img.Width) / 2, (e.Item.Height - img.Height) / 2 + Theme.Dp(1), img.Width, img.Height);
            if (e.Item.Enabled) e.Graphics.DrawImage(img, dst);
            else ControlPaint.DrawImageDisabled(e.Graphics, img, dst.X, dst.Y, Theme.SurfaceContainer);
        }

        // Full width, as in Material menus: the current choice filled, hover as an 8 % state layer on top
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var r = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            if (IsChecked(e.Item)) using (var b = new SolidBrush(Theme.SecondaryContainer)) e.Graphics.FillRectangle(b, r);
            if (e.Item.Selected && e.Item.Enabled) using (var b = new SolidBrush(Theme.Alpha(Theme.OnSurface, 0.08f))) e.Graphics.FillRectangle(b, r);
        }

        // Drawn here: Material spacing instead of WinForms' text position, and centered with the icon
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            if (e.Text != e.Item.Text)   // the shortcut ("Ctrl+Z"): right-aligned, muted
            {
                var sr = new Rectangle(0, 0, e.Item.Width - Theme.Dp(SidePad), e.Item.Height);
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, sr, Theme.OnSurfaceVariant, flags | TextFormatFlags.Right);
                return;
            }
            Color color = !e.Item.Enabled ? Theme.Mix(Theme.SurfaceContainer, Theme.OnSurface, 0.38f)
                        : IsChecked(e.Item) ? Theme.OnSecondaryContainer : Theme.OnSurface;
            var owner = e.Item.Owner as ToolStripDropDownMenu;
            int x = TextX(owner == null || owner.ShowImageMargin);
            var r = new Rectangle(x, 0, e.Item.Width - x - Theme.Dp(SidePad), e.Item.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, color, flags | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        // Full width with 8 dp above and below
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Theme.OutlineVariant)) e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
        }
    }
}

class IconButton : MControl
{
    public string Glyph;
    public Func<Color> ForeOf = () => Theme.OnSurfaceVariant;   // read when painting, so it follows light/dark
    Color Fore { get { return ForeOf(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        if (StateAlpha > 0) Theme.FillRound(g, Theme.Alpha(Fore, StateAlpha * 1.5f), r, r.Height / 2f);
        if (Theme.HasIcons) Theme.Glyph(g, Glyph, Theme.Icon, ClientRectangle, Fore);
        else Theme.Text(g, Text, Theme.Label, ClientRectangle, Fore, TextFormatFlags.HorizontalCenter);
    }
}

class Chip : MControl
{
    bool isChecked;
    public event EventHandler CheckedChanged;
    public bool Checked
    {
        get { return isChecked; }
        set { if (isChecked == value) return; isChecked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
    }

    public int PreferredWidth()
    {
        return Theme.Measure(Text, Theme.Label) + Theme.Dp(40);
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = Theme.Dp(8);
        Color fg = isChecked ? Theme.OnSecondaryContainer : Theme.OnSurfaceVariant;
        if (isChecked) Theme.FillRound(g, Theme.SecondaryContainer, r, rad);
        else Theme.StrokeRound(g, Theme.OutlineVariant, Math.Max(1f, Theme.S), r, rad);
        if (StateAlpha > 0) Theme.FillRound(g, Theme.Alpha(fg, StateAlpha), r, rad);

        // No checkmark: the filled background already shows it's on, and the text stays put when it's toggled
        TextRenderer.DrawText(g, Text, Theme.Label, ClientRectangle, fg,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }
}

class Segmented : MControl
{
    public string[] Items = new string[0];
    int selected, hoverIdx = -1;
    public event EventHandler SelectedChanged;
    public int Selected
    {
        get { return selected; }
        set { if (selected == value) return; selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
    }

    int SegWidth(int i)
    {
        return Theme.Measure(Items[i], Theme.Label) + Theme.Dp(44);
    }

    public int PreferredWidth() { int w = 0; for (int i = 0; i < Items.Length; i++) w += SegWidth(i); return w; }

    int[] Edges()
    {
        // scale segment widths to the control width
        int total = PreferredWidth();
        var edges = new int[Items.Length + 1];
        float acc = 0;
        for (int i = 0; i < Items.Length; i++) { edges[i] = (int)acc; acc += SegWidth(i) * (Width - 1f) / total; }
        edges[Items.Length] = Width - 1;
        return edges;
    }

    int IndexAt(int x)
    {
        var e = Edges();
        for (int i = 0; i < Items.Length; i++) if (x >= e[i] && x < e[i + 1]) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.X); if (i != hoverIdx) { hoverIdx = i; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hoverIdx = -1; base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { int i = IndexAt(e.X); if (i >= 0) Selected = i; base.OnMouseClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = r.Height / 2f;
        var edges = Edges();
        using (var outer = Theme.Round(r, rad))
        {
            var oldClip = g.Clip;
            g.SetClip(outer, CombineMode.Intersect);
            for (int i = 0; i < Items.Length; i++)
            {
                var seg = new RectangleF(edges[i], 0, edges[i + 1] - edges[i], Height);
                if (i == selected) using (var b = new SolidBrush(Theme.SecondaryContainer)) g.FillRectangle(b, seg);
                if (i == hoverIdx)
                {
                    float a = Pressed ? 0.12f : 0.08f;
                    using (var b = new SolidBrush(Theme.Alpha(Theme.OnSurface, a))) g.FillRectangle(b, seg);
                }
            }
            g.Clip = oldClip;
            using (var pen = new Pen(Theme.Outline, Math.Max(1f, Theme.S)))
            {
                g.DrawPath(pen, outer);
                for (int i = 1; i < Items.Length; i++) g.DrawLine(pen, edges[i], 0, edges[i], Height);
            }
        }
        for (int i = 0; i < Items.Length; i++)
        {
            bool sel = i == selected;
            Color fg = sel ? Theme.OnSecondaryContainer : Theme.OnSurface;
            // No checkmark: the filled segment shows the choice, and the labels stay put when it changes
            TextRenderer.DrawText(g, Items[i], Theme.Label, new Rectangle(edges[i], 0, edges[i + 1] - edges[i], Height), fg,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}

class SearchBox : Control
{
    public readonly TextBox Box = new TextBox();
    readonly IconButton clear = new IconButton();
    bool focused;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

    public SearchBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.IBeam;
        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = Theme.SurfaceContainerHigh;
        Box.ForeColor = Theme.OnSurface;
        Box.Font = Theme.Body;
        Box.GotFocus += (s, e) => { focused = true; Invalidate(); };
        Box.LostFocus += (s, e) => { focused = false; Invalidate(); };
        Box.TextChanged += (s, e) => { clear.Visible = Box.Text.Length > 0; };
        Box.HandleCreated += (s, e) => SetCue();
        clear.Glyph = Theme.GClose; clear.Text = "x"; clear.Visible = false;
        clear.Click += (s, e) => { Box.Text = ""; Box.Focus(); };
        Controls.Add(Box);
        Controls.Add(clear);
    }

    public void ApplyTheme()
    {
        Box.BackColor = Theme.SurfaceContainerHigh;
        Box.ForeColor = Theme.OnSurface;
        Invalidate(true);
    }

    void SetCue()
    {
        try { SendMessage(Box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "Search programs"); } catch { }
    }

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int left = Theme.Dp(44), right = Theme.Dp(40);
        Box.Width = Math.Max(10, Width - left - right);
        Box.Location = new Point(left, (Height - Box.Height) / 2 + 1);
        int cb = Theme.Dp(32);
        clear.Bounds = new Rectangle(Width - cb - Theme.Dp(4), (Height - cb) / 2, cb, cb);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Theme.SurfaceContainerHigh, r, r.Height / 2f);
        if (focused) Theme.StrokeRound(g, Theme.Primary, Math.Max(2f, 2f * Theme.S), new RectangleF(1, 1, Width - 3, Height - 3), (Height - 3) / 2f);
        Theme.Glyph(g, Theme.GSearch, Theme.IconSmall, new Rectangle(Theme.Dp(14), 0, Theme.Dp(20), Height), Theme.OnSurfaceVariant);
    }
}

// The app's top bar, which on Windows is also the window's title bar (MainForm extends its content into the title
// bar area): logo and title, the app's actions (child controls) and Material window buttons at the top right. Mouse
// hits on it go to the window (HTTRANSPARENT), which tells Windows what's there: the buttons, or a place to drag.
class AppBar : Control
{
    public Image Logo;
    public bool WindowButtons;           // drawn here because the window has no title bar of its own
    public bool Maximized, Active = true;
    public int Hot, Pressed;             // hit-test code of the window button under the mouse / held down, or 0
    public const int HTMINBUTTON = 8, HTMAXBUTTON = 9, HTCLOSE = 20;

    public AppBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.AppBar;
    }

    // Windows' caption button size
    public int ButtonsWidth { get { return WindowButtons ? 3 * Theme.Dp(46) : 0; } }

    public Rectangle ButtonRect(int ht)
    {
        int w = Theme.Dp(46), i = ht == HTCLOSE ? 0 : ht == HTMAXBUTTON ? 1 : 2;
        return new Rectangle(Width - (i + 1) * w, 0, w, Theme.Dp(32));
    }

    public int ButtonAt(Point p)
    {
        if (!WindowButtons) return 0;
        foreach (int ht in new[] { HTCLOSE, HTMAXBUTTON, HTMINBUTTON }) if (ButtonRect(ht).Contains(p)) return ht;
        return 0;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084 /* WM_NCHITTEST */ && WindowButtons) { m.Result = (IntPtr)(-1) /* HTTRANSPARENT */; return; }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        g.Clear(Theme.AppBar);
        int x = Theme.Dp(24);
        if (Logo != null)
        {
            int s = Theme.Dp(36);
            g.DrawImage(Logo, new Rectangle(x, (Height - s) / 2, s, s));
            x += s + Theme.Dp(14);
        }
        int mid = Height / 2, tw = Width - x - Theme.Dp(24) - ButtonsWidth;
        Theme.Text(g, "DPI Override", Theme.Title, new Rectangle(x, mid - Theme.Dp(24), tw, Theme.Dp(28)), Theme.OnAppBar);
        Theme.Text(g, "High-DPI scaling override for any program", Theme.Subtitle,
            new Rectangle(x, mid + Theme.Dp(2), tw, Theme.Dp(20)), Theme.OnAppBarVariant);
        if (WindowButtons) foreach (int ht in new[] { HTMINBUTTON, HTMAXBUTTON, HTCLOSE }) DrawButton(g, ht);
    }

    // Material state layers on the bar's color; close turns red like everywhere in Windows
    void DrawButton(Graphics g, int ht)
    {
        var r = ButtonRect(ht);
        bool hot = Hot == ht, pressed = Pressed == ht && hot;
        Color fg = Active ? Theme.OnAppBar : Theme.Mix(Theme.AppBar, Theme.OnAppBar, 0.55f);
        if (ht == HTCLOSE && (hot || pressed))
        {
            using (var b = new SolidBrush(pressed ? Color.FromArgb(255, 0xA3, 0x24, 0x18) : Color.FromArgb(255, 0xC4, 0x2B, 0x1C))) g.FillRectangle(b, r);
            fg = Color.White;
        }
        else if (hot || pressed) using (var b = new SolidBrush(Theme.Alpha(Theme.OnAppBar, pressed ? 0.18f : 0.10f))) g.FillRectangle(b, r);
        if (Theme.HasIcons)
        {
            string glyph = ht == HTCLOSE ? "" : ht == HTMINBUTTON ? "" : Maximized ? "" : "";
            Theme.Glyph(g, glyph, Theme.CaptionIcon, r, fg);
        }
        else
        {
            int s = Theme.Dp(5), cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var pen = new Pen(fg, Math.Max(1f, Theme.S)))
            {
                if (ht == HTMINBUTTON) g.DrawLine(pen, cx - s, cy, cx + s, cy);
                else if (ht == HTMAXBUTTON) g.DrawRectangle(pen, cx - s, cy - s, 2 * s, 2 * s);
                else { g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s); g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s); }
            }
        }
    }
}

class Snackbar : Control
{
    readonly Timer timer = new Timer();
    bool error;
    // Up to two text buttons on the right, e.g. UNDO and RESTART
    string[] actionTexts = new string[0];
    Action[] actions = new Action[0];
    Rectangle[] actionRects = new Rectangle[0];
    int hover = -1;

    public Snackbar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Visible = false;
        timer.Tick += (s, e) => { timer.Stop(); Visible = false; };
        Cursor = Cursors.Hand;
    }

    int HitAction(Point p)
    {
        for (int i = 0; i < actionRects.Length; i++) if (actionRects[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitAction(e.Location);
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int hit = HitAction(e.Location);
        var a = hit >= 0 ? actions[hit] : null;
        Visible = false; timer.Stop();
        if (a != null) a();
        base.OnMouseClick(e);
    }

    public void Show(string text, bool isError, Rectangle area, string actionLabel = null, Action onAction = null,
                     string action2Label = null, Action onAction2 = null)
    {
        Text = text; error = isError; hover = -1;
        var labels = new List<string>(); var acts = new List<Action>();
        if (actionLabel != null) { labels.Add(actionLabel); acts.Add(onAction); }
        if (action2Label != null) { labels.Add(action2Label); acts.Add(onAction2); }
        actionTexts = labels.ToArray(); actions = acts.ToArray();
        var widths = actionTexts.Select(t => Theme.Measure(t, Theme.Label) + Theme.Dp(32)).ToArray();
        int actionsW = widths.Sum();
        int w = Math.Min(area.Width - Theme.Dp(32), Theme.Measure(text, Theme.Body) + Theme.Dp(72) + actionsW);
        int h = Theme.Dp(48);
        Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Bottom - h - Theme.Dp(16), w, h);
        actionRects = new Rectangle[widths.Length];
        int x = w - Theme.Dp(4) - actionsW;
        for (int i = 0; i < widths.Length; i++) { actionRects[i] = new Rectangle(x, Theme.Dp(6), widths[i], h - Theme.Dp(12)); x += widths[i]; }
        using (var p = Theme.Round(new RectangleF(0, 0, w, h), Theme.Dp(6))) Region = new Region(p);
        Visible = true;
        BringToFront();
        Invalidate();
        timer.Stop();
        timer.Interval = actionTexts.Length > 0 ? 9000 : 4500;
        timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        g.Clear(Theme.InverseSurface);
        Color ic = error ? Theme.SnackError : Theme.InversePrimary;
        Theme.Glyph(g, error ? Theme.GError : Theme.GDone, Theme.Icon, new Rectangle(Theme.Dp(14), 0, Theme.Dp(24), Height), ic);
        int x = Theme.HasIcons ? Theme.Dp(48) : Theme.Dp(16);
        int right = actionRects.Length > 0 ? actionRects[0].Left : Width - Theme.Dp(12);
        Theme.Text(g, Text, Theme.Body, new Rectangle(x, 0, right - x, Height), Theme.InverseOnSurface);
        for (int i = 0; i < actionRects.Length; i++)
        {
            if (hover == i) Theme.FillRound(g, Theme.Alpha(Theme.InversePrimary, 0.12f), actionRects[i], actionRects[i].Height / 2f);
            TextRenderer.DrawText(g, actionTexts[i], Theme.Label, actionRects[i], Theme.InversePrimary,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }
}

// Material-style confirmation dialog
// Material dialog: no window title bar, rounded corners with a shadow, the raised surface color. Esc, Enter and the
// buttons close it; it can be moved by dragging it anywhere.
class ConfirmDialog : Form
{
    readonly string title, body;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    ConfirmDialog(string title, string body, string okText)
    {
        this.title = title; this.body = body;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.SurfaceContainerHigh;
        Font = Theme.Body;
        Text = "DPI Override";   // not shown; for the accessibility tools
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        HandleCreated += (s, e) =>
        {
            try
            {
                int round = 2 /* DWMWCP_ROUND */;
                DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
                int none = unchecked((int)0xFFFFFFFE) /* DWMWA_COLOR_NONE */;
                DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref none, 4);
            }
            catch { }
        };

        int w = Theme.Dp(440);
        int textW = w - Theme.Dp(48);
        int bodyH = TextRenderer.MeasureText(body, Theme.Body, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        ClientSize = new Size(w, Theme.Dp(24 + 32 + 16) + bodyH + Theme.Dp(24 + 40 + 20));

        var ok = new MButton { Kind = ButtonKind.Filled, Text = okText };
        var cancel = new MButton { Kind = ButtonKind.Text, Text = "Cancel" };
        ok.Size = new Size(ok.PreferredWidth(), Theme.Dp(40));
        cancel.Size = new Size(cancel.PreferredWidth(), Theme.Dp(40));
        ok.Location = new Point(ClientSize.Width - Theme.Dp(24) - ok.Width, ClientSize.Height - Theme.Dp(20) - ok.Height);
        cancel.Location = new Point(ok.Left - Theme.Dp(8) - cancel.Width, ok.Top);
        ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(ok); Controls.Add(cancel);
        KeyPreview = true;
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
            else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
    }

    // A soft shadow around the borderless window (Windows 11 rounds it with the corners)
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000 /* CS_DROPSHADOW */; return cp; }
    }

    // Moved by dragging it anywhere, as there's no title bar
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.SurfaceContainerHigh);
        int x = Theme.Dp(24), w = ClientSize.Width - Theme.Dp(48);
        Theme.Text(g, title, Theme.Title, new Rectangle(x, Theme.Dp(22), w, Theme.Dp(34)), Theme.OnSurface);
        TextRenderer.DrawText(g, body, Theme.Body, new Rectangle(x, Theme.Dp(72), w, ClientSize.Height - Theme.Dp(72 + 80)),
            Theme.OnSurfaceVariant, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    public static bool Ask(IWin32Window owner, string title, string body, string okText)
    {
        using (var d = new ConfirmDialog(title, body, okText)) return d.ShowDialog(owner) == DialogResult.OK;
    }
}

// A list row: one program, a group header, or a program inside an open group
class Row
{
    public Entry Entry;     // null for a group header
    public Group Group;     // the group this row heads or belongs to
    public Row Parent;      // the group header of a program inside an open group
    public ListViewItem Item;
    public bool IsGroup { get { return Entry == null; } }
    public bool IsChild { get { return Parent != null; } }
}

class MainForm : Form
{
    readonly AppBar appBar = new AppBar();
    readonly IconButton btnRefresh = new IconButton();
    readonly IconButton btnMore = new IconButton();
    readonly MMenu moreMenu = new MMenu();
    readonly DbPanel toolbar = new DbPanel();
    readonly SearchBox search = new SearchBox();
    readonly Chip chipHide = new Chip();
    readonly Chip chipOverride = new Chip();
    readonly Chip chipBlurry = new Chip();
    readonly MButton btnRestart = new MButton();
    readonly MButton btnAdd = new MButton();
    readonly DbPanel content = new DbPanel();
    readonly DbPanel header = new DbPanel();
    readonly DbListView list = new DbListView();
    readonly Label empty = new Label();
    readonly DbPanel bottom = new DbPanel();
    readonly Segmented modes = new Segmented();
    readonly MButton btnApply = new MButton();
    readonly Snackbar snack = new Snackbar();

    readonly HashSet<string> extra = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> addedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // used as groups as they are
    readonly HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);       // roots of open groups
    readonly MMenu addMenu = new MMenu();
    ToolStripMenuItem addHere;
    ToolStripSeparator addHereSep;
    string addHereDir;
    readonly MMenu rowMenu = new MMenu();
    readonly MButton btnScope = new MButton();
    readonly MMenu scopeMenu = new MMenu();
    ToolStripMenuItem scopeAll, scopeMe;
    readonly ToolTip applyTip = new ToolTip();
    bool allUsersScope = true;   // Apply sets the override for all users (asks for admin rights) or only for the current user
    ToolStripMenuItem rowOpen, rowCopy, rowRestart, rowRemove;
    readonly Dictionary<string, string> descCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    readonly string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(new[] { '\\' }) + "\\";
    List<Entry> entries = new List<Entry>();
    int shownCount;
    int sortCol = 0; bool sortAsc = true;
    int hoverIndex = -1;
    const int Gutter = 30;      // room for the expand chevron left of the icon
    const int ChildIndent = 30;
    const int MaxFolderScan = 2000;

    static readonly string[] ColNames = { "PROGRAM", "ALL USERS", "CURRENT USER", "RENDERING", "LOCATION" };
    List<int> monitorDpis = new List<int>();
    bool restarting;
    static readonly string[] ModeKeys = { "Application", "System", "System (Enhanced)", "Not set" };

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static int Dp(float v) { return Theme.Dp(v); }

    public MainForm()
    {
        float scale;
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        Theme.Init(scale);
        Theme.SetDark(Theme.SystemDark());

        current = this;
        Text = SingleInstance.WindowTitle;   // a second launch finds this window by its title
        Font = Theme.Body;
        Size = new Size(Dp(1180), Dp(720));
        MinimumSize = new Size(Dp(900), Dp(480));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        try
        {
            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
                if (s != null)
                {
                    Icon = new Icon(s);
                }
        }
        catch { }
        try
        {
            using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("logo.png"))
                if (s != null) appBar.Logo = new Bitmap(s);
        }
        catch { }

        // App bar
        appBar.Dock = DockStyle.Top;
        appBar.Height = Dp(64);   // Material's top app bar; the app's commands are in the toolbar below
        appBar.WindowButtons = CustomTitleBar;
        Activated += (s, e) => { appBar.Active = true; appBar.Invalidate(); };
        Deactivate += (s, e) => { appBar.Active = false; appBar.Invalidate(); };
        SizeChanged += (s, e) => { appBar.Maximized = WindowState == FormWindowState.Maximized; appBar.Invalidate(); };
        btnRefresh.Glyph = Theme.GRefresh; btnRefresh.Text = "R";
        btnRefresh.Click += (s, e) => Reload();
        btnMore.Glyph = Theme.GMore; btnMore.Text = "…";
        var undoItem = moreMenu.Add("Undo", Theme.GUndo, () => Undo());
        undoItem.ShortcutKeyDisplayString = "Ctrl+Z";   // display only: OnKeyDown handles the key
        undoItem.ShowShortcutKeys = true;
        moreMenu.AddSeparator();
        moreMenu.Add("Back up overrides…", Theme.GSave, BackUp);
        moreMenu.Add("Restore from backup…", Theme.GOpenFile, Restore);
        var explorerSep = moreMenu.AddSeparator();
        var explorerItem = moreMenu.Add("", Theme.GOpenFolder, ToggleExplorerMenu);
        btnMore.Click += (s, e) =>
        {
            // before Show, so the menu is sized and placed for the new text
            undoItem.Enabled = undo.Count > 0;
            undoItem.Text = undo.Count > 0 ? "Undo: " + Shorten(undo[undo.Count - 1].Label, 48) : "Nothing to undo";
            bool onWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            explorerItem.Visible = explorerSep.Visible = onWindows;
            string label = "“" + ExplorerMenu.Label + "”";
            if (onWindows) explorerItem.Text = OnlyCompactMenuMissing() ? "Add " + label + " to Windows 11's menu"
                                             : ExplorerEntryOn() ? "Remove " + label + " from Explorer" : "Add " + label + " to Explorer";
            moreMenu.Show(btnMore, new Point(btnMore.Width, btnMore.Height + Dp(4)), ToolStripDropDownDirection.BelowLeft);
        };
        new ToolTip().SetToolTip(btnRefresh, "Refresh (F5)");
        new ToolTip().SetToolTip(btnMore, "Undo, back up and restore, Explorer menu");

        // Toolbar
        toolbar.Dock = DockStyle.Top;
        toolbar.Height = Dp(68);
        search.Box.TextChanged += (s, e) => FillList();
        chipHide.Text = "Hide Windows processes"; chipHide.Checked = true;
        chipHide.CheckedChanged += (s, e) => FillList();
        chipOverride.Text = "Only with override";
        chipOverride.CheckedChanged += (s, e) => FillList();
        chipBlurry.Text = "Possibly blurry";
        chipBlurry.CheckedChanged += (s, e) => FillList();
        btnAdd.Kind = ButtonKind.Tonal; btnAdd.Text = "Add"; btnAdd.Glyph = Theme.GAdd; btnAdd.DropDown = true;
        btnAdd.Click += (s, e) => ShowAddMenu();
        addMenu.Add("Program (.exe)…", Theme.GFile, Browse);
        addMenu.Add("All programs in a folder…", Theme.GFolder, BrowseFolder);
        addHereSep = addMenu.AddSeparator();
        addHere = addMenu.Add("", Theme.GFolder, () => AddPaths(new[] { addHereDir }));
        new ToolTip().SetToolTip(btnAdd, "Add a program that isn't running, or every program in a folder. You can also drop programs, shortcuts and folders onto the window.");
        toolbar.Controls.AddRange(new Control[] { search, chipHide, chipOverride, chipBlurry, btnRefresh, btnAdd, btnMore });
        toolbar.Resize += (s, e) => LayoutToolbar();
        toolbar.Paint += PaintToolbar;

        // Content card: header + list
        content.Dock = DockStyle.Fill;
        content.Padding = new Padding(Dp(28), Dp(10), Dp(28), Dp(20));
        content.Paint += PaintCard;
        content.Resize += (s, e) => { content.Invalidate(); PlaceEmpty(); };

        header.Dock = DockStyle.Top;
        header.Height = Dp(40);
        header.Paint += PaintHeader;
        header.MouseClick += HeaderClick;
        header.Cursor = Cursors.Hand;

        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.HeaderStyle = ColumnHeaderStyle.None;
        list.BorderStyle = BorderStyle.None;
        list.FullRowSelect = true;
        list.MultiSelect = true;
        list.HideSelection = false;
        list.OwnerDraw = true;
        list.ShowItemToolTips = false;   // the rows show everything; tooltips only repeated it
        list.HandleCreated += (s, e) => Theme.Scrollbars(list.Handle);
        list.SmallImageList = new ImageList { ImageSize = new Size(1, Dp(56)) }; // row height
        for (int i = 0; i < ColNames.Length; i++) list.Columns.Add(ColNames[i], 100);
        list.DrawItem += DrawRow;
        list.DrawSubItem += (s, e) => { e.DrawDefault = false; };
        list.DrawColumnHeader += (s, e) => { e.DrawDefault = true; };
        list.Resize += (s, e) => LayoutColumns();
        list.ClientSizeChanged += (s, e) => LayoutColumns();   // the vertical scrollbar came or went
        list.MouseMove += ListMouseMove;
        list.MouseLeave += (s, e) => SetHover(-1);
        list.SelectedIndexChanged += (s, e) => { UpdateSelectionUi(); list.Invalidate(); };
        list.MouseDown += ListMouseDown;
        list.MouseDoubleClick += ListDoubleClick;
        list.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right && list.GetItemAt(e.X, e.Y) != null) ShowRowMenu(e.Location); };
        list.KeyDown += ListKeyDown;
        rowOpen = rowMenu.Add("Open file location", Theme.GOpenFolder, OpenLocation);
        rowCopy = rowMenu.Add("Copy path", Theme.GCopy, CopyPaths);
        rowMenu.AddSeparator();
        rowRestart = rowMenu.Add("Restart", Theme.GRefresh, RestartSelected);
        rowRemove = rowMenu.Add("Remove override", Theme.GRemove, RemoveOverride);

        empty.Text = "No programs match your search or filters";
        empty.Font = Theme.Body;
        empty.AutoSize = true;
        empty.Visible = false;

        content.Controls.Add(list);
        content.Controls.Add(header);
        content.Controls.Add(empty);
        empty.BringToFront();

        // Bottom bar
        bottom.Dock = DockStyle.Bottom;
        bottom.Height = Dp(84);
        bottom.Paint += PaintBottom;
        modes.Items = new[] { "Application", "System", "System (Enhanced)", "Off" };
        modes.Selected = 0;
        modes.SelectedChanged += (s, e) => bottom.Invalidate();
        btnApply.Kind = ButtonKind.Filled; btnApply.Text = "Apply to selected"; btnApply.Glyph = Theme.GCheck;
        btnApply.Click += (s, e) => Apply();
        btnApply.Enabled = false;
        btnRestart.Kind = ButtonKind.Tonal; btnRestart.Text = "Restart"; btnRestart.Glyph = Theme.GRefresh;
        btnRestart.Enabled = false;
        btnRestart.Click += (s, e) => RestartSelected();
        new ToolTip().SetToolTip(btnRestart, "Close and reopen the selected running programs so a new setting takes effect");
        btnScope.Kind = ButtonKind.Outlined; btnScope.DropDown = true;
        btnScope.Click += (s, e) => scopeMenu.Show(btnScope, new Point(0, -Dp(4)), ToolStripDropDownDirection.AboveRight);
        scopeAll = scopeMenu.Add("For all users (asks for administrator rights)", Theme.GPeople, () => SetScope(true));
        scopeMe = scopeMenu.Add("Only for me (no administrator rights needed)", Theme.GPerson, () => SetScope(false));
        new ToolTip().SetToolTip(btnScope, "Who the setting is for");
        allUsersScope = LoadScope();
        bottom.Controls.AddRange(new Control[] { btnScope, modes, btnRestart, btnApply });
        UpdateScopeUi();
        bottom.Resize += (s, e) => LayoutBottom();

        Controls.Add(content);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(appBar);
        Controls.Add(snack);

        ApplyColors();
        LayoutToolbar(); LayoutBottom();
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) { EnableOleDrop(this); EnableOleDrop(list); }
        Activated += (s, e) => FollowWindowsTheme();   // also catches a switch the window wasn't told about
        Shown += (s, e) =>
        {
            Reload();
            list.Focus();
            if (Program.StartPaths.Count > 0) AddPaths(Program.StartPaths);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // recalculate the frame now that WM_NCCALCSIZE gives the title bar area to the app bar
        if (CustomTitleBar) SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020 /* NOSIZE NOMOVE NOZORDER FRAMECHANGED */);
        ApplyFrame();
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            try { FileDrop.Enable(Handle); } catch (Exception ex) { Program.Log("drop setup failed: " + ex.Message); }
            MessageFilter.Allow(Handle, WM_SETTINGCHANGE);   // Windows' theme switch, sent by normal-user processes
        }
    }

    // ---------- Light and dark ----------
    const int WM_SETTINGCHANGE = 0x001A;

    // The title bar in the app bar's color
    void ApplyFrame()
    {
        Theme.Frame(Handle);
        Theme.Caption(Handle, Theme.AppBar, Theme.OnAppBar);
    }

    // Colors that controls keep (the rest is read from Theme when painting)
    void ApplyColors()
    {
        BackColor = Theme.Surface;
        appBar.BackColor = Theme.AppBar;
        toolbar.BackColor = Theme.Surface;
        content.BackColor = Theme.Surface;
        header.BackColor = Theme.Card;
        list.BackColor = Theme.Card;
        empty.ForeColor = Theme.OnSurfaceVariant;
        empty.BackColor = Theme.Card;
        bottom.BackColor = Theme.SurfaceContainer;
        search.ApplyTheme();
        if (IsHandleCreated) ApplyFrame();
        if (list.IsHandleCreated) Theme.Scrollbars(list.Handle);
        Invalidate(true);
    }

    // Switches between light and dark when Windows' app mode changed
    void FollowWindowsTheme()
    {
        bool dark = Theme.SystemDark();
        if (dark == Theme.Dark) return;
        Theme.SetDark(dark);
        Program.Log("switched to " + (dark ? "dark" : "light") + " mode");
        ApplyColors();
    }

    // WinForms rewrites the window's extended style from CreateParams (e.g. when it's shown), which would drop the
    // WS_EX_ACCEPTFILES that DragAcceptFiles sets, so it's part of CreateParams
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) cp.ExStyle |= 0x10 /* WS_EX_ACCEPTFILES */;
            return cp;
        }
    }

    // ---------- The app bar as the title bar ----------
    // The window keeps its normal frame (resize edges, shadow, rounded corners, snapping), but the title bar area belongs
    // to the client: WM_NCCALCSIZE gives it back, and WM_NCHITTEST tells Windows where the window buttons, the place to
    // drag and the top resize edge are. Returning HTMAXBUTTON also brings Windows 11's Snap Layouts to our button.
    static bool CustomTitleBar { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

    const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1,
              WM_NCLBUTTONUP = 0x00A2, WM_NCLBUTTONDBLCLK = 0x00A3, WM_NCRBUTTONUP = 0x00A5, WM_NCMOUSELEAVE = 0x02A2;

    [DllImport("user32.dll")]
    static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);
    [DllImport("user32.dll")]
    static extern bool EnableMenuItem(IntPtr menu, uint id, uint flags);
    [DllImport("user32.dll")]
    static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // Windows' window menu (Restore, Move, Size, Minimize, Maximize, Close), with the items that apply now
    void ShowSystemMenu(Point screen)
    {
        IntPtr menu = GetSystemMenu(Handle, false);
        if (menu == IntPtr.Zero) return;
        bool max = IsZoomed(Handle);
        const uint ENABLED = 0, GRAYED = 1;
        EnableMenuItem(menu, 0xF120 /* SC_RESTORE */, max ? ENABLED : GRAYED);
        EnableMenuItem(menu, 0xF010 /* SC_MOVE */, max ? GRAYED : ENABLED);
        EnableMenuItem(menu, 0xF000 /* SC_SIZE */, max ? GRAYED : ENABLED);
        EnableMenuItem(menu, 0xF020 /* SC_MINIMIZE */, ENABLED);
        EnableMenuItem(menu, 0xF030 /* SC_MAXIMIZE */, max ? GRAYED : ENABLED);
        int cmd = TrackPopupMenu(menu, 0x0100 /* TPM_RETURNCMD */ | 0x0002 /* TPM_RIGHTBUTTON */, screen.X, screen.Y, 0, Handle, IntPtr.Zero);
        if (cmd != 0) PostMessage(Handle, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)cmd, IntPtr.Zero);
    }
    const int HTCLIENT = 1, HTCAPTION = 2, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [StructLayout(LayoutKind.Sequential)]
    struct TRACKMOUSEEVENT { public int cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    bool trackingNcLeave;

    // Height of the invisible resize border; also how far a maximized window reaches past the screen edge
    static int FrameY { get { return GetSystemMetrics(33 /* SM_CYFRAME */) + GetSystemMetrics(92 /* SM_CXPADDEDBORDER */); } }

    static bool IsWindowButton(int ht) { return ht == AppBar.HTMINBUTTON || ht == AppBar.HTMAXBUTTON || ht == AppBar.HTCLOSE; }

    void SetHotButton(int ht)
    {
        if (appBar.Hot == ht) return;
        appBar.Hot = ht;
        appBar.Invalidate();
    }

    // True when the message was handled here
    bool TitleBarMessage(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCCALCSIZE:
                if (m.WParam == IntPtr.Zero) return false;
                int top = Marshal.ReadInt32(m.LParam, 4);   // NCCALCSIZE_PARAMS.rgrc[0].top
                base.WndProc(ref m);
                // no title bar; a maximized window sticks out past the screen by its frame, so start below that
                Marshal.WriteInt32(m.LParam, 4, top + (IsZoomed(Handle) ? FrameY : 0));
                m.Result = IntPtr.Zero;
                return true;

            case WM_NCHITTEST:
                base.WndProc(ref m);
                if (m.Result.ToInt64() != HTCLIENT) return true;   // the side and bottom resize edges
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int frame = FrameY;
                if (!IsZoomed(Handle) && p.Y < frame)
                    m.Result = (IntPtr)(p.X < frame * 2 ? HTTOPLEFT : p.X >= ClientSize.Width - frame * 2 ? HTTOPRIGHT : HTTOP);
                else if (appBar.Bounds.Contains(p))
                {
                    int button = appBar.ButtonAt(new Point(p.X - appBar.Left, p.Y - appBar.Top));
                    m.Result = (IntPtr)(button != 0 ? button : HTCAPTION);
                }
                return true;

            case WM_NCMOUSEMOVE:
                SetHotButton(IsWindowButton(m.WParam.ToInt32()) ? m.WParam.ToInt32() : 0);
                if (!trackingNcLeave)
                {
                    var t = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(TRACKMOUSEEVENT)), dwFlags = 0x02 | 0x10 /* TME_LEAVE | TME_NONCLIENT */, hwndTrack = Handle };
                    trackingNcLeave = TrackMouseEvent(ref t);
                }
                return false;

            case WM_NCMOUSELEAVE:
                trackingNcLeave = false;
                SetHotButton(0);
                if (appBar.Pressed != 0) { appBar.Pressed = 0; appBar.Invalidate(); }
                return false;

            case WM_NCLBUTTONDOWN:
            case WM_NCLBUTTONDBLCLK:
                if (!IsWindowButton(m.WParam.ToInt32())) return false;   // the caption: Windows drags or maximizes
                appBar.Pressed = m.WParam.ToInt32();   // drawn here; Windows would draw its old buttons
                appBar.Invalidate();
                m.Result = IntPtr.Zero;
                return true;

            case WM_NCRBUTTONUP:
                // Windows places its window menu by its own title bar, which isn't there any more: open it here
                if (m.WParam.ToInt32() != HTCAPTION) return false;
                long rp = m.LParam.ToInt64();
                ShowSystemMenu(new Point((short)(rp & 0xFFFF), (short)((rp >> 16) & 0xFFFF)));
                m.Result = IntPtr.Zero;
                return true;

            case WM_NCLBUTTONUP:
                int pressed = appBar.Pressed;
                if (pressed == 0) return false;
                appBar.Pressed = 0;
                appBar.Invalidate();
                if (m.WParam.ToInt32() == pressed)
                {
                    if (pressed == AppBar.HTCLOSE) Close();
                    else if (pressed == AppBar.HTMINBUTTON) WindowState = FormWindowState.Minimized;
                    else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                }
                m.Result = IntPtr.Zero;
                return true;
        }
        return false;
    }

    // Programs, shortcuts and folders dropped onto the window (Windows: WM_DROPFILES, see FileDrop)
    protected override void WndProc(ref Message m)
    {
        if (CustomTitleBar && TitleBarMessage(ref m)) return;
        if (m.Msg == FileDrop.WM_DROPFILES)
        {
            var paths = FileDrop.Paths(m.WParam);
            Program.Log("dropped " + paths.Count + ": " + string.Join(" | ", paths));
            Activate();
            BeginInvoke(new Action(() => AddPaths(paths)));   // lets Explorer finish the drag before any dialog
            return;
        }
        if (m.Msg == WM_SETTINGCHANGE && m.LParam != IntPtr.Zero)
        {
            string what = null;
            try { what = Marshal.PtrToStringUni(m.LParam); } catch { }
            if (what == "ImmersiveColorSet") BeginInvoke(new Action(FollowWindowsTheme));
        }
        var handed = SingleInstance.Receive(ref m);
        if (handed != null)
        {
            Program.Log("got " + handed.Count + " path(s) from a second launch" + (handed.Count > 0 ? ": " + string.Join(" | ", handed) : ""));
            BeginInvoke(new Action(() =>   // after the sender got its answer
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                SingleInstance.SetForegroundWindow(Handle);
                if (handed.Count > 0) AddPaths(handed);
            }));
            return;
        }
        base.WndProc(ref m);
    }

    // OLE drag and drop, for Mono (on Windows the elevated window only gets WM_DROPFILES)
    void EnableOleDrop(Control c)
    {
        c.AllowDrop = true;
        c.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        c.DragDrop += (s, e) =>
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) BeginInvoke(new Action(() => AddPaths(files)));
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5) { Reload(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.F) { search.Box.Focus(); search.Box.SelectAll(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.Z && !search.Box.Focused) { Undo(); e.Handled = true; e.SuppressKeyPress = true; }   // the search box keeps its own undo
        base.OnKeyDown(e);
    }

    // ---------- Layout ----------
    void LayoutToolbar()
    {
        int h = Dp(40), y = (toolbar.Height - h) / 2 + Dp(2);
        int x = Dp(28);
        // right side: refresh, Add, and the overflow menu last
        btnMore.Bounds = new Rectangle(toolbar.Width - Dp(24) - h, y, h, h);
        int addW = btnAdd.PreferredWidth();
        btnAdd.Bounds = new Rectangle(btnMore.Left - Dp(8) - addW, y, addW, h);
        btnRefresh.Bounds = new Rectangle(btnAdd.Left - Dp(8) - h, y, h, h);
        int rightW = toolbar.Width - btnRefresh.Left;
        int chipsW = chipHide.PreferredWidth() + chipOverride.PreferredWidth() + chipBlurry.PreferredWidth() + Dp(16) + Dp(16);
        int countW = Theme.Measure("9999 programs", Theme.Small) + Dp(8 + 12 + 16);   // room for the count between the chips and refresh
        int searchW = Math.Max(Dp(180), Math.Min(Dp(340), toolbar.Width - Dp(28) - rightW - chipsW - countW));
        search.Bounds = new Rectangle(x, y, searchW, h);
        x += searchW + Dp(16);
        int ch = Dp(32), cy = y + (h - ch) / 2;
        chipHide.Bounds = new Rectangle(x, cy, chipHide.PreferredWidth(), ch);
        x += chipHide.Width + Dp(8);
        chipOverride.Bounds = new Rectangle(x, cy, chipOverride.PreferredWidth(), ch);
        x += chipOverride.Width + Dp(8);
        chipBlurry.Bounds = new Rectangle(x, cy, chipBlurry.PreferredWidth(), ch);
        toolbar.Invalidate();
    }

    void LayoutBottom()
    {
        int h = Dp(40), y = (bottom.Height - h) / 2;
        int applyW = btnApply.PreferredWidth();
        btnApply.Bounds = new Rectangle(bottom.Width - Dp(28) - applyW, y, applyW, h);
        int restartW = btnRestart.PreferredWidth();
        btnRestart.Bounds = new Rectangle(btnApply.Left - Dp(8) - restartW, y, restartW, h);
        int segW = modes.PreferredWidth();
        modes.Bounds = new Rectangle(btnRestart.Left - Dp(24) - segW, y, segW, h);
        int scopeW = btnScope.PreferredWidth();
        btnScope.Bounds = new Rectangle(modes.Left - Dp(8) - scopeW, y, scopeW, h);
        bottom.Invalidate();
    }

    int[] ColumnWidths(int total)
    {
        int c1 = Dp(140), c2 = Dp(140), c3 = Dp(160);
        int rest = Math.Max(Dp(300), total - c1 - c2 - c3);
        int c0 = Math.Max(Dp(260), (int)(rest * 0.48f));
        int c4 = Math.Max(Dp(60), total - c0 - c1 - c2 - c3);
        return new[] { c0, c1, c2, c3, c4 };
    }

    void LayoutColumns()
    {
        var w = ColumnWidths(list.ClientSize.Width);
        list.BeginUpdate();
        for (int i = 0; i < w.Length; i++) list.Columns[i].Width = w[i];
        list.EndUpdate();
        header.Invalidate();
        PlaceEmpty();
    }

    void PlaceEmpty()
    {
        empty.Location = new Point(list.Left + (list.Width - empty.Width) / 2, list.Top + Dp(48));
    }

    // ---------- Painting ----------
    Rectangle CardRect()
    {
        var p = content.Padding;
        return new Rectangle(p.Left - Dp(12), p.Top - Dp(4), content.Width - p.Left - p.Right + Dp(24), content.Height - p.Top - p.Bottom + Dp(16));
    }

    void PaintCard(object sender, PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var r = CardRect();
        float rad = Dp(16);
        // soft shadow
        for (int i = 3; i >= 1; i--)
        {
            var sr = new RectangleF(r.X - i * 0.5f, r.Y + i * 0.8f, r.Width + i, r.Height + i * 0.8f);
            Theme.FillRound(g, Color.FromArgb((Theme.Dark ? 30 : 10) + (3 - i) * 4, Theme.Shadow), sr, rad + i);
        }
        Theme.FillRound(g, Theme.Card, r, rad);
        Theme.StrokeRound(g, Theme.Divider, 1f, r, rad);
    }

    void PaintToolbar(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        string count = shownCount == 1 ? "1 program" : shownCount + " programs";
        int w = Theme.Measure(count, Theme.Small) + Dp(8);
        int right = btnRefresh.Left - Dp(12);
        int left = chipBlurry.Right + Dp(16);
        if (right - w > left)
            Theme.Text(g, count, Theme.Small, new Rectangle(right - w, btnAdd.Top, w, btnAdd.Height), Theme.OnSurfaceVariant, TextFormatFlags.Right);
    }

    void PaintHeader(object sender, PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        g.Clear(Theme.Card);
        int x = 0;
        for (int i = 0; i < ColNames.Length; i++)
        {
            int w = list.Columns[i].Width;
            int pad = i == 0 ? Dp(16) : Dp(8);
            bool active = i == sortCol;
            Color c = active ? Theme.Primary : Theme.OnSurfaceVariant;
            int tw = Theme.Measure(ColNames[i], Theme.SmallStrong);
            Theme.Text(g, ColNames[i], Theme.SmallStrong, new Rectangle(x + pad, 0, w - pad, header.Height), c);
            if (active)
                Theme.Glyph(g, sortAsc ? Theme.GUp : Theme.GDown, Theme.IconSmall, new Rectangle(x + pad + tw + Dp(4), 0, Dp(16), header.Height), c);
            x += w;
        }
        using (var pen = new Pen(Theme.OutlineVariant, 1f))
            g.DrawLine(pen, Dp(8), header.Height - 1, header.Width - Dp(8), header.Height - 1);
    }

    void PaintBottom(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var pen = new Pen(Theme.OutlineVariant, 1f)) g.DrawLine(pen, 0, 0, bottom.Width, 0);
        int n = SelectedEntries().Count;
        string title = n == 0 ? "Select programs in the list" : n == 1 ? "1 program selected" : n + " programs selected";
        string sub = "Scaling performed by: " + ModeDescription() + "  ·  restart afterwards";
        int x = Dp(28), w = btnScope.Left - x - Dp(8);
        int mid = bottom.Height / 2;
        Theme.Text(g, title, Theme.BodyStrong, new Rectangle(x, mid - Dp(22), w, Dp(22)), Theme.OnSurface);
        Theme.Text(g, sub, Theme.Small, new Rectangle(x, mid + Dp(1), w, Dp(20)), Theme.OnSurfaceVariant);
    }

    string ModeDescription()
    {
        switch (modes.Selected)
        {
            case 0: return "the app itself (sharp)";
            case 1: return "Windows (may blur)";
            case 2: return "Windows, enhanced";
            default: return "default (remove override)";
        }
    }

    void DrawRow(object sender, DrawListViewItemEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var row = (Row)e.Item.Tag;
        var r = e.Bounds;
        r.Width = Math.Max(r.Width, list.ClientSize.Width);
        using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);

        int indent = row.IsChild ? Dp(ChildIndent) : 0;
        int iconX = r.X + Dp(Gutter + 14) + indent;
        bool sel = e.Item.Selected, hov = e.ItemIndex == hoverIndex;
        bool inSelectedGroup = row.IsChild && row.Parent.Item.Selected;    // included when the group is applied
        var hr = new RectangleF(r.X + Dp(6), r.Y + Dp(2), r.Width - Dp(12), r.Height - Dp(4));
        if (sel) Theme.FillRound(g, Theme.SecondaryContainer, hr, Dp(12));
        else if (inSelectedGroup) Theme.FillRound(g, Theme.Mix(Theme.Card, Theme.SecondaryContainer, hov ? 0.7f : 0.45f), hr, Dp(12));
        else if (hov) Theme.FillRound(g, Theme.Mix(Theme.Card, Theme.OnSurface, 0.05f), hr, Dp(12));
        else if (e.ItemIndex < list.Items.Count - 1)
            using (var pen = new Pen(Theme.Divider, 1f)) g.DrawLine(pen, iconX, r.Bottom - 1, r.Right - Dp(16), r.Bottom - 1);

        var w = ColumnWidths(list.ClientSize.Width);
        int x = r.X;
        int mid = r.Y + r.Height / 2;
        var en = row.IsGroup ? row.Group.Lead : row.Entry;

        // Column 0: chevron + icon + name + exe
        if (row.IsGroup) Theme.Chevron(g, new Point(r.X + Dp(26), mid), expanded.Contains(row.Group.Root), Theme.OnSurfaceVariant);
        int icon = Dp(32);
        var ir = new Rectangle(iconX, r.Y + (r.Height - icon) / 2, icon, icon);
        var bmp = GetIcon(en.Path);
        string title = row.IsGroup ? row.Group.Name : en.Display;
        if (bmp != null) g.DrawImage(bmp, ir);
        else
        {
            Theme.FillRound(g, Theme.PrimaryContainer, ir, Dp(8));
            Theme.Text(g, title.Length > 0 ? title.Substring(0, 1).ToUpperInvariant() : "?", Theme.BodyStrong, ir, Theme.OnPrimaryContainer, TextFormatFlags.HorizontalCenter);
        }
        if (row.IsGroup) DrawCountBadge(g, row.Group.Members.Count, ir, sel ? Theme.SecondaryContainer : Theme.Card);

        string sub;
        if (row.IsGroup)
        {
            int running = row.Group.RunningCount;
            sub = row.Group.Members.Count + " programs" + (running > 0 ? "  ·  " + running + " running" : "");
        }
        else sub = row.IsChild ? Grouping.Relative(en.Path, row.Group.Root) : en.Name;
        int tx = ir.Right + Dp(14), tw = w[0] - (tx - x) - Dp(8);
        Theme.Text(g, title, Theme.BodyStrong, new Rectangle(tx, mid - Dp(21), tw, Dp(22)), Theme.OnSurface);
        Theme.Text(g, sub, Theme.Small, new Rectangle(tx, mid + Dp(1), tw, Dp(19)), Theme.OnSurfaceVariant);
        x += w[0];

        DrawModeChip(g, row.IsGroup ? GroupMode(row.Group, true) : en.AllUsers, new Rectangle(x + Dp(8), r.Y, w[1] - Dp(16), r.Height));
        x += w[1];
        DrawModeChip(g, row.IsGroup ? GroupMode(row.Group, false) : en.CurrentUser, new Rectangle(x + Dp(8), r.Y, w[2] - Dp(16), r.Height),
                     !row.IsGroup && Ignored(en));
        x += w[2];

        DrawLook(g, row.IsGroup ? LookOf(row.Group) : LookOf(en), new Rectangle(x + Dp(8), r.Y, w[3] - Dp(16), r.Height));
        x += w[3];

        string dir = row.IsGroup ? row.Group.Root : Grouping.FolderOf(en.Path);
        Theme.Text(g, dir, Theme.Small, new Rectangle(x + Dp(8), r.Y, w[4] - Dp(24), r.Height), Theme.OnSurfaceVariant, TextFormatFlags.PathEllipsis);
    }

    // Number of programs in a group, as a small badge on the icon's corner
    void DrawCountBadge(Graphics g, int n, Rectangle icon, Color ring)
    {
        string t = n > 99 ? "99+" : n.ToString();
        int h = Dp(18), bw = Math.Max(h, Theme.Measure(t, Theme.SmallStrong) + Dp(10));
        var br = new RectangleF(icon.Right - bw + Dp(7), icon.Bottom - h + Dp(5), bw, h);
        var ringR = RectangleF.Inflate(br, Dp(2), Dp(2));
        Theme.FillRound(g, ring, ringR, ringR.Height / 2f);
        Theme.FillRound(g, Theme.Primary, br, br.Height / 2f);
        TextRenderer.DrawText(g, t, Theme.SmallStrong, Rectangle.Round(br), Theme.OnPrimary,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }

    // The group's setting when all its programs agree, otherwise "Mixed"
    static string GroupMode(Group grp, bool allUsers)
    {
        var modes = grp.Members.Select(m => allUsers ? m.AllUsers : m.CurrentUser).Distinct().ToList();
        return modes.Count == 1 ? modes[0] : "Mixed";
    }

    // ---------- Rendering (live DPI awareness) ----------
    enum Look { NotRunning, NoWindow, Unknown, Sharp, MainOnly, Blurry, Enhanced, Unscaled }

    bool AnyScaled { get { return monitorDpis.Count == 0 ? Theme.S > 1.01f : monitorDpis.Any(d => d > 96); } }
    bool MixedDpi { get { return monitorDpis.Distinct().Count() > 1; } }

    Look LookOf(Entry e)
    {
        if (!e.Running) return Look.NotRunning;
        if (!e.HasWindow) return Look.NoWindow;
        switch (e.Rendering)
        {
            case Awareness.PerMonitor:
            case Awareness.PerMonitorV2: return Look.Sharp;
            case Awareness.System: return MixedDpi ? Look.MainOnly : Look.Sharp;
            case Awareness.GdiScaled: return AnyScaled ? Look.Enhanced : Look.Unscaled;
            case Awareness.Unaware: return AnyScaled ? Look.Blurry : Look.Unscaled;
            default: return Look.Unknown;
        }
    }

    // A group looks as bad as its worst running program
    Look LookOf(Group grp)
    {
        var running = grp.Members.Where(m => m.Running).ToList();
        if (running.Count == 0) return Look.NotRunning;
        return running.Select(m => LookOf(m)).OrderBy(l => LookRank(l)).First();
    }

    static int LookRank(Look l)
    {
        switch (l)
        {
            case Look.Blurry: return 0;
            case Look.MainOnly: return 1;
            case Look.Enhanced: return 2;
            case Look.Unscaled: return 3;
            case Look.Sharp: return 4;
            case Look.Unknown: return 5;
            case Look.NoWindow: return 6;
            default: return 7;
        }
    }


    void DrawLook(Graphics g, Look look, Rectangle cell)
    {
        string label; Color bg, fg;
        switch (look)
        {
            case Look.Sharp: label = "Sharp"; bg = Theme.GoodBg; fg = Theme.GoodFg; break;
            case Look.MainOnly: label = "Main screen only"; bg = Theme.WarnBg; fg = Theme.WarnFg; break;
            case Look.Blurry: label = "Blurry"; bg = Theme.BadBg; fg = Theme.BadFg; break;
            case Look.Enhanced: label = "GDI-scaled"; bg = Theme.InfoBg; fg = Theme.InfoFg; break;
            case Look.Unscaled: label = "Unscaled"; bg = Theme.SurfaceContainerHigh; fg = Theme.OnSurfaceVariant; break;
            case Look.Unknown: Theme.Text(g, "Unknown", Theme.Small, cell, Theme.Outline); return;
            case Look.NoWindow: Theme.Text(g, "No window", Theme.Small, cell, Theme.Outline); return;
            default: Theme.Text(g, "Not running", Theme.Small, cell, Theme.OutlineVariant); return;
        }
        DrawChip(g, label, bg, fg, cell);
    }

    void DrawChip(Graphics g, string label, Color bg, Color fg, Rectangle cell)
    {
        int h = Dp(26);
        int cw = Math.Min(cell.Width, Theme.Measure(label, Theme.SmallStrong) + Dp(24));
        var cr = new RectangleF(cell.X, cell.Y + (cell.Height - h) / 2f, cw, h);
        Theme.FillRound(g, bg, cr, Dp(8));
        TextRenderer.DrawText(g, label, Theme.SmallStrong, Rectangle.Round(cr), fg,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
    }

    // The current user's value doesn't count: Windows uses the all-users one (its Compatibility tab greys it out)
    static bool Ignored(Entry e) { return e.AllUsers != "Not set" && e.CurrentUser != "Not set"; }

    // `ignored`: a value Windows doesn't use, drawn muted
    void DrawModeChip(Graphics g, string mode, Rectangle cell, bool ignored = false)
    {
        Color bg, fg;
        Theme.ModeColors(mode, out bg, out fg);
        if (mode == "Not set")
        {
            Theme.Text(g, "Not set", Theme.Small, cell, Theme.Outline);
            return;
        }
        if (mode == "Mixed" || ignored) { bg = Theme.SurfaceContainerHigh; fg = ignored ? Theme.Outline : Theme.OnSurfaceVariant; }
        string label = mode == "System (Enhanced)" ? "Enhanced" : mode;
        int h = Dp(26);
        int cw = Math.Min(cell.Width, Theme.Measure(label, Theme.SmallStrong) + Dp(24));
        var cr = new RectangleF(cell.X, cell.Y + (cell.Height - h) / 2f, cw, h);
        Theme.FillRound(g, bg, cr, Dp(8));
        TextRenderer.DrawText(g, label, Theme.SmallStrong, Rectangle.Round(cr), fg,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
    }

    // ---------- Interaction ----------
    void SetHover(int idx)
    {
        if (idx == hoverIndex) return;
        int old = hoverIndex; hoverIndex = idx;
        if (old >= 0 && old < list.Items.Count) list.Invalidate(list.Items[old].Bounds);
        if (idx >= 0 && idx < list.Items.Count) list.Invalidate(list.Items[idx].Bounds);
    }

    void ListMouseMove(object sender, MouseEventArgs e)
    {
        var hit = list.HitTest(e.Location);
        SetHover(hit.Item != null ? hit.Item.Index : -1);
    }

    bool OnChevron(ListViewItem it, int x)
    {
        return ((Row)it.Tag).IsGroup && x - it.Bounds.X < Dp(Gutter + 12);
    }

    void ListMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var it = list.GetItemAt(e.X, e.Y);
        if (it != null && OnChevron(it, e.X))
        {
            var row = (Row)it.Tag;
            BeginInvoke(new Action(() => Toggle(row.Group.Root)));   // after the ListView has handled the click
        }
    }

    void ListDoubleClick(object sender, MouseEventArgs e)
    {
        var it = list.GetItemAt(e.X, e.Y);
        if (it != null && ((Row)it.Tag).IsGroup && !OnChevron(it, e.X)) Toggle(((Row)it.Tag).Group.Root);
    }

    void ListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.A)
        {
            list.BeginUpdate(); foreach (ListViewItem it in list.Items) it.Selected = true; list.EndUpdate();
            e.Handled = true;
            return;
        }
        if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
        {
            var at = list.FocusedItem != null && list.FocusedItem.Selected ? list.FocusedItem
                   : list.SelectedItems.Count > 0 ? list.SelectedItems[0] : null;
            if (at != null) { at.EnsureVisible(); ShowRowMenu(new Point(at.Bounds.X + Dp(Gutter + 48), at.Bounds.Bottom - Dp(8))); }
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        var focused = list.FocusedItem;
        if (focused == null) return;
        var row = (Row)focused.Tag;
        bool open = row.IsGroup && expanded.Contains(row.Group.Root);
        if (row.IsGroup && ((e.KeyCode == Keys.Right && !open) || (e.KeyCode == Keys.Left && open) || e.KeyCode == Keys.Enter))
            Toggle(row.Group.Root);
        else if (row.IsChild && e.KeyCode == Keys.Left)
        {
            list.SelectedItems.Clear();
            row.Parent.Item.Selected = true;
            row.Parent.Item.Focused = true;
            row.Parent.Item.EnsureVisible();
        }
        else return;
        e.Handled = true; e.SuppressKeyPress = true;
    }

    // Opens or closes a group. Closing it while programs inside are selected selects the whole group instead.
    void Toggle(string root)
    {
        var keys = SelectedKeys();
        if (expanded.Remove(root))
        {
            var rows = list.SelectedItems.Cast<ListViewItem>().Select(i => (Row)i.Tag).ToList();
            var inside = rows.Where(r => r.IsChild && string.Equals(r.Group.Root, root, StringComparison.OrdinalIgnoreCase)).ToList();
            if (inside.Count > 0)
            {
                keys = rows.Except(inside).SelectMany(RowKeys).ToList();
                keys.Add(inside[0].Group.Key);
            }
        }
        else expanded.Add(root);
        int top = list.TopItem != null ? list.TopItem.Index : 0;
        FillList(keys, false);
        if (top < list.Items.Count) try { list.TopItem = list.Items[top]; } catch { }
        var head = list.Items.Cast<ListViewItem>().FirstOrDefault(i => ((Row)i.Tag).IsGroup &&
                       string.Equals(((Row)i.Tag).Group.Root, root, StringComparison.OrdinalIgnoreCase));
        if (head != null) { head.Focused = true; head.EnsureVisible(); }
    }

    // Programs behind the selected rows (a group row stands for all programs in it)
    List<Entry> SelectedEntries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Entry>();
        foreach (ListViewItem it in list.SelectedItems)
        {
            var row = (Row)it.Tag;
            foreach (var e in row.IsGroup ? row.Group.Members : new List<Entry> { row.Entry })
                if (seen.Add(e.Path)) result.Add(e);
        }
        return result;
    }

    static IEnumerable<string> RowKeys(Row r)
    {
        if (!r.IsGroup) return new[] { r.Entry.Path };
        return new[] { r.Group.Key }.Concat(r.Group.Members.Select(m => m.Path));
    }

    List<string> SelectedKeys()
    {
        return list.SelectedItems.Cast<ListViewItem>().SelectMany(i => RowKeys((Row)i.Tag)).ToList();
    }

    void HeaderClick(object sender, MouseEventArgs e)
    {
        int x = 0;
        for (int i = 0; i < list.Columns.Count; i++)
        {
            x += list.Columns[i].Width;
            if (e.X < x)
            {
                if (sortCol == i) sortAsc = !sortAsc; else { sortCol = i; sortAsc = true; }
                header.Invalidate();
                FillList();
                return;
            }
        }
    }

    void UpdateSelectionUi()
    {
        var sel = SelectedEntries();
        btnApply.Enabled = sel.Count > 0;
        btnRestart.Enabled = !restarting && sel.Any(e => e.Running);
        bottom.Invalidate();
    }

    // ---------- Data ----------
    string Describe(string path)
    {
        string d;
        if (descCache.TryGetValue(path, out d)) return d;
        d = null;
        try { if (File.Exists(path)) d = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { }
        if (string.IsNullOrWhiteSpace(d)) d = Path.GetFileNameWithoutExtension(Grouping.FileName(path));
        d = d.Trim();
        descCache[path] = d;
        return d;
    }

    Bitmap GetIcon(string path)
    {
        Bitmap b;
        if (iconCache.TryGetValue(path, out b)) return b;
        b = null;
        try
        {
            if (File.Exists(path))
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                    if (ic != null) b = ic.ToBitmap();
        }
        catch { }
        iconCache[path] = b;
        return b;
    }

    void Reload(IEnumerable<string> select = null)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var running = Program.Demo ? DemoRunning() : Procs.Running();
            monitorDpis = Program.Demo ? new List<int> { 144, 96 } : DpiInfo.MonitorDpis();
            var lm = Compat.ReadAll(RegistryHive.LocalMachine);
            var cu = Compat.ReadAll(RegistryHive.CurrentUser);

            var owners = Program.Demo ? null : DpiInfo.WindowOwners();
            var paths = new HashSet<string>(running.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in lm) if (Compat.ModeOf(kv.Value) != "Not set") paths.Add(kv.Key);
            foreach (var kv in cu) if (Compat.ModeOf(kv.Value) != "Not set") paths.Add(kv.Key);
            paths.UnionWith(extra);

            entries = paths.Select(p =>
            {
                string a, c;
                List<int> pids;
                lm.TryGetValue(p, out a); cu.TryGetValue(p, out c);
                if (!running.TryGetValue(p, out pids)) pids = new List<int>();
                // judge how it looks by the instances that show a window
                var windowed = owners == null ? pids : pids.Where(owners.Contains).ToList();
                return new Entry
                {
                    Path = p,
                    Name = Grouping.FileName(p),
                    Display = Describe(p),
                    AllUsers = Compat.ModeOf(a),
                    CurrentUser = Compat.ModeOf(c),
                    Pids = pids,
                    Rendering = pids.Count == 0 ? Awareness.Unknown : Program.Demo ? DemoAwareness(p)
                              : DpiInfo.Best(windowed.Count > 0 ? windowed : pids),
                    HasWindow = windowed.Count > 0,
                    System = p.StartsWith(winDir, StringComparison.OrdinalIgnoreCase),
                };
            }).ToList();
            FillList(select, true, select != null);
            Program.Log("loaded " + entries.Count + " entries");
        }
        catch (Exception ex)
        {
            Program.Log("reload failed: " + ex);
            ShowSnack("Couldn't load the program list: " + ex.Message, true);
        }
        finally { Cursor = Cursors.Default; }
    }

    static MainForm current;
    static Look LookOfStatic(Entry e) { return current.LookOf(e); }

    static string Col(Entry e, int c)
    {
        switch (c)
        {
            case 0: return e.Display;
            case 1: return e.AllUsers;
            case 2: return e.CurrentUser;
            case 3: return LookRank(LookOfStatic(e)).ToString();
            default: return e.Path;
        }
    }

    static string Col(Row r, int c)
    {
        if (!r.IsGroup) return Col(r.Entry, c);
        switch (c)
        {
            case 0: return r.Group.Name;
            case 1: return GroupMode(r.Group, true);
            case 2: return GroupMode(r.Group, false);
            case 3: return LookRank(current.LookOf(r.Group)).ToString();
            default: return r.Group.Root;
        }
    }

    IEnumerable<T> Sorted<T>(IEnumerable<T> rows, Func<T, string> key)
    {
        return sortAsc ? rows.OrderBy(key, StringComparer.CurrentCultureIgnoreCase)
                       : rows.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase);
    }

    // Rebuilds the rows. `select` lists the paths (and group keys) to select, by default the current selection.
    // With wholeGroups, a group whose programs are all in `select` is selected as one row.
    void FillList(IEnumerable<string> select = null, bool scroll = true, bool wholeGroups = false)
    {
        var sel = new HashSet<string>(select ?? SelectedKeys(), StringComparer.OrdinalIgnoreCase);
        var f = search.Box.Text.Trim();
        var shown = entries.Where(e =>
        {
            bool hasOverride = e.AllUsers != "Not set" || e.CurrentUser != "Not set";
            if (chipOverride.Checked && !hasOverride) return false;
            if (chipBlurry.Checked) { var l = LookOf(e); if (l != Look.Blurry && l != Look.MainOnly) return false; }
            // Windows programs the user added (Add, drop, Explorer) stay; merely selected ones are filtered like the rest
            if (chipHide.Checked && e.System && !hasOverride && !extra.Contains(e.Path)) return false;
            if (f.Length > 0 && (e.Display + " " + e.Path).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }).ToList();

        // Programs sharing an install folder become one group row
        var roots = Grouping.Roots(shown.Select(e => e.Path), addedFolders);
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        var tops = new List<Row>();
        foreach (var e in shown)
        {
            string root;
            if (!roots.TryGetValue(e.Path, out root)) { tops.Add(new Row { Entry = e }); continue; }
            Group grp;
            if (!groups.TryGetValue(root, out grp))
            {
                grp = new Group { Root = root, Name = Grouping.GroupName(root) };
                groups[root] = grp;
                tops.Add(new Row { Group = grp });
            }
            grp.Members.Add(e);
        }
        foreach (var grp in groups.Values)
            grp.Lead = grp.Members.OrderBy(m => Grouping.Depth(m.Path)).ThenBy(m => m.Running ? 0 : 1).ThenBy(m => m.Name.Length).First();

        hoverIndex = -1;
        list.BeginUpdate();
        list.Items.Clear();
        ListViewItem first = null;
        Action<Row, bool> add = (row, selected) =>
        {
            ListViewItem it;
            if (row.IsGroup)
            {
                it = new ListViewItem(new[] { row.Group.Name, GroupMode(row.Group, true), GroupMode(row.Group, false), LookOf(row.Group).ToString(), row.Group.Root })
                     { Tag = row };
            }
            else
            {
                var e = row.Entry;
                it = new ListViewItem(new[] { e.Display, e.AllUsers, e.CurrentUser, LookOf(e).ToString(), e.Path }) { Tag = row };
            }
            row.Item = it;
            list.Items.Add(it);
            if (selected) { it.Selected = true; if (first == null) first = it; }
        };
        foreach (var top in Sorted(tops, r => Col(r, sortCol)))
        {
            if (!top.IsGroup) { add(top, sel.Contains(top.Entry.Path)); continue; }
            var grp = top.Group;
            bool whole = sel.Contains(grp.Key) || (wholeGroups && grp.Members.All(m => sel.Contains(m.Path)));
            if (!whole && grp.Members.Any(m => sel.Contains(m.Path))) expanded.Add(grp.Root);   // show selected programs inside
            add(top, whole);
            if (expanded.Contains(grp.Root))
                foreach (var m in Sorted(grp.Members, m => Col(m, sortCol)))
                    add(new Row { Entry = m, Group = grp, Parent = top }, !whole && sel.Contains(m.Path));
        }
        list.EndUpdate();
        LayoutColumns();
        if (first != null && scroll) first.EnsureVisible();
        shownCount = shown.Count;
        empty.Visible = shown.Count == 0;
        list.Visible = shown.Count > 0;   // an empty themed list draws column lines; the card behind it is plain
        PlaceEmpty();
        toolbar.Invalidate();
        UpdateSelectionUi();
    }

    // ---------- Adding programs and folders ----------
    void ShowAddMenu()
    {
        // shortcut for the selected program's folder
        addHereDir = SelectedFolder();
        addHere.Visible = addHereSep.Visible = addHereDir != null;
        if (addHereDir != null) addHere.Text = "All programs in \u201C" + Grouping.NameOf(addHereDir) + "\u201D";
        addMenu.Show(btnAdd, new Point(btnAdd.Width, btnAdd.Height + Dp(4)), ToolStripDropDownDirection.BelowLeft);
    }

    // Install folder of the one selected row (its group's folder, or the folder the .exe is in)
    string SelectedFolder()
    {
        if (list.SelectedItems.Count != 1) return null;
        var row = (Row)list.SelectedItems[0].Tag;
        string dir = row.Group != null ? row.Group.Root : Grouping.FolderOf(row.Entry.Path);
        return Directory.Exists(dir) ? dir : null;
    }

    void Browse()
    {
        using (var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            AddPaths(new[] { dlg.FileName });
        }
    }

    void BrowseFolder()
    {
        string start = SelectedFolder();
        if (start == null) try { start = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); } catch { }
        string dir = FolderPicker.Pick(this, "Choose a folder: every program (.exe) in it and its subfolders is added", start);
        if (dir != null) AddPaths(new[] { dir });
    }

    // Adds .exe files and every .exe inside folders, then selects them so a mode can be applied in one go
    void AddPaths(IEnumerable<string> paths)
    {
        var added = new List<string>();
        var errors = new List<string>();
        string folderName = null;
        int folders = 0;
        foreach (var raw in paths)
        {
            string p = raw;
            if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            {
                string target = Shortcut.Target(p);
                if (target == null)
                {
                    errors.Add("The shortcut " + Path.GetFileNameWithoutExtension(p) + " doesn't point to a program file (Store apps have none)");
                    continue;
                }
                string real = File.Exists(target) ? Grouping.RealProgram(target) : target;
                Program.Log("shortcut " + p + " -> " + target + (real != target ? " -> " + real : ""));
                p = real;
            }
            if (Directory.Exists(p))
            {
                var exes = ScanFolder(p);
                if (exes == null) continue;
                if (exes.Count == 0) { errors.Add("No programs (.exe) in " + p); continue; }
                addedFolders.Add(p);
                added.AddRange(exes);
                folderName = Grouping.NameOf(p.TrimEnd(new[] { '\\', '/' }));
                folders++;
            }
            else if (File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) added.Add(p);
            else errors.Add("Not a program or folder: " + Grouping.FileName(p));
        }
        foreach (var err in errors) Program.Log(err);
        if (added.Count == 0)
        {
            if (errors.Count > 0) ShowSnack(errors[0], true);
            return;
        }
        Program.Log("added " + added.Count + " programs" + (folders > 0 ? " from " + folders + " folder(s)" : ""));
        extra.UnionWith(added);
        // make sure the new programs aren't hidden by the search or a filter
        search.Box.Text = "";
        chipOverride.Checked = false;
        chipBlurry.Checked = false;
        Reload(added);
        list.Focus();
        string what = added.Count == 1 ? Describe(added[0]) : added.Count + " programs" + (folders == 1 ? " from " + folderName : "");
        ShowSnack("Added " + what + ". Choose a scaling mode and apply it." + (errors.Count > 0 ? " (" + errors.Count + " skipped)" : ""), false);
    }

    // Every .exe in a folder, after asking when there are many. Null when cancelled.
    List<string> ScanFolder(string dir)
    {
        List<string> exes;
        Cursor = Cursors.WaitCursor;
        try { exes = Grouping.FindExes(dir, MaxFolderScan + 1); }
        finally { Cursor = Cursors.Default; }
        bool capped = exes.Count > MaxFolderScan;
        if (capped) exes.RemoveAt(exes.Count - 1);
        if (exes.Count > 40 && !ConfirmDialog.Ask(this, "Add " + exes.Count + (capped ? "+" : "") + " programs?",
                "Found " + (capped ? "more than " : "") + exes.Count + " programs (.exe) in " + dir + " and its subfolders" +
                (capped ? " (only the first " + MaxFolderScan + " are added)" : "") + ". Add them all to the list?", "Add all"))
            return null;
        return exes;
    }

    // ---------- Restart ----------
    void RestartSelected()
    {
        var targets = SelectedEntries().Where(e => e.Running).ToList();
        RestartEntries(targets, SelectedGroupName());
    }

    // Name of the group when the selection is exactly one group row
    string SelectedGroupName()
    {
        if (list.SelectedItems.Count != 1) return null;
        var row = (Row)list.SelectedItems[0].Tag;
        return row.IsGroup ? row.Group.Name : null;
    }

    void RestartEntries(List<Entry> targets, string groupName = null)
    {
        if (restarting || targets.Count == 0) return;
        string names = targets.Count == 1 ? targets[0].Display
                     : targets.Count == 2 ? targets[0].Display + " and " + targets[1].Display
                     : groupName ?? targets.Count + " programs";
        string body = (groupName != null && targets.Count > 2 ? "All " + targets.Count + " running programs in " + groupName : names) +
                      " will be closed and started again so the new scaling setting takes effect. Unsaved work in " +
                      (targets.Count == 1 ? "it" : "them") + " may be lost.";
        if (!ConfirmDialog.Ask(this, "Restart " + (targets.Count == 1 ? targets[0].Display : groupName ?? targets.Count + " programs") + "?", body, "Restart"))
            return;

        restarting = true;
        UpdateSelectionUi();
        ShowSnack("Restarting " + names + "…", false);
        var jobs = targets.Select(t => new Restarter.Job { Exe = t.Path, Display = t.Display, Pids = new List<int>(t.Pids) }).ToList();
        var worker = new System.Threading.Thread(() =>
        {
            List<string> errors;
            try { errors = Program.Demo ? new List<string>() : Restarter.RestartAll(jobs); }
            catch (Exception ex) { errors = new List<string> { names + ": " + ex.Message }; }
            foreach (var err in errors) Program.Log("restart failed: " + err);
            System.Threading.Thread.Sleep(2500); // let the programs start before re-reading their DPI mode
            BeginInvoke(new Action(() =>
            {
                restarting = false;
                Reload();
                if (errors.Count == 0) ShowSnack("Restarted " + names, false);
                else ShowSnack(errors.Count == 1 ? "Couldn't restart " + errors[0] : errors.Count + " programs couldn't be restarted", true);
            }));
        });
        worker.IsBackground = true;
        worker.Start();
    }

    // ---------- Explorer's right-click menu ----------
    static bool ExplorerEntryOn()
    {
        try { return ExplorerMenu.Registered() != null || ExplorerMenu.AllUsersRegistered() || ShellPackage.Registered(); }
        catch { return false; }
    }

    bool shellPackageBusy;

    // The classic entry (instant) and, on Windows 11, the compact menu's package (a few seconds, in the background)
    void ToggleExplorerMenu()
    {
        if (shellPackageBusy) return;
        string label = "“" + ExplorerMenu.Label + "”";
        if (OnlyCompactMenuMissing()) { ChangeShellPackage(false, label); return; }   // e.g. Developer Mode was turned on since
        bool on = ExplorerEntryOn();
        try
        {
            if (on)
            {
                ExplorerMenu.Unregister();
                if (ExplorerMenu.AllUsersRegistered())   // from a version that ran as admin: removing it needs admin rights
                {
                    var res = AllUsers.Set(new List<KeyValuePair<string, string>>(), true, Handle);
                    if (res.Cancelled) { ShowSnack("The entry stays: removing it for all users needs administrator rights", true); return; }
                    if (res.Errors.Count > 0) { ShowSnack("Couldn't remove the Explorer entry: " + res.Errors[0], true); return; }
                }
            }
            else ExplorerMenu.Register(Application.ExecutablePath);
            Program.Log((on ? "removed" : "added") + " the classic Explorer entry");
        }
        catch (Exception ex)
        {
            Program.Log("Explorer entry change failed: " + ex);
            ShowSnack("Couldn't change the Explorer menu: " + ex.Message, true);
            return;
        }

        string why = ShellPackage.Unavailable();
        bool package = on ? ShellPackage.Registered() : why == null;
        if (!package)
        {
            if (on) ShowSnack("Removed " + label + " from Explorer", false);
            else if (ShellPackage.NeedsDeveloperMode)   // the one thing the user can change: say where
                ShowSnack("Added under Show more options. For Windows 11's compact menu too, turn on Developer Mode, then use ⋯ again.",
                          false, "OPEN SETTINGS", () => { try { Process.Start("ms-settings:developers"); } catch { } });
            else
                ShowSnack("Right-click a program or shortcut in Explorer and choose " + label +
                          (ShellPackage.OnWindows11 ? " under Show more options (not in the compact menu: " + why + ")" : ""), false);
            return;
        }
        ChangeShellPackage(on, label);
    }

    // The classic entry is there, the compact menu's isn't, and it could be added now
    static bool OnlyCompactMenuMissing()
    {
        try
        {
            return (ExplorerMenu.Registered() != null || ExplorerMenu.AllUsersRegistered()) &&
                   !ShellPackage.Registered() && ShellPackage.Unavailable() == null;
        }
        catch { return false; }
    }

    // Registers or removes the compact menu's package in the background (it takes about a second)
    void ChangeShellPackage(bool remove, string label)
    {
        bool on = remove;
        shellPackageBusy = true;
        ShowSnack((on ? "Removing it from" : "Adding it to") + " Windows 11's compact menu…", false);
        var worker = new System.Threading.Thread(() =>
        {
            string error = on ? ShellPackage.Unregister() : ShellPackage.Register();
            BeginInvoke(new Action(() =>
            {
                shellPackageBusy = false;
                if (error != null)
                    ShowSnack((on ? "Removed the classic entry, but not the compact menu's: " : "Added to Show more options, but not to the compact menu: ") + error, true);
                else
                    ShowSnack(on ? "Removed " + label + " from Explorer"
                                 : "Right-click a program or shortcut in Explorer and choose " + label, false);
            }));
        });
        worker.IsBackground = true;
        worker.Start();
    }

    // ---------- Back up and restore ----------
    static List<Backup.Item> CurrentOverrides()
    {
        var items = new List<Backup.Item>();
        foreach (bool lm in new[] { true, false })
            foreach (var kv in Compat.ReadAll(lm ? RegistryHive.LocalMachine : RegistryHive.CurrentUser))
                if (Compat.ModeOf(kv.Value) != "Not set") items.Add(new Backup.Item { AllUsers = lm, Exe = kv.Key, Value = kv.Value });
        return items;
    }

    void BackUp()
    {
        List<Backup.Item> items;
        try { items = CurrentOverrides(); }
        catch (Exception ex) { ShowSnack("Couldn't read the overrides: " + ex.Message, true); return; }
        if (items.Count == 0) { ShowSnack("There are no overrides to back up yet", false); return; }
        using (var dlg = new SaveFileDialog
        {
            Title = "Back up DPI overrides",
            Filter = "Registry file (*.reg)|*.reg",
            FileName = "DPI overrides " + DateTime.Now.ToString("yyyy-MM-dd") + ".reg",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string file = dlg.FileName;
            try
            {
                File.WriteAllText(file, Backup.Write(items), System.Text.Encoding.Unicode);   // UTF-16 with BOM, like regedit's exports
                Program.Log("backed up " + items.Count + " overrides to " + file);
                ShowSnack("Backed up " + items.Count + " overrides to " + Path.GetFileName(file), false, "SHOW",
                          () => { try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } });
            }
            catch (Exception ex)
            {
                Program.Log("backup failed: " + ex);
                ShowSnack("Couldn't save the backup: " + ex.Message, true);
            }
        }
    }

    // Sets the DPI mode of every program in a backup. Other compatibility flags, and programs that aren't in the
    // backup, stay as they are.
    void Restore()
    {
        string file;
        using (var dlg = new OpenFileDialog
        {
            Title = "Restore DPI overrides",
            Filter = "Registry file (*.reg)|*.reg",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            file = dlg.FileName;
        }
        List<Backup.Item> items;
        Dictionary<string, string> lm, cu;
        try
        {
            items = Backup.Read(File.ReadAllText(file));
            lm = Compat.ReadAll(RegistryHive.LocalMachine);
            cu = Compat.ReadAll(RegistryHive.CurrentUser);
        }
        catch (Exception ex) { ShowSnack("Couldn't read " + Path.GetFileName(file) + ": " + ex.Message, true); return; }
        if (items.Count == 0) { ShowSnack("No DPI overrides found in " + Path.GetFileName(file), true); return; }

        var changes = items.Where(i =>
        {
            string now;
            (i.AllUsers ? lm : cu).TryGetValue(i.Exe, out now);
            return Compat.ModeOf(now) != Compat.ModeOf(i.Value);
        }).ToList();
        if (changes.Count == 0) { ShowSnack("All " + items.Count + " overrides in the backup are already set", false); return; }
        if (!ConfirmDialog.Ask(this, "Restore " + changes.Count + " override" + (changes.Count == 1 ? "" : "s") + "?",
                changes.Count + " of the " + items.Count + " programs in " + Path.GetFileName(file) +
                " have a different setting now. They get the setting from the backup. Other compatibility settings, and " +
                "programs that aren't in the backup, stay as they are.", "Restore"))
            return;

        var errors = new List<string>();
        var step = UndoStep.Begin("restore from " + Path.GetFileName(file), changes.Select(i => i.Exe));
        Program.Log("restoring " + changes.Count + " override(s) from " + file);
        if (!WriteModes(changes.Where(i => i.AllUsers).Select(i => Op(i.Exe, Compat.ModeOf(i.Value))).ToList(),
                        changes.Where(i => !i.AllUsers).Select(i => Op(i.Exe, Compat.ModeOf(i.Value))).ToList(), errors))
            return;
        var kept = Remember(step);
        var paths = changes.Select(i => i.Exe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        search.Box.Text = "";
        chipOverride.Checked = false;
        chipBlurry.Checked = false;
        Reload(paths);
        if (errors.Count > 0)
        {
            ShowSnack(errors.Count + " of " + changes.Count + " couldn't be restored", true);
            MessageBox.Show(this, string.Join("\n", errors), "Some overrides weren't restored", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        ShowChanged("Restored " + changes.Count + " override" + (changes.Count == 1 ? "" : "s"), paths, kept);
    }

    // ---------- Right-click menu on rows ----------
    List<Row> SelectedRows() { return list.SelectedItems.Cast<ListViewItem>().Select(i => (Row)i.Tag).ToList(); }

    // A group row stands for its folder, a program row for its .exe
    static string PathOf(Row r) { return r.IsGroup ? r.Group.Root : r.Entry.Path; }

    void ShowRowMenu(Point at)
    {
        var rows = SelectedRows();
        if (rows.Count == 0) return;
        var sel = SelectedEntries();
        string single = rows.Count == 1 ? PathOf(rows[0]) : null;
        rowOpen.Enabled = single != null && (rows[0].IsGroup ? Directory.Exists(single)
                                                              : File.Exists(single) || Directory.Exists(Grouping.FolderOf(single)));
        rowCopy.Text = rows.Count == 1 ? "Copy path" : "Copy paths";
        rowRestart.Enabled = !restarting && sel.Any(e => e.Running);
        rowRemove.Enabled = sel.Any(HasOverride);
        rowMenu.Show(list, at);
    }

    static bool HasOverride(Entry e) { return e.AllUsers != "Not set" || e.CurrentUser != "Not set"; }

    void OpenLocation()
    {
        var rows = SelectedRows();
        if (rows.Count != 1) return;
        string p = PathOf(rows[0]);
        try
        {
            // Explorer hands this to the running (non-elevated) desktop Explorer
            if (!rows[0].IsGroup && File.Exists(p)) Process.Start("explorer.exe", "/select,\"" + p + "\"");
            else Process.Start("explorer.exe", "\"" + (rows[0].IsGroup ? p : Grouping.FolderOf(p)) + "\"");
        }
        catch (Exception ex)
        {
            Program.Log("open location failed for " + p + ": " + ex.Message);
            ShowSnack("Couldn't open the folder: " + ex.Message, true);
        }
    }

    void CopyPaths()
    {
        var paths = SelectedRows().Select(PathOf).ToList();
        if (paths.Count == 0) return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
            ShowSnack(paths.Count == 1 ? "Path copied" : paths.Count + " paths copied", false);
        }
        catch (Exception ex) { ShowSnack("Couldn't copy: " + ex.Message, true); }
    }

    // Removes the override for all users and for the current user, wherever one is set
    void RemoveOverride()
    {
        var items = SelectedEntries().Where(HasOverride).ToList();
        if (items.Count == 0) return;
        Change(items, "Override removed for " + What(items.Select(i => i.Display).ToList()),
               items.Where(i => i.AllUsers != "Not set").Select(i => Op(i.Path, "Not set")).ToList(),
               items.Where(i => i.CurrentUser != "Not set").Select(i => Op(i.Path, "Not set")).ToList());
    }

    // ---------- Demo data (for testing the UI under Mono on Linux: --demo) ----------
    static Dictionary<string, List<int>> DemoRunning()
    {
        var d = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { @"C:\Program Files\Steam\steam.exe", @"C:\Program Files\Steam\bin\cef\cef.win7x64\steamwebhelper.exe",
                                  @"C:\Riot Games\League of Legends\LeagueClient.exe", @"C:\Riot Games\League of Legends\LeagueClientUx.exe",
                                  @"C:\Riot Games\League of Legends\LeagueClientUxRender.exe", @"C:\Riot Games\Riot Client\UX\RiotClientUx.exe",
                                  @"C:\Games\Old.exe", @"C:\Tools\Legacy\setup-tool.exe", @"C:\Program Files\Notes\notes.exe" })
            d[p] = new List<int> { 1 };
        return d;
    }

    static Awareness DemoAwareness(string path)
    {
        if (path.EndsWith("steam.exe")) return Awareness.System;
        if (path.EndsWith("LeagueClient.exe") || path.EndsWith("LeagueClientUx.exe")) return Awareness.PerMonitorV2;
        if (path.EndsWith("LeagueClientUxRender.exe") || path.EndsWith("steamwebhelper.exe")) return Awareness.Unaware;
        if (path.EndsWith("Old.exe")) return Awareness.GdiScaled;
        if (path.EndsWith("setup-tool.exe")) return Awareness.Unaware;
        return Awareness.PerMonitor;
    }

    void ShowSnack(string text, bool error, string actionLabel = null, Action onAction = null,
                   string action2Label = null, Action onAction2 = null)
    {
        var area = new Rectangle(0, 0, ClientSize.Width, bottom.Top);
        snack.Show(text, error, area, actionLabel, onAction, action2Label, onAction2);
    }

    // ---------- Undo ----------
    readonly List<UndoStep> undo = new List<UndoStep>();
    const int MaxUndo = 20;

    static string Shorten(string s, int max) { return s.Length <= max ? s : s.Substring(0, max - 1) + "…"; }

    // Keeps the step when it changed anything; returns it, or null
    UndoStep Remember(UndoStep step)
    {
        if (!step.End()) return null;
        undo.Add(step);
        if (undo.Count > MaxUndo) undo.RemoveAt(0);
        return step;
    }

    // Snackbar after a change: UNDO (for exactly this change), plus RESTART when changed programs are running
    void ShowChanged(string message, List<string> paths, UndoStep step, string groupName = null)
    {
        var running = entries.Where(en => en.Running && paths.Contains(en.Path, StringComparer.OrdinalIgnoreCase)).ToList();
        string undoLabel = step != null ? "UNDO" : null;
        Action undoIt = () => Undo(step);
        if (running.Count > 0)
        {
            Action restart = () => RestartEntries(running, groupName);
            if (step != null) ShowSnack(message + ". Restart to apply.", false, undoLabel, undoIt, "RESTART", restart);
            else ShowSnack(message + ". Restart to apply.", false, "RESTART", restart);
        }
        else ShowSnack(message + ".", false, undoLabel, undoIt);
    }

    // Undoes the latest change; with `only`, just that change and only while it's still the latest
    void Undo(UndoStep only = null)
    {
        if (undo.Count == 0) { ShowSnack("Nothing to undo", false); return; }
        var step = undo[undo.Count - 1];
        if (only != null && step != only) { ShowSnack("That change was already undone or another one came after it", false); return; }
        // all-users values first: when the UAC prompt is declined, nothing is undone and the step stays
        var errors = new List<string>();
        var lm = step.AllUsersRevert();
        if (lm.Count > 0)
        {
            var res = AllUsers.Set(lm, false, Handle);
            if (res.Cancelled) { ShowSnack(NeedsAdmin("Nothing undone"), true); return; }
            errors.AddRange(res.Errors);
        }
        undo.RemoveAt(undo.Count - 1);
        errors.AddRange(step.RevertCurrentUser());
        foreach (var c in step.Changes)
            Program.Log("undo " + (c.Hive == RegistryHive.LocalMachine ? "" : "(current user) ") + c.Exe + ": [" + c.After + "] -> [" + c.Before + "]");
        foreach (var err in errors) Program.Log("undo failed: " + err);
        var paths = step.Changes.Select(c => c.Exe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Reload(paths);
        if (errors.Count > 0)
        {
            ShowSnack("Couldn't undo everything: " + errors[0], true);
            return;
        }
        var running = entries.Where(en => en.Running && paths.Contains(en.Path, StringComparer.OrdinalIgnoreCase)).ToList();
        string m = "Undone: " + step.Label;
        if (running.Count > 0) ShowSnack(m + ". Restart to apply.", false, "RESTART", () => RestartEntries(running));
        else ShowSnack(m, false);
    }

    void Apply() { Apply(ModeKeys[modes.Selected], SelectedEntries(), allUsersScope); }

    // For all users: sets it for all users and clears the current user's own value, as Windows' dialog does.
    // Only for me: sets the current user's value; where an all-users value exists, Windows keeps using that one.
    void Apply(string mode, List<Entry> items, bool forAllUsers)
    {
        if (items.Count == 0) return;
        string what = What(items.Select(i => i.Display).ToList());
        string m = (mode == "Not set" ? "Override removed for " + what : modes.Items[Array.IndexOf(ModeKeys, mode)] + " scaling set for " + what) +
                   (forAllUsers ? "" : ", for you only");
        int shadowed = forAllUsers || mode == "Not set" ? 0 : items.Count(i => i.AllUsers != "Not set");
        if (shadowed > 0)
            m += items.Count == 1 ? " (Windows keeps using its all-users setting)"
                                  : " (Windows keeps using the all-users setting for " + shadowed + " of them)";
        if (forAllUsers)
            Change(items, m, items.Select(i => Op(i.Path, mode)).ToList(),
                   items.Where(i => i.CurrentUser != "Not set").Select(i => Op(i.Path, "Not set")).ToList());
        else
            Change(items, m, new List<KeyValuePair<string, string>>(), items.Select(i => Op(i.Path, mode)).ToList());
    }

    // ---------- Who the setting is for ----------
    const string SettingsKey = @"Software\DpiOverride";

    static bool LoadScope()
    {
        try { using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey)) return k == null || Convert.ToString(k.GetValue("Scope")) != "CurrentUser"; }
        catch { return true; }
    }

    void SetScope(bool forAllUsers)
    {
        allUsersScope = forAllUsers;
        try { using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue("Scope", forAllUsers ? "AllUsers" : "CurrentUser"); }
        catch (Exception ex) { Program.Log("couldn't save the scope: " + ex.Message); }
        UpdateScopeUi();
    }

    void UpdateScopeUi()
    {
        btnScope.Text = allUsersScope ? "All users" : "Only me";
        scopeAll.Checked = allUsersScope; scopeMe.Checked = !allUsersScope;   // filled in the menu
        btnScope.Glyph = allUsersScope ? Theme.GPeople : Theme.GPerson;
        // Windows' shield on buttons that ask for admin rights
        bool asks = allUsersScope && Environment.OSVersion.Platform == PlatformID.Win32NT && !Program.IsAdmin();
        btnApply.Glyph = asks ? Theme.GShield : Theme.GCheck;
        applyTip.SetToolTip(btnApply, allUsersScope ? "Set it for all users" + (asks ? " (asks for administrator rights)" : "")
                                                    : "Set it only for you (Windows still prefers an all-users setting)");
        btnScope.Invalidate(); btnApply.Invalidate();
        LayoutBottom();
    }

    // ---------- Writing ----------
    static KeyValuePair<string, string> Op(string exe, string mode) { return new KeyValuePair<string, string>(exe, mode); }

    // "HASS.Agent", "League of Legends (3 programs)" or "4 programs"
    string What(List<string> names)
    {
        string groupName = SelectedGroupName();
        return names.Count == 1 ? names[0] : groupName != null ? groupName + " (" + names.Count + " programs)" : names.Count + " programs";
    }

    static string NeedsAdmin(string what) { return what + ": changing the setting for all users needs administrator rights"; }

    // Sets DPI modes: all-users ones through AllUsers (one UAC prompt unless the app already runs as admin), then the
    // current user's directly. False when the UAC prompt was declined; then nothing was changed.
    bool WriteModes(List<KeyValuePair<string, string>> allUsers, List<KeyValuePair<string, string>> currentUser, List<string> errors)
    {
        if (allUsers.Count > 0)
        {
            Cursor = Cursors.WaitCursor;
            AllUsers.Result res;
            try { res = AllUsers.Set(allUsers, false, Handle); }
            finally { Cursor = Cursors.Default; }
            if (res.Cancelled) { ShowSnack(NeedsAdmin("Nothing changed"), true); return false; }
            errors.AddRange(res.Errors);
        }
        foreach (var o in currentUser)
        {
            try
            {
                Compat.Set(RegistryHive.CurrentUser, o.Key, o.Value);
                Program.Log("set " + o.Value + " (current user) for " + o.Key);
            }
            catch (Exception ex)
            {
                errors.Add(Grouping.FileName(o.Key) + ": " + ex.Message);
                Program.Log("set failed (current user) for " + o.Key + ": " + ex);
            }
        }
        return true;
    }

    // Makes a change that can be undone, reloads with the programs selected and reports it
    void Change(List<Entry> items, string message, List<KeyValuePair<string, string>> allUsers, List<KeyValuePair<string, string>> currentUser)
    {
        string groupName = SelectedGroupName();
        var paths = items.Select(i => i.Path).ToList();
        var errors = new List<string>();
        var step = UndoStep.Begin(null, paths);
        if (!WriteModes(allUsers, currentUser, errors)) return;
        step.Label = errors.Count == 0 ? message : "the last change";
        var kept = Remember(step);   // also when some failed: the ones that worked can be undone
        Reload(paths);

        if (errors.Count > 0)
        {
            ShowSnack(errors.Count == 1 ? "Couldn't change " + errors[0] : errors.Count + " changes failed", true);
            MessageBox.Show(this, string.Join("\n", errors), "Some changes failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else ShowChanged(message, paths, kept, groupName);
    }
}
