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

    // Material 3 baseline (purple) palette
    public static readonly Color Primary = C(0x6750A4);
    public static readonly Color OnPrimary = Color.White;
    public static readonly Color PrimaryContainer = C(0xEADDFF);
    public static readonly Color OnPrimaryContainer = C(0x21005D);
    public static readonly Color SecondaryContainer = C(0xE8DEF8);
    public static readonly Color OnSecondaryContainer = C(0x1D192B);
    public static readonly Color Surface = C(0xFEF7FF);
    public static readonly Color SurfaceContainer = C(0xF3EDF7);
    public static readonly Color SurfaceContainerHigh = C(0xECE6F0);
    public static readonly Color Card = Color.White;
    public static readonly Color OnSurface = C(0x1D1B20);
    public static readonly Color OnSurfaceVariant = C(0x49454F);
    public static readonly Color Outline = C(0x79747E);
    public static readonly Color OutlineVariant = C(0xCAC4D0);
    public static readonly Color Divider = C(0xEFEAF3);
    public static readonly Color InverseSurface = C(0x322F35);
    public static readonly Color InverseOnSurface = C(0xF5EFF7);
    public static readonly Color InversePrimary = C(0xD0BCFF);
    public static readonly Color Error = C(0xB3261E);
    public static readonly Color Running = C(0x2E7D32);

    public static Font Body, BodyStrong, Small, SmallStrong, Title, Subtitle, Label, Icon, IconSmall;
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
            case "Application": bg = C(0xDCF3E2); fg = C(0x1B5E20); break;
            case "System": bg = C(0xDCE7FA); fg = C(0x0D3C8C); break;
            case "System (Enhanced)": bg = C(0xFFE9C7); fg = C(0x7A4100); break;
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

enum ButtonKind { Filled, Tonal, Text }

class MButton : MControl
{
    public ButtonKind Kind = ButtonKind.Filled;
    public string Glyph;
    public Color? ContentOverride;

    public int PreferredWidth()
    {
        int w = Theme.Measure(Text, Theme.Label) + Theme.Dp(48);
        if (Glyph != null && Theme.HasIcons) w += Theme.Dp(26) - Theme.Dp(8);
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
            default: bg = Color.Transparent; fg = ContentOverride ?? Theme.Primary; break;
        }
        if (!Enabled)
        {
            bg = Kind == ButtonKind.Text ? Color.Transparent : Theme.Alpha(Theme.OnSurface, 0.12f);
            fg = Theme.Alpha(Theme.OnSurface, 0.38f);
            fg = Theme.Mix(Theme.SurfaceContainer, Theme.OnSurface, 0.38f);
        }
        if (bg.A > 0) Theme.FillRound(g, bg, r, rad);
        if (StateAlpha > 0) Theme.FillRound(g, Theme.Alpha(fg, StateAlpha), r, rad);

