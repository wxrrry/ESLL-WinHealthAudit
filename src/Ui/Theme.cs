using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinHealthAudit.Ui
{
    /// <summary>Colours, fonts and the small helpers every control in the window uses.</summary>
    internal static class Theme
    {
        public static readonly Color Screen = Color.FromArgb(15, 17, 22);
        public static readonly Color Surface = Color.FromArgb(25, 28, 36);
        public static readonly Color SurfaceHover = Color.FromArgb(33, 37, 48);
        public static readonly Color Line = Color.FromArgb(42, 47, 61);
        public static readonly Color Text = Color.FromArgb(232, 235, 242);
        public static readonly Color Muted = Color.FromArgb(151, 160, 180);
        public static readonly Color Accent = Color.FromArgb(59, 130, 246);
        public static readonly Color AccentHover = Color.FromArgb(96, 156, 255);
        public static readonly Color Critical = Color.FromArgb(239, 68, 68);
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);
        public static readonly Color Info = Color.FromArgb(56, 189, 248);
        public static readonly Color Good = Color.FromArgb(34, 197, 94);

        public static readonly Brush ScreenBrush = new SolidBrush(Screen);
        public static readonly Brush SurfaceBrush = new SolidBrush(Surface);
        public static readonly Brush AccentBrush = new SolidBrush(Accent);

        public static readonly Font Title = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font TileNumber = new Font("Segoe UI", 22f, FontStyle.Bold);
        public static readonly Font TileCaption = new Font("Segoe UI", 8f);
        public static readonly Font Heading = new Font("Segoe UI", 10f, FontStyle.Bold);
        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font SmallBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 9f);

        public static Color SeverityColour(Severity level)
        {
            switch (level)
            {
                case Severity.Critical: return Critical;
                case Severity.Warning: return Warning;
                default: return Info;
            }
        }

        public static string SeverityWord(Severity level)
        {
            switch (level)
            {
                case Severity.Critical: return "CRITICAL";
                case Severity.Warning: return "WARNING";
                default: return "INFO";
            }
        }

        /// <summary>Height a label needs to show the text without clipping.</summary>
        public static int TextHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return 0;

            var bounds = TextRenderer.MeasureText(
                text, font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            return bounds.Height + 3;
        }

        /// <summary>Sets the text and the bounds of a label in one call, returns its bottom edge.</summary>
        public static int PlaceText(Label label, string text, Font font, Color colour, int x, int y, int width)
        {
            label.Text = text;
            label.Font = font;
            label.ForeColor = colour;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            label.SetBounds(x, y, width, TextHeight(text, font, width));
            return label.Bottom;
        }

        public static Label MakeLabel(string text, Font font, Color colour)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = colour,
                BackColor = Color.Transparent,
                AutoSize = false
            };
        }

        public static Button MakeButton(string text, EventHandler onClick, Color background)
        {
            var button = new Button
            {
                Text = text,
                Font = Body,
                ForeColor = Color.White,
                BackColor = background,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance =
                {
                    BorderSize = 0,
                    MouseOverBackColor = Lighter(background, 24),
                    MouseDownBackColor = background
                },
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };

            if (onClick != null) button.Click += onClick;
            return button;
        }

        public static Color Lighter(Color colour, int amount)
        {
            return Color.FromArgb(
                Math.Min(255, colour.R + amount),
                Math.Min(255, colour.G + amount),
                Math.Min(255, colour.B + amount));
        }

        /// <summary>Gives a control rounded corners.</summary>
        public static void RoundCorners(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0) return;

            var box = new Rectangle(0, 0, control.Width, control.Height);

            using (var shape = new GraphicsPath())
            {
                shape.AddArc(0, 0, radius, radius, 180, 90);
                shape.AddArc(box.Width - radius, 0, radius, radius, 270, 90);
                shape.AddArc(box.Width - radius, box.Height - radius, radius, radius, 0, 90);
                shape.AddArc(0, box.Height - radius, radius, radius, 90, 90);
                shape.CloseFigure();

                control.Region = new Region(shape);
            }
        }
    }
}
