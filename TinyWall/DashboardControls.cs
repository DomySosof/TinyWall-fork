using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    internal readonly struct TrafficSample
    {
        internal readonly DateTime Time;
        internal readonly long Rx;
        internal readonly long Tx;

        internal TrafficSample(DateTime time, long rx, long tx)
        {
            Time = time;
            Rx = rx;
            Tx = tx;
        }
    }

    internal sealed class DashboardPalette
    {
        internal Color Background;
        internal Color HeaderBackground;
        internal Color Card;
        internal Color CardAlt;
        internal Color Border;
        internal Color Text;
        internal Color Muted;
        internal Color Green;
        internal Color Orange;
        internal Color Red;
        internal Color Blue;
        internal Color Yellow;
        internal Color ChartDownload;
        internal Color ChartUpload;
        internal Color GridLine;
        internal Color HoverLine;
        internal Color HoverBox;

        internal DashboardPalette Clone()
        {
            return (DashboardPalette)MemberwiseClone();
        }
    }

    internal static class DashboardTheme
    {
        private static readonly DashboardPalette DarkPalette = new()
        {
            Background = Color.FromArgb(15, 18, 24),
            HeaderBackground = Color.FromArgb(18, 22, 30),
            Card = Color.FromArgb(23, 27, 36),
            CardAlt = Color.FromArgb(30, 35, 46),
            Border = Color.FromArgb(39, 46, 60),
            Text = Color.FromArgb(232, 236, 244),
            Muted = Color.FromArgb(138, 148, 166),
            Green = Color.FromArgb(43, 217, 160),
            Orange = Color.FromArgb(255, 169, 77),
            Red = Color.FromArgb(255, 92, 108),
            Blue = Color.FromArgb(76, 154, 255),
            Yellow = Color.FromArgb(255, 210, 92),
            GridLine = Color.FromArgb(26, 255, 255, 255),
            HoverLine = Color.FromArgb(70, 255, 255, 255),
            HoverBox = Color.FromArgb(235, 10, 13, 18)
        };

        private static readonly DashboardPalette LightPalette = new()
        {
            Background = Color.FromArgb(242, 245, 249),
            HeaderBackground = Color.FromArgb(255, 255, 255),
            Card = Color.FromArgb(255, 255, 255),
            CardAlt = Color.FromArgb(237, 242, 247),
            Border = Color.FromArgb(213, 220, 230),
            Text = Color.FromArgb(27, 36, 48),
            Muted = Color.FromArgb(100, 116, 139),
            Green = Color.FromArgb(16, 159, 110),
            Orange = Color.FromArgb(217, 119, 6),
            Red = Color.FromArgb(217, 58, 74),
            Blue = Color.FromArgb(47, 123, 224),
            Yellow = Color.FromArgb(183, 134, 0),
            GridLine = Color.FromArgb(30, 0, 0, 0),
            HoverLine = Color.FromArgb(80, 0, 0, 0),
            HoverBox = Color.FromArgb(240, 255, 255, 255)
        };

        private static DashboardPalette current = DarkPalette;

        internal static bool IsDark { get; private set; } = true;

        internal static Color Background => current.Background;
        internal static Color HeaderBackground => current.HeaderBackground;
        internal static Color Card => current.Card;
        internal static Color CardAlt => current.CardAlt;
        internal static Color Border => current.Border;
        internal static Color Text => current.Text;
        internal static Color Muted => current.Muted;
        internal static Color Green => current.Green;
        internal static Color Orange => current.Orange;
        internal static Color Red => current.Red;
        internal static Color Yellow => current.Yellow;
        internal static Color Accent => current.Blue;
        internal static Color ChartDownload => current.ChartDownload;
        internal static Color ChartUpload => current.ChartUpload;
        internal static Color GridLine => current.GridLine;
        internal static Color HoverLine => current.HoverLine;
        internal static Color HoverBox => current.HoverBox;

        internal static readonly Font FontHeaderTitle = new("Segoe UI Semibold", 16F);
        internal static readonly Font FontCardTitle = new("Segoe UI Semibold", 9.75F);
        internal static readonly Font FontValue = new("Segoe UI Semibold", 15F);
        internal static readonly Font FontApp = new("Segoe UI Semibold", 9.75F);
        internal static readonly Font FontBody = new("Segoe UI", 9F);
        internal static readonly Font FontSmall = new("Segoe UI", 8.25F);

        internal static void Configure(ControllerSettings settings)
        {
            bool dark = Utils.IsDarkModeActive(settings);
            var palette = (dark ? DarkPalette : LightPalette).Clone();
            palette.ChartDownload = ParseColor(settings.ChartDownloadColor, palette.Green);
            palette.ChartUpload = ParseColor(settings.ChartUploadColor, palette.Orange);
            palette.Blue = ParseColor(settings.ChartAccentColor, palette.Blue);
            current = palette;
            IsDark = dark;
        }

        internal static Color ResolveChartDownload(ControllerSettings settings)
        {
            var palette = Utils.IsDarkModeActive(settings) ? DarkPalette : LightPalette;
            return ParseColor(settings.ChartDownloadColor, palette.Green);
        }

        internal static Color ResolveChartUpload(ControllerSettings settings)
        {
            var palette = Utils.IsDarkModeActive(settings) ? DarkPalette : LightPalette;
            return ParseColor(settings.ChartUploadColor, palette.Orange);
        }

        internal static Color ResolveChartAccent(ControllerSettings settings)
        {
            var palette = Utils.IsDarkModeActive(settings) ? DarkPalette : LightPalette;
            return ParseColor(settings.ChartAccentColor, palette.Blue);
        }

        internal static Color ParseColor(string? value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            try
            {
                return ColorTranslator.FromHtml(value);
            }
            catch
            {
                return fallback;
            }
        }

        internal static string ToHtml(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        internal static Color ContrastText(Color background)
        {
            double luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
            return (luminance > 0.6) ? Color.FromArgb(20, 24, 32) : Color.White;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        internal static void ApplyWindowChrome(Form form)
        {
            if (!form.IsHandleCreated)
                return;

            try
            {
                int dark = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));

                // Caption color on Windows 11
                int caption = IsDark ? 0x18120F : 0xFFFFFF;
                DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            }
            catch { }
        }

        internal static int S(double value)
        {
            return (int)Math.Round(value * Utils.DpiScalingFactor);
        }

        internal static float FS(double value)
        {
            return (float)(value * Utils.DpiScalingFactor);
        }

        internal static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            if ((bounds.Width <= 1) || (bounds.Height <= 1))
            {
                path.AddRectangle(bounds);
                return path;
            }

            radius = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
            if (radius < 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            float d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void PaintCard(Graphics g, Rectangle bounds, float radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1f, bounds.Height - 1f);
            using var path = RoundedRect(rect, radius);
            using var fill = new SolidBrush(Card);
            g.FillPath(fill, path);
            using var border = new Pen(Border);
            g.DrawPath(border, path);
        }
    }

    internal static class DashboardFormat
    {
        internal static string Bytes(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while ((value >= 1024) && (unit < units.Length - 1))
            {
                value /= 1024;
                unit++;
            }

            string fmt = (unit == 0) ? "{0:N0} {1}" : "{0:0.#} {1}";
            return string.Format(CultureInfo.CurrentCulture, fmt, value, units[unit]);
        }

        internal static string Rate(long bytesPerSecond)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0}/s", Bytes(bytesPerSecond));
        }
    }

    internal sealed class DashboardCard : Panel
    {
        private readonly List<Control> headerControls = new();
        private readonly string title;

        internal void ApplyAppearance()
        {
            BackColor = DashboardTheme.Background;
            ForeColor = DashboardTheme.Text;
            Invalidate();
        }

        internal DashboardCard(string title)
        {
            this.title = title;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DashboardTheme.Background;
            ForeColor = DashboardTheme.Text;
            Padding = new Padding(DashboardTheme.S(1), DashboardTheme.S(40), DashboardTheme.S(1), DashboardTheme.S(1));
        }

        internal void AddHeaderControl(Control control)
        {
            headerControls.Add(control);
            Controls.Add(control);
            LayoutHeaderControls();
            control.BringToFront();
        }

        private void LayoutHeaderControls()
        {
            if (headerControls.Count == 0)
                return;

            int gap = DashboardTheme.S(6);
            int total = 0;
            foreach (var c in headerControls)
                total += c.Width;

            total += gap * (headerControls.Count - 1);

            int x = Width - DashboardTheme.S(14) - total;
            int y = DashboardTheme.S(9);
            foreach (var c in headerControls)
            {
                c.Location = new Point(x, y);
                x += c.Width + gap;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutHeaderControls();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(DashboardTheme.Background);
            DashboardTheme.PaintCard(e.Graphics, ClientRectangle, DashboardTheme.S(8));

            // Keep the title clear of the header buttons
            int availableWidth = Width - DashboardTheme.S(28);
            foreach (var c in headerControls)
                availableWidth -= c.Width + DashboardTheme.S(6);

            var titleRect = new Rectangle(DashboardTheme.S(16), DashboardTheme.S(9),
                Math.Max(0, availableWidth), DashboardTheme.S(22));
            TextRenderer.DrawText(e.Graphics, title, DashboardTheme.FontCardTitle, titleRect,
                DashboardTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class DashboardFlatButton : Control
    {
        private bool hovered;
        private bool pressed;
        private Bitmap? tintedIcon;

        internal bool Selected;
        internal bool Filled;
        internal Color Accent = DashboardTheme.Accent;
        internal Image? IconImage;
        internal Color SurfaceColor = DashboardTheme.Card;

        internal DashboardFlatButton(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SurfaceColor;
            ForeColor = DashboardTheme.Text;
            Font = DashboardTheme.FontSmall;
            Height = DashboardTheme.S(28);
            Text = text;
            Cursor = Cursors.Hand;
            FitToText();
        }

        internal void ApplySurfaceColor(Color color)
        {
            SurfaceColor = color;
            BackColor = color;
            Invalidate();
        }

        internal void ApplyAppearance(Color surfaceColor)
        {
            SurfaceColor = surfaceColor;
            BackColor = surfaceColor;
            ForeColor = DashboardTheme.Text;
            Accent = DashboardTheme.Accent;
            tintedIcon?.Dispose();
            tintedIcon = null;
            Invalidate();
        }

        internal void FitToText()
        {
            int textWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            int iconWidth = (IconImage is null) ? 0 : DashboardTheme.S(16) + DashboardTheme.S(6);
            Width = textWidth + iconWidth + DashboardTheme.S(24);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (pressed)
            {
                pressed = false;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SurfaceColor);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using var path = DashboardTheme.RoundedRect(rect, DashboardTheme.S(6));

            Color fill;
            if (Filled)
                fill = pressed ? ControlPaint.Dark(Accent, 0.05f) : hovered ? ControlPaint.Light(Accent, 0.05f) : Accent;
            else if (Selected)
                fill = Color.FromArgb(38, Accent);
            else if (hovered)
                fill = DashboardTheme.CardAlt;
            else
                fill = Color.Empty;

            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }

            if (!Filled && (Selected || hovered))
            {
                using var pen = new Pen(Selected ? Accent : DashboardTheme.Border);
                g.DrawPath(pen, path);
            }

            Color textColor = Filled ? DashboardTheme.ContrastText(Accent) : Selected ? Accent : DashboardTheme.Text;
            int x = DashboardTheme.S(12);
            Image? icon = GetIcon();
            if (icon is not null)
            {
                int iconSize = DashboardTheme.S(16);
                var iconRect = new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, iconRect);
                x += iconSize + DashboardTheme.S(6);
            }

            var textRect = new Rectangle(x, 0, Math.Max(0, Width - x - DashboardTheme.S(8)), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private Image? GetIcon()
        {
            if (IconImage is null)
                return null;

            if (tintedIcon is null)
                tintedIcon = TintIcon(IconImage, Filled ? DashboardTheme.ContrastText(Accent) : DashboardTheme.Muted);

            return tintedIcon ?? IconImage;
        }

        private static Bitmap? TintIcon(Image source, Color color)
        {
            try
            {
                var bitmap = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                    g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));

                for (int y = 0; y < bitmap.Height; ++y)
                {
                    for (int x = 0; x < bitmap.Width; ++x)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.A > 0)
                            bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, color));
                    }
                }

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                tintedIcon?.Dispose();

            base.Dispose(disposing);
        }
    }

    internal sealed class DashboardTile : Control
    {
        private readonly string title;
        private string value = Resources.Dashboard.TileNoValue;
        private string detail = string.Empty;
        private Color accent;

        internal DashboardTile(string title, Color accent)
        {
            this.title = title;
            this.accent = accent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DashboardTheme.Background;
            ForeColor = DashboardTheme.Text;
        }

        internal void ApplyAppearance(Color accentColor)
        {
            accent = accentColor;
            BackColor = DashboardTheme.Background;
            ForeColor = DashboardTheme.Text;
            Invalidate();
        }

        internal void SetValues(string value, string detail)
        {
            if ((value == this.value) && (detail == this.detail))
                return;

            this.value = value;
            this.detail = detail;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(DashboardTheme.Background);
            DashboardTheme.PaintCard(g, ClientRectangle, DashboardTheme.S(8));

            int left = DashboardTheme.S(24);
            int width = Math.Max(0, Width - left - DashboardTheme.S(12));

            int pad = DashboardTheme.S(14);
            int titleHeight = DashboardTheme.S(16);
            int valueHeight = DashboardTheme.S(30);
            int detailHeight = (detail.Length > 0) ? DashboardTheme.S(16) : 0;
            int blockHeight = valueHeight + detailHeight;
            int titleBottom = pad + titleHeight;
            int blockTop = titleBottom + Math.Max(0, (Height - pad - titleBottom - blockHeight) / 2);

            TextRenderer.DrawText(g, title, DashboardTheme.FontSmall,
                new Rectangle(left, pad, width, titleHeight),
                DashboardTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            using (var accentBrush = new SolidBrush(accent))
                g.FillRectangle(accentBrush, DashboardTheme.S(12), blockTop, DashboardTheme.S(3), Math.Max(DashboardTheme.S(6), blockHeight));

            TextRenderer.DrawText(g, value, DashboardTheme.FontValue,
                new Rectangle(left, blockTop, width, valueHeight),
                DashboardTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (detailHeight > 0)
            {
                TextRenderer.DrawText(g, detail, DashboardTheme.FontSmall,
                    new Rectangle(left, blockTop + valueHeight, width, detailHeight),
                    DashboardTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    internal sealed class DashboardHeader : Control
    {
        private readonly List<Control> actions = new();
        private string modeText = string.Empty;
        private Color modeColor = DashboardTheme.Muted;
        private Image? modeIcon;
        private FirewallMode lastMode = FirewallMode.Unknown;
        private bool hasState;
        private bool locked;

        internal DashboardHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DashboardTheme.HeaderBackground;
            ForeColor = DashboardTheme.Text;
            Height = DashboardTheme.S(86);
        }

        internal void AddAction(Control control)
        {
            actions.Add(control);
            Controls.Add(control);
            LayoutActions();
            control.BringToFront();
        }

        internal void ApplyAppearance()
        {
            BackColor = DashboardTheme.HeaderBackground;
            ForeColor = DashboardTheme.Text;

            if (hasState)
            {
                hasState = false;
                SetState(lastMode, locked);
            }

            Invalidate();
        }

        internal void SetState(FirewallMode mode, bool isLocked)
        {
            if (hasState && (lastMode == mode) && (locked == isLocked))
                return;

            hasState = true;
            locked = isLocked;
            lastMode = mode;

            Color color;
            string text;
            Image icon;
            switch (mode)
            {
                case FirewallMode.Normal:
                    text = Resources.Messages.FirewallModeNormal;
                    color = DashboardTheme.Green;
                    icon = Resources.Icons.shield_green;
                    break;
                case FirewallMode.AllowOutgoing:
                    text = Resources.Messages.FirewallModeAllowOut;
                    color = DashboardTheme.Red;
                    icon = Resources.Icons.shield_red;
                    break;
                case FirewallMode.BlockAll:
                    text = Resources.Messages.FirewallModeBlockAll;
                    color = DashboardTheme.Yellow;
                    icon = Resources.Icons.shield_yellow;
                    break;
                case FirewallMode.Disabled:
                    text = Resources.Messages.FirewallModeDisabled;
                    color = DashboardTheme.Muted;
                    icon = Resources.Icons.shield_grey;
                    break;
                case FirewallMode.Learning:
                    text = Resources.Messages.FirewallModeLearn;
                    color = DashboardTheme.Accent;
                    icon = Resources.Icons.shield_blue;
                    break;
                default:
                    text = Resources.Messages.FirewallModeUnknown;
                    color = DashboardTheme.Muted;
                    icon = Resources.Icons.shield_grey;
                    break;
            }

            modeText = text;
            modeColor = color;
            modeIcon = icon;
            Invalidate();
        }

        private void LayoutActions()
        {
            int right = Width - DashboardTheme.S(20);
            foreach (var action in actions)
            {
                action.Location = new Point(right - action.Width, (Height - action.Height) / 2);
                right = action.Location.X - DashboardTheme.S(8);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutActions();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(DashboardTheme.HeaderBackground);
            using (var border = new Pen(DashboardTheme.Border))
                g.DrawLine(border, 0, Height - 1, Width, Height - 1);

            int x = DashboardTheme.S(22);
            int iconSize = DashboardTheme.S(44);
            int iconY = (Height - iconSize) / 2;
            if (modeIcon is not null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(modeIcon, new Rectangle(x, iconY, iconSize, iconSize));
            }
            x += iconSize + DashboardTheme.S(14);

            TextRenderer.DrawText(g, "TinyWall", DashboardTheme.FontHeaderTitle,
                new Rectangle(x, DashboardTheme.S(18), DashboardTheme.S(220), DashboardTheme.S(30)),
                DashboardTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, Resources.Dashboard.Subtitle, DashboardTheme.FontSmall,
                new Rectangle(x, DashboardTheme.S(48), DashboardTheme.S(300), DashboardTheme.S(18)),
                DashboardTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int pillX = x + TextRenderer.MeasureText("TinyWall", DashboardTheme.FontHeaderTitle).Width + DashboardTheme.S(16);
            int pillHeight = DashboardTheme.S(26);
            int pillY = (Height - pillHeight) / 2;

            if (modeText.Length > 0)
                pillX = DrawPill(g, pillX, pillY, pillHeight, modeText, modeColor);

            if (locked)
                DrawPill(g, pillX, pillY, pillHeight, Resources.Dashboard.Locked, DashboardTheme.Red);
        }

        private static int DrawPill(Graphics g, int x, int y, int height, string text, Color color)
        {
            int textWidth = TextRenderer.MeasureText(text, DashboardTheme.FontSmall).Width;
            int width = textWidth + DashboardTheme.S(22);
            var rect = new RectangleF(x + 0.5f, y + 0.5f, width - 1f, height - 1f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = DashboardTheme.RoundedRect(rect, height / 2f))
            {
                using (var fill = new SolidBrush(Color.FromArgb(28, color)))
                    g.FillPath(fill, path);
                using (var border = new Pen(Color.FromArgb(90, color)))
                    g.DrawPath(border, path);
            }

            TextRenderer.DrawText(g, text, DashboardTheme.FontSmall,
                new Rectangle(x, y, width, height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            return x + width + DashboardTheme.S(8);
        }
    }

    internal sealed class TrafficChart : Control
    {
        private IReadOnlyList<TrafficSample> samples = Array.Empty<TrafficSample>();
        private TimeSpan window = TimeSpan.FromMinutes(5);
        private double[] lastRx = Array.Empty<double>();
        private double[] lastTx = Array.Empty<double>();
        private double lastMaxValue = 1;
        private int hoverBucket = -1;
        private int firstBucketIndex;
        private Rectangle lastPlot;

        internal TrafficChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DashboardTheme.Card;
            ForeColor = DashboardTheme.Text;
        }

        internal void ApplyAppearance()
        {
            BackColor = DashboardTheme.Card;
            ForeColor = DashboardTheme.Text;
            Invalidate();
        }

        internal void SetData(IReadOnlyList<TrafficSample> data, TimeSpan timeWindow)
        {
            samples = data;
            window = timeWindow;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int bucket = BucketFromX(e.X);
            if (bucket != hoverBucket)
            {
                hoverBucket = bucket;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverBucket != -1)
            {
                hoverBucket = -1;
                Invalidate();
            }
        }

        private int BucketFromX(int x)
        {
            if ((lastRx.Length == 0) || (lastPlot.Width <= 0) || (x < lastPlot.Left) || (x > lastPlot.Right))
                return -1;

            double fraction = (double)(x - lastPlot.Left) / lastPlot.Width;
            int bucket = (int)Math.Round(fraction * (lastRx.Length - 1));
            return Math.Max(0, Math.Min(lastRx.Length - 1, bucket));
        }

        private static double NiceCeiling(double value)
        {
            if (value <= 1)
                return 1;

            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double normalized = value / magnitude;
            double step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
            return step * magnitude;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var plot = new Rectangle(DashboardTheme.S(12), DashboardTheme.S(30),
                Math.Max(0, Width - DashboardTheme.S(24)), Math.Max(0, Height - DashboardTheme.S(54)));
            lastPlot = plot;
            if ((plot.Width < 20) || (plot.Height < 20))
                return;

            DrawLegend(g);

            if (samples.Count == 0)
            {
                lastRx = Array.Empty<double>();
                lastTx = Array.Empty<double>();
                firstBucketIndex = 0;
                TextRenderer.DrawText(g, Resources.Dashboard.WaitingForData, DashboardTheme.FontBody,
                    plot, DashboardTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            DateTime now = samples[samples.Count - 1].Time;
            int bucketCount = Math.Max(16, Math.Min(400, plot.Width / 2));
            BuildSeries(now, bucketCount, out double[] rx, out double[] tx, out int firstBucket);
            lastRx = rx;
            lastTx = tx;
            firstBucketIndex = firstBucket;

            double peak = 0;
            for (int i = 0; i < bucketCount; ++i)
                peak = Math.Max(peak, Math.Max(rx[i], tx[i]));
            double maxValue = NiceCeiling(peak);
            lastMaxValue = maxValue;

            DrawGrid(g, plot, maxValue);

            PointF[] rxPoints = BuildPoints(plot, rx, maxValue);
            PointF[] txPoints = BuildPoints(plot, tx, maxValue);
            if (firstBucket > 0)
            {
                rxPoints = SlicePoints(rxPoints, firstBucket);
                txPoints = SlicePoints(txPoints, firstBucket);
            }

            DrawSeries(g, plot, rxPoints, DashboardTheme.ChartDownload);
            DrawSeries(g, plot, txPoints, DashboardTheme.ChartUpload);

            DrawAxisLabels(g, plot, now, maxValue);
            DrawHover(g, plot, now);
        }

        private static PointF[] SlicePoints(PointF[] points, int from)
        {
            if ((from <= 0) || (from >= points.Length))
                return points;

            var sliced = new PointF[points.Length - from];
            Array.Copy(points, from, sliced, 0, sliced.Length);
            return sliced;
        }

        private static PointF[] BuildPoints(Rectangle plot, double[] values, double maxValue)
        {
            var points = new PointF[values.Length];
            float stepX = (values.Length > 1) ? (float)plot.Width / (values.Length - 1) : 0;
            for (int i = 0; i < values.Length; ++i)
            {
                float y = plot.Bottom - (float)(values[i] / maxValue * plot.Height);
                points[i] = new PointF(plot.Left + (i * stepX), y);
            }

            return points;
        }

        private static void DrawSeries(Graphics g, Rectangle plot, PointF[] points, Color color)
        {
            if (points.Length < 2)
                return;

            using (var area = new GraphicsPath())
            {
                area.AddLines(points);
                area.AddLine(points[points.Length - 1].X, plot.Bottom, points[0].X, plot.Bottom);
                area.CloseFigure();

                using var gradient = new LinearGradientBrush(
                    new Rectangle(plot.Left, plot.Top, Math.Max(1, plot.Width), Math.Max(1, plot.Height)),
                    Color.FromArgb(95, color), Color.FromArgb(0, color), LinearGradientMode.Vertical);
                g.FillPath(gradient, area);
            }

            using (var pen = new Pen(color, DashboardTheme.FS(1.6)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, points);
            }
        }

        private static void DrawGrid(Graphics g, Rectangle plot, double maxValue)
        {
            using var gridPen = new Pen(DashboardTheme.GridLine) { DashStyle = DashStyle.Dot };
            for (int i = 0; i <= 4; ++i)
            {
                int y = plot.Top + (plot.Height * i / 4);
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            }
        }

        private void DrawAxisLabels(Graphics g, Rectangle plot, DateTime now, double maxValue)
        {
            TextRenderer.DrawText(g, DashboardFormat.Rate((long)maxValue), DashboardTheme.FontSmall,
                new Rectangle(plot.Right - DashboardTheme.S(160), DashboardTheme.S(10), DashboardTheme.S(160), DashboardTheme.S(16)),
                DashboardTheme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int labelHeight = DashboardTheme.S(16);
            int y = plot.Bottom + DashboardTheme.S(4);
            string pastLabel = string.Format(CultureInfo.CurrentCulture, "-{0} min", Math.Round(window.TotalMinutes));
            if (window >= TimeSpan.FromHours(1))
                pastLabel = string.Format(CultureInfo.CurrentCulture, "-{0} h", Math.Round(window.TotalHours));

            TextRenderer.DrawText(g, pastLabel, DashboardTheme.FontSmall,
                new Rectangle(plot.Left, y, DashboardTheme.S(100), labelHeight), DashboardTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, Resources.Dashboard.Now, DashboardTheme.FontSmall,
                new Rectangle(plot.Right - DashboardTheme.S(100), y, DashboardTheme.S(100), labelHeight), DashboardTheme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static void DrawLegend(Graphics g)
        {
            int x = DashboardTheme.S(12);
            int y = DashboardTheme.S(10);
            int dot = DashboardTheme.S(8);
            int textHeight = DashboardTheme.S(16);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var inBrush = new SolidBrush(DashboardTheme.ChartDownload))
                g.FillEllipse(inBrush, x, y + (textHeight - dot) / 2, dot, dot);
            x += dot + DashboardTheme.S(6);
            TextRenderer.DrawText(g, Resources.Dashboard.LegendIn, DashboardTheme.FontSmall,
                new Rectangle(x, y, DashboardTheme.S(120), textHeight), DashboardTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x += TextRenderer.MeasureText(Resources.Dashboard.LegendIn, DashboardTheme.FontSmall).Width + DashboardTheme.S(16);

            using (var outBrush = new SolidBrush(DashboardTheme.ChartUpload))
                g.FillEllipse(outBrush, x, y + (textHeight - dot) / 2, dot, dot);
            x += dot + DashboardTheme.S(6);
            TextRenderer.DrawText(g, Resources.Dashboard.LegendOut, DashboardTheme.FontSmall,
                new Rectangle(x, y, DashboardTheme.S(120), textHeight), DashboardTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawHover(Graphics g, Rectangle plot, DateTime now)
        {
            if ((hoverBucket < 0) || (hoverBucket >= lastRx.Length) || (hoverBucket < firstBucketIndex))
                return;

            float stepX = (lastRx.Length > 1) ? (float)plot.Width / (lastRx.Length - 1) : 0;
            int x = plot.Left + (int)Math.Round(hoverBucket * stepX);
            using (var line = new Pen(DashboardTheme.HoverLine) { DashStyle = DashStyle.Dash })
                g.DrawLine(line, x, plot.Top, x, plot.Bottom);

            double peak = Math.Max(1, Math.Max(lastRx[hoverBucket], lastTx[hoverBucket]));
            double maxValue = Math.Max(lastMaxValue, NiceCeiling(peak));

            int dot = DashboardTheme.S(6);
            int rxY = plot.Bottom - (int)(lastRx[hoverBucket] / maxValue * plot.Height);
            int txY = plot.Bottom - (int)(lastTx[hoverBucket] / maxValue * plot.Height);
            using (var rxBrush = new SolidBrush(DashboardTheme.ChartDownload))
                g.FillEllipse(rxBrush, x - dot / 2, rxY - dot / 2, dot, dot);
            using (var txBrush = new SolidBrush(DashboardTheme.ChartUpload))
                g.FillEllipse(txBrush, x - dot / 2, txY - dot / 2, dot, dot);

            DateTime time = now - TimeSpan.FromTicks((long)(window.Ticks * (1.0 - (double)hoverBucket / Math.Max(1, lastRx.Length - 1))));
            string line1 = string.Format(CultureInfo.CurrentCulture, "{0}  {1}", time.ToString("HH:mm:ss", CultureInfo.CurrentCulture), DashboardFormat.Rate((long)lastRx[hoverBucket]));
            string line2 = DashboardFormat.Rate((long)lastTx[hoverBucket]);

            int boxWidth = DashboardTheme.S(150);
            int boxHeight = DashboardTheme.S(46);
            int boxX = Math.Min(plot.Right - boxWidth, Math.Max(plot.Left, x + DashboardTheme.S(10)));
            int boxY = plot.Top + DashboardTheme.S(6);

            var box = new RectangleF(boxX + 0.5f, boxY + 0.5f, boxWidth - 1f, boxHeight - 1f);
            using (var path = DashboardTheme.RoundedRect(box, DashboardTheme.S(6)))
            {
                using (var fill = new SolidBrush(DashboardTheme.HoverBox))
                    g.FillPath(fill, path);
                using (var border = new Pen(DashboardTheme.Border))
                    g.DrawPath(border, path);
            }

            TextRenderer.DrawText(g, line1, DashboardTheme.FontSmall,
                new Rectangle(boxX + DashboardTheme.S(10), boxY + DashboardTheme.S(5), boxWidth - DashboardTheme.S(20), DashboardTheme.S(16)),
                DashboardTheme.ChartDownload, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, line2, DashboardTheme.FontSmall,
                new Rectangle(boxX + DashboardTheme.S(10), boxY + DashboardTheme.S(23), boxWidth - DashboardTheme.S(20), DashboardTheme.S(16)),
                DashboardTheme.ChartUpload, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void BuildSeries(DateTime now, int bucketCount, out double[] rx, out double[] tx, out int firstBucket)
        {
            rx = new double[bucketCount];
            tx = new double[bucketCount];
            var counts = new int[bucketCount];

            DateTime start = now - window;
            double totalSeconds = Math.Max(1, window.TotalSeconds);

            for (int i = samples.Count - 1; i >= 0; --i)
            {
                TrafficSample sample = samples[i];
                if (sample.Time < start)
                    break;

                double fraction = (sample.Time - start).TotalSeconds / totalSeconds;
                int bucket = (int)Math.Round(fraction * (bucketCount - 1));
                bucket = Math.Max(0, Math.Min(bucketCount - 1, bucket));
                rx[bucket] += sample.Rx;
                tx[bucket] += sample.Tx;
                counts[bucket]++;
            }

            firstBucket = 0;
            while ((firstBucket < bucketCount - 1) && (counts[firstBucket] == 0))
                firstBucket++;

            double lastRxValue = 0;
            double lastTxValue = 0;
            for (int i = 0; i < bucketCount; ++i)
            {
                if (counts[i] > 0)
                {
                    rx[i] /= counts[i];
                    tx[i] /= counts[i];
                    lastRxValue = rx[i];
                    lastTxValue = tx[i];
                }
                else
                {
                    rx[i] = lastRxValue;
                    tx[i] = lastTxValue;
                }
            }
        }
    }
}