        bool glyph = Glyph != null && Theme.HasIcons;
        int tw = Theme.Measure(Text, Theme.Label);
        int total = tw + (glyph ? Theme.Dp(26) : 0);
        int x = (Width - total) / 2;
        if (glyph)
        {
            Theme.Glyph(g, Glyph, Theme.IconSmall, new Rectangle(x, 0, Theme.Dp(18), Height), fg);
            x += Theme.Dp(26);
        }
        TextRenderer.DrawText(g, Text, Theme.Label, new Rectangle(x, 0, tw + Theme.Dp(4), Height), fg,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

class IconButton : MControl
{
    public string Glyph;
    public Color Fore = Theme.OnSurfaceVariant;

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
        return Theme.Measure(Text, Theme.Label) + Theme.Dp(32) + (Theme.HasIcons ? Theme.Dp(26) : 0);
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

        int x = Theme.Dp(16);
        if (Theme.HasIcons)
        {
            if (isChecked) Theme.Glyph(g, Theme.GCheck, Theme.IconSmall, new Rectangle(Theme.Dp(10), 0, Theme.Dp(18), Height), fg);
            x = isChecked ? Theme.Dp(34) : Theme.Dp(16) + Theme.Dp(9);
        }
        TextRenderer.DrawText(g, Text, Theme.Label, new Rectangle(x, 0, Width - x, Height), fg,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
        return Theme.Measure(Items[i], Theme.Label) + Theme.Dp(36) + (Theme.HasIcons ? Theme.Dp(24) : 0);
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
            int tw = Theme.Measure(Items[i], Theme.Label);
            int gw = sel && Theme.HasIcons ? Theme.Dp(24) : 0;
            int segW = edges[i + 1] - edges[i];
            int x = edges[i] + (segW - tw - gw) / 2;
            if (gw > 0) Theme.Glyph(g, Theme.GCheck, Theme.IconSmall, new Rectangle(x, 0, Theme.Dp(18), Height), fg);
            TextRenderer.DrawText(g, Items[i], Theme.Label, new Rectangle(x + gw, 0, tw + Theme.Dp(4), Height), fg,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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

class AppBar : Control
{
    public Image Logo;
    public AppBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Primary;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        g.Clear(Theme.Primary);
        int x = Theme.Dp(24);
        if (Logo != null)
        {
            int s = Theme.Dp(36);
            g.DrawImage(Logo, new Rectangle(x, (Height - s) / 2, s, s));
            x += s + Theme.Dp(14);
        }
        int mid = Height / 2;
        Theme.Text(g, "DPI Override", Theme.Title, new Rectangle(x, mid - Theme.Dp(24), Width - x - Theme.Dp(80), Theme.Dp(28)), Theme.OnPrimary);
        Theme.Text(g, "High-DPI scaling override for all users", Theme.Subtitle,
            new Rectangle(x, mid + Theme.Dp(2), Width - x - Theme.Dp(80), Theme.Dp(20)), Theme.InverseOnSurface);
    }
}

class Snackbar : Control
{
    readonly Timer timer = new Timer();
    bool error;
    public Snackbar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Visible = false;
        timer.Interval = 4500;
        timer.Tick += (s, e) => { timer.Stop(); Visible = false; };
        Cursor = Cursors.Hand;
    }
    protected override void OnClick(EventArgs e) { Visible = false; base.OnClick(e); }

    public void Show(string text, bool isError, Rectangle area)
    {
        Text = text; error = isError;
        int w = Math.Min(area.Width - Theme.Dp(32), Theme.Measure(text, Theme.Body) + Theme.Dp(72));
        int h = Theme.Dp(48);
        Bounds = new Rectangle(area.X + (area.Width - w) / 2, area.Bottom - h - Theme.Dp(16), w, h);
        using (var p = Theme.Round(new RectangleF(0, 0, w, h), Theme.Dp(6))) Region = new Region(p);
        Visible = true;
        BringToFront();
        Invalidate();
        timer.Stop(); timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        g.Clear(Theme.InverseSurface);
        Color ic = error ? Color.FromArgb(255, 0xF2, 0xB8, 0xB5) : Theme.InversePrimary;
        Theme.Glyph(g, error ? Theme.GError : Theme.GDone, Theme.Icon, new Rectangle(Theme.Dp(14), 0, Theme.Dp(24), Height), ic);
        int x = Theme.HasIcons ? Theme.Dp(48) : Theme.Dp(16);
        Theme.Text(g, Text, Theme.Body, new Rectangle(x, 0, Width - x - Theme.Dp(12), Height), Theme.InverseOnSurface);
    }
}

class MainForm : Form
{
    readonly AppBar appBar = new AppBar();
    readonly IconButton btnRefresh = new IconButton();
    readonly DbPanel toolbar = new DbPanel();
    readonly SearchBox search = new SearchBox();
    readonly Chip chipHide = new Chip();
    readonly Chip chipOverride = new Chip();
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
    readonly Dictionary<string, string> descCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    readonly string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(new[] { '\\' }) + "\\";
    List<Entry> entries = new List<Entry>();
    int shownCount;
    int sortCol = 0; bool sortAsc = true;
    int hoverIndex = -1;

    static readonly string[] ColNames = { "PROGRAM", "ALL USERS", "CURRENT USER", "RUNNING", "LOCATION" };
    static readonly string[] ModeKeys = { "Application", "System", "System (Enhanced)", "Not set" };

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static int Dp(float v) { return Theme.Dp(v); }

    public MainForm()
    {
        float scale;
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        Theme.Init(scale);

        Text = "DPI Override";
        Font = Theme.Body;
        BackColor = Theme.Surface;
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
        appBar.Height = Dp(72);
        btnRefresh.Glyph = Theme.GRefresh; btnRefresh.Text = "R";
        btnRefresh.Fore = Theme.OnPrimary;
        btnRefresh.Size = new Size(Dp(44), Dp(44));
        btnRefresh.Click += (s, e) => Reload();
        appBar.Controls.Add(btnRefresh);
        appBar.Resize += (s, e) => btnRefresh.Location = new Point(appBar.Width - btnRefresh.Width - Dp(16), (appBar.Height - btnRefresh.Height) / 2);
        new ToolTip().SetToolTip(btnRefresh, "Refresh (F5)");

        // Toolbar
        toolbar.Dock = DockStyle.Top;
        toolbar.Height = Dp(68);
        toolbar.BackColor = Theme.Surface;
        search.Box.TextChanged += (s, e) => FillList();
        chipHide.Text = "Hide Windows processes"; chipHide.Checked = true;
        chipHide.CheckedChanged += (s, e) => FillList();
        chipOverride.Text = "Only with override";
        chipOverride.CheckedChanged += (s, e) => FillList();
        btnAdd.Kind = ButtonKind.Tonal; btnAdd.Text = "Add program"; btnAdd.Glyph = Theme.GAdd;
        btnAdd.Click += (s, e) => Browse();
        new ToolTip().SetToolTip(btnAdd, "Pick an .exe that isn't running");
        toolbar.Controls.AddRange(new Control[] { search, chipHide, chipOverride, btnAdd });
        toolbar.Resize += (s, e) => LayoutToolbar();
        toolbar.Paint += PaintToolbar;

        // Content card: header + list
        content.Dock = DockStyle.Fill;
        content.BackColor = Theme.Surface;
        content.Padding = new Padding(Dp(28), Dp(10), Dp(28), Dp(20));
        content.Paint += PaintCard;
        content.Resize += (s, e) => { content.Invalidate(); PlaceEmpty(); };

        header.Dock = DockStyle.Top;
        header.Height = Dp(40);
        header.BackColor = Theme.Card;
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
        list.BackColor = Theme.Card;
        list.SmallImageList = new ImageList { ImageSize = new Size(1, Dp(56)) }; // row height
        for (int i = 0; i < ColNames.Length; i++) list.Columns.Add(ColNames[i], 100);
        list.DrawItem += DrawRow;
        list.DrawSubItem += (s, e) => { e.DrawDefault = false; };
        list.DrawColumnHeader += (s, e) => { e.DrawDefault = true; };
        list.Resize += (s, e) => LayoutColumns();
        list.MouseMove += ListMouseMove;
        list.MouseLeave += (s, e) => SetHover(-1);
        list.SelectedIndexChanged += (s, e) => UpdateSelectionUi();
        list.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.A) { list.BeginUpdate(); foreach (ListViewItem it in list.Items) it.Selected = true; list.EndUpdate(); e.Handled = true; }
        };

        empty.Text = "No programs match your search or filters";
        empty.ForeColor = Theme.OnSurfaceVariant;
        empty.BackColor = Theme.Card;
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
        bottom.BackColor = Theme.SurfaceContainer;
        bottom.Paint += PaintBottom;
        modes.Items = new[] { "Application", "System", "System (Enhanced)", "Off" };
        modes.Selected = 0;
        modes.SelectedChanged += (s, e) => bottom.Invalidate();
        btnApply.Kind = ButtonKind.Filled; btnApply.Text = "Apply to selected"; btnApply.Glyph = Theme.GCheck;
        btnApply.Click += (s, e) => Apply();
        btnApply.Enabled = false;
        bottom.Controls.AddRange(new Control[] { modes, btnApply });
        bottom.Resize += (s, e) => LayoutBottom();

        Controls.Add(content);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(appBar);
        Controls.Add(snack);

        LayoutToolbar(); LayoutBottom();
        Shown += (s, e) => { Reload(); list.Focus(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int cap = Theme.Primary.R | (Theme.Primary.G << 8) | (Theme.Primary.B << 16);
            int txt = 0xFFFFFF;
            DwmSetWindowAttribute(Handle, 35 /* DWMWA_CAPTION_COLOR */, ref cap, 4);
            DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref cap, 4);
            DwmSetWindowAttribute(Handle, 36 /* DWMWA_TEXT_COLOR */, ref txt, 4);
        }
        catch { }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5) { Reload(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.F) { search.Box.Focus(); search.Box.SelectAll(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ---------- Layout ----------
    void LayoutToolbar()
    {
        int h = Dp(40), y = (toolbar.Height - h) / 2 + Dp(2);
        int x = Dp(28);
        int addW = btnAdd.PreferredWidth();
        btnAdd.Bounds = new Rectangle(toolbar.Width - Dp(28) - addW, y, addW, h);
        int searchW = Math.Max(Dp(200), Math.Min(Dp(360), toolbar.Width - Dp(28) * 2 - addW - Dp(480)));
        search.Bounds = new Rectangle(x, y, searchW, h);
        x += searchW + Dp(16);
        int ch = Dp(32), cy = y + (h - ch) / 2;
        chipHide.Bounds = new Rectangle(x, cy, chipHide.PreferredWidth(), ch);
        x += chipHide.Width + Dp(8);
        chipOverride.Bounds = new Rectangle(x, cy, chipOverride.PreferredWidth(), ch);
        toolbar.Invalidate();
    }

    void LayoutBottom()
    {
        int h = Dp(40), y = (bottom.Height - h) / 2;
        int applyW = btnApply.PreferredWidth();
        btnApply.Bounds = new Rectangle(bottom.Width - Dp(28) - applyW, y, applyW, h);
        int segW = modes.PreferredWidth();
        modes.Bounds = new Rectangle(btnApply.Left - Dp(24) - segW, y, segW, h);
        bottom.Invalidate();
    }

    int[] ColumnWidths(int total)
    {
        int c1 = Dp(150), c2 = Dp(150), c3 = Dp(110);
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
            Theme.FillRound(g, Color.FromArgb(10 + (3 - i) * 4, 60, 40, 100), sr, rad + i);
        }
        Theme.FillRound(g, Theme.Card, r, rad);
        Theme.StrokeRound(g, Theme.Divider, 1f, r, rad);
    }

    void PaintToolbar(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        string count = shownCount == 1 ? "1 program" : shownCount + " programs";
        int w = Theme.Measure(count, Theme.Small) + Dp(8);
        int right = btnAdd.Left - Dp(16);
        int left = chipOverride.Right + Dp(16);
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
        int n = list.SelectedItems.Count;
        string title = n == 0 ? "Select programs in the list" : n == 1 ? "1 program selected" : n + " programs selected";
        string sub = "Scaling performed by: " + ModeDescription() + "  ·  restart the program afterwards";
        int x = Dp(28), w = modes.Left - x - Dp(16);
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
        var en = (Entry)e.Item.Tag;
        var r = e.Bounds;
        r.Width = Math.Max(r.Width, list.ClientSize.Width);
        using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);

        bool sel = e.Item.Selected, hov = e.ItemIndex == hoverIndex;
        var hr = new RectangleF(r.X + Dp(6), r.Y + Dp(2), r.Width - Dp(12), r.Height - Dp(4));
        if (sel) Theme.FillRound(g, Theme.SecondaryContainer, hr, Dp(12));
        else if (hov) Theme.FillRound(g, Theme.Mix(Theme.Card, Theme.OnSurface, 0.05f), hr, Dp(12));
        else if (e.ItemIndex < list.Items.Count - 1)
            using (var pen = new Pen(Theme.Divider, 1f)) g.DrawLine(pen, r.X + Dp(64), r.Bottom - 1, r.Right - Dp(16), r.Bottom - 1);

        var w = ColumnWidths(list.ClientSize.Width);
        int x = r.X;
        // Column 0: icon + name + exe
        int icon = Dp(32);
        var ir = new Rectangle(x + Dp(18), r.Y + (r.Height - icon) / 2, icon, icon);
        var bmp = GetIcon(en.Path);
        if (bmp != null) g.DrawImage(bmp, ir);
        else
        {
            Theme.FillRound(g, Theme.PrimaryContainer, ir, Dp(8));
            Theme.Text(g, en.Display.Length > 0 ? en.Display.Substring(0, 1).ToUpperInvariant() : "?", Theme.BodyStrong, ir, Theme.OnPrimaryContainer, TextFormatFlags.HorizontalCenter);
        }
        int tx = ir.Right + Dp(14), tw = w[0] - (tx - x) - Dp(8);
        int mid = r.Y + r.Height / 2;
        Theme.Text(g, en.Display, Theme.BodyStrong, new Rectangle(tx, mid - Dp(21), tw, Dp(22)), Theme.OnSurface);
        Theme.Text(g, en.Name, Theme.Small, new Rectangle(tx, mid + Dp(1), tw, Dp(19)), Theme.OnSurfaceVariant);
        x += w[0];

        DrawModeChip(g, en.AllUsers, new Rectangle(x + Dp(8), r.Y, w[1] - Dp(16), r.Height));
        x += w[1];
        DrawModeChip(g, en.CurrentUser, new Rectangle(x + Dp(8), r.Y, w[2] - Dp(16), r.Height));
        x += w[2];

        if (en.Running)
        {
            int d = Dp(8);
            using (var b = new SolidBrush(Theme.Running)) g.FillEllipse(b, x + Dp(8), mid - d / 2, d, d);
            Theme.Text(g, "Running", Theme.Small, new Rectangle(x + Dp(22), r.Y, w[3] - Dp(26), r.Height), Theme.OnSurfaceVariant);
        }
        else Theme.Text(g, "—", Theme.Small, new Rectangle(x + Dp(8), r.Y, w[3] - Dp(12), r.Height), Theme.OutlineVariant);
        x += w[3];

        string dir = "";
        try { dir = Path.GetDirectoryName(en.Path) ?? en.Path; } catch { dir = en.Path; }
        Theme.Text(g, dir, Theme.Small, new Rectangle(x + Dp(8), r.Y, w[4] - Dp(24), r.Height), Theme.OnSurfaceVariant, TextFormatFlags.PathEllipsis);
    }

    void DrawModeChip(Graphics g, string mode, Rectangle cell)
    {
        Color bg, fg;
        Theme.ModeColors(mode, out bg, out fg);
        if (mode == "Not set")
        {
            Theme.Text(g, "Not set", Theme.Small, cell, Theme.Outline);
            return;
        }
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
        btnApply.Enabled = list.SelectedItems.Count > 0;
        bottom.Invalidate();
    }

    // ---------- Data ----------
    string Describe(string path)
    {
        string d;
        if (descCache.TryGetValue(path, out d)) return d;
        d = null;
        try { if (File.Exists(path)) d = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { }
        if (string.IsNullOrWhiteSpace(d)) d = Path.GetFileNameWithoutExtension(path);
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

    void Reload()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            var running = Procs.RunningPaths();
            var lm = Compat.ReadAll(RegistryHive.LocalMachine);
            var cu = Compat.ReadAll(RegistryHive.CurrentUser);

            var paths = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in lm) if (Compat.ModeOf(kv.Value) != "Not set") paths.Add(kv.Key);
            foreach (var kv in cu) if (Compat.ModeOf(kv.Value) != "Not set") paths.Add(kv.Key);
            paths.UnionWith(extra);

            entries = paths.Select(p =>
            {
                string a, c;
                lm.TryGetValue(p, out a); cu.TryGetValue(p, out c);
                return new Entry
                {
                    Path = p,
                    Name = Path.GetFileName(p),
                    Display = Describe(p),
                    AllUsers = Compat.ModeOf(a),
                    CurrentUser = Compat.ModeOf(c),
                    Running = running.Contains(p),
                    System = p.StartsWith(winDir, StringComparison.OrdinalIgnoreCase),
                };
            }).ToList();
            FillList();
            Program.Log("loaded " + entries.Count + " entries");
        }
        catch (Exception ex)
        {
            Program.Log("reload failed: " + ex);
            ShowSnack("Couldn't load the program list: " + ex.Message, true);
        }
        finally { Cursor = Cursors.Default; }
    }

    static string Col(Entry e, int c)
    {
        switch (c)
        {
            case 0: return e.Display;
            case 1: return e.AllUsers;
            case 2: return e.CurrentUser;
            case 3: return e.Running ? "0" : "1";
            default: return e.Path;
        }
    }

    void FillList(IEnumerable<string> select = null)
    {
        var sel = new HashSet<string>(select ?? list.SelectedItems.Cast<ListViewItem>().Select(i => ((Entry)i.Tag).Path),
                                      StringComparer.OrdinalIgnoreCase);
        var f = search.Box.Text.Trim();
        IEnumerable<Entry> rows = entries.Where(e =>
        {
            bool hasOverride = e.AllUsers != "Not set" || e.CurrentUser != "Not set";
            if (chipOverride.Checked && !hasOverride) return false;
            if (chipHide.Checked && e.System && !hasOverride && !sel.Contains(e.Path)) return false;
            if (f.Length > 0 && (e.Display + " " + e.Path).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        });
        rows = sortAsc ? rows.OrderBy(e => Col(e, sortCol), StringComparer.CurrentCultureIgnoreCase)
                       : rows.OrderByDescending(e => Col(e, sortCol), StringComparer.CurrentCultureIgnoreCase);

        hoverIndex = -1;
        list.BeginUpdate();
        list.Items.Clear();
        ListViewItem first = null;
        int n = 0;
        foreach (var e in rows)
        {
            var it = new ListViewItem(new[] { e.Display, e.AllUsers, e.CurrentUser, e.Running ? "Yes" : "No", e.Path }) { Tag = e };
            list.Items.Add(it);
            if (sel.Contains(e.Path)) { it.Selected = true; if (first == null) first = it; }
            n++;
        }
        list.EndUpdate();
        if (first != null) first.EnsureVisible();
        shownCount = n;
        empty.Visible = n == 0;
        PlaceEmpty();
        toolbar.Invalidate();
        UpdateSelectionUi();
    }

    void Browse()
    {
        using (var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            extra.Add(dlg.FileName);
            search.Box.Text = "";
            Reload();
            FillList(new[] { dlg.FileName });
        }
    }

    void ShowSnack(string text, bool error)
    {
        var area = new Rectangle(0, 0, ClientSize.Width, bottom.Top);
        snack.Show(text, error, area);
    }

    void Apply()
    {
        var items = list.SelectedItems.Cast<ListViewItem>().Select(i => (Entry)i.Tag).ToList();
        if (items.Count == 0) return;
        string mode = ModeKeys[modes.Selected];

        var errors = new List<string>();
        var done = new List<string>();
        foreach (var e in items)
        {
            try
            {
                Compat.Set(RegistryHive.LocalMachine, e.Path, mode);
                if (e.CurrentUser != "Not set") Compat.Set(RegistryHive.CurrentUser, e.Path, "Not set");
                done.Add(e.Display);
                Program.Log("set " + mode + " for " + e.Path);
            }
            catch (Exception ex)
            {
                errors.Add(e.Name + ": " + ex.Message);
                Program.Log("set failed for " + e.Path + ": " + ex);
            }
        }
        var paths = items.Select(i => i.Path).ToList();
        Reload();
        FillList(paths);

        if (errors.Count > 0)
        {
            ShowSnack(errors.Count == 1 ? "Couldn't change " + errors[0] : errors.Count + " changes failed", true);
            MessageBox.Show(this, string.Join("\n", errors), "Some changes failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else if (done.Count > 0)
        {
            string what = done.Count == 1 ? done[0] : done.Count + " programs";
            string m = mode == "Not set" ? "Override removed for " + what : modes.Items[modes.Selected] + " scaling set for " + what;
            ShowSnack(m + ". Restart to apply.", false);
        }
    }
}
