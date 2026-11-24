using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using GTA.UI;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.UI
{
    internal enum Align { Left, Center, Right }

    /// <summary>Colors and sizes of the command-console style interface. Accent follows the faction color.</summary>
    internal sealed class Theme
    {
        public Color Backdrop = Color.FromArgb(150, 4, 6, 10);
        public Color Panel = Color.FromArgb(238, 12, 16, 22);
        public Color PanelAlt = Color.FromArgb(240, 19, 25, 33);
        public Color PanelRaised = Color.FromArgb(245, 26, 33, 43);
        public Color Line = Color.FromArgb(40, 255, 255, 255);
        public Color Text = Color.FromArgb(255, 232, 235, 240);
        public Color TextDim = Color.FromArgb(255, 152, 160, 172);
        public Color TextFaint = Color.FromArgb(255, 98, 106, 118);
        public Color Good = Color.FromArgb(255, 88, 196, 120);
        public Color Warn = Color.FromArgb(255, 240, 190, 70);
        public Color Bad = Color.FromArgb(255, 232, 82, 72);
        public Color Info = Color.FromArgb(255, 90, 170, 232);
        public Color Accent = Color.FromArgb(255, 200, 50, 50);
        public Color AccentDim = Color.FromArgb(90, 200, 50, 50);
        public Color AccentText = Color.White;

        public const float TitleSize = 0.62f;
        public const float HeadSize = 0.42f;
        public const float BodySize = 0.34f;
        public const float SmallSize = 0.29f;
        public const float TinySize = 0.25f;

        public static readonly Font Body = Font.ChaletLondon;
        public static readonly Font Condensed = Font.ChaletComprimeCologne;
        public static readonly Font Numbers = Font.ChaletComprimeCologne;

        public void SetAccent(Rgb c)
        {
            // Keep the accent readable on the dark background.
            if (c.Luminance < 0.18f) c = c.Lerp(new Rgb(255, 255, 255), 0.45f);
            Accent = Color.FromArgb(255, c.R, c.G, c.B);
            AccentDim = Color.FromArgb(70, c.R, c.G, c.B);
            AccentText = c.Luminance > 0.6f ? Color.FromArgb(255, 16, 18, 22) : Color.White;
        }

        public Color Severity(Core.Model.EventSeverity s)
        {
            switch (s)
            {
                case Core.Model.EventSeverity.Good: return Good;
                case Core.Model.EventSeverity.Warning: return Warn;
                case Core.Model.EventSeverity.Bad:
                case Core.Model.EventSeverity.Critical: return Bad;
                default: return Info;
            }
        }
    }

    /// <summary>
    /// Immediate-mode drawing in a virtual coordinate space 1080 units tall (width follows the aspect ratio),
    /// so layouts are resolution independent. Text metrics come from the game and are cached.
    /// </summary>
    internal sealed class Renderer
    {
        public float W = 1920, H = 1080;
        public float Scale = 1f;
        public readonly Theme Theme = new Theme();
        public float MouseX = -1, MouseY = -1;

        public void BeginFrame(float uiScale)
        {
            Scale = MathEx.Clamp(uiScale, 0.75f, 1.25f);
            float aspect = Screen.AspectRatio;
            if (aspect < 1f || float.IsNaN(aspect)) aspect = 16f / 9f;
            H = 1080f / Scale;
            W = H * aspect;
        }

        public void Rect(float x, float y, float w, float h, Color c)
        {
            if (w <= 0 || h <= 0 || c.A == 0) return;
            Nat.Rect((x + w / 2f) / W, (y + h / 2f) / H, w / W, h / H, c);
        }

        public void Border(float x, float y, float w, float h, float t, Color c)
        {
            Rect(x, y, w, t, c);
            Rect(x, y + h - t, w, t, c);
            Rect(x, y + t, t, h - 2 * t, c);
            Rect(x + w - t, y + t, t, h - 2 * t, c);
        }

        /// <summary>Military-console corner brackets.</summary>
        public void Corners(float x, float y, float w, float h, float len, float t, Color c)
        {
            Rect(x, y, len, t, c); Rect(x, y, t, len, c);
            Rect(x + w - len, y, len, t, c); Rect(x + w - t, y, t, len, c);
            Rect(x, y + h - t, len, t, c); Rect(x, y + h - len, t, len, c);
            Rect(x + w - len, y + h - t, len, t, c); Rect(x + w - t, y + h - len, t, len, c);
        }

        public void Bar(float x, float y, float w, float h, float frac, Color fill, Color? bg = null)
        {
            Rect(x, y, w, h, bg ?? Color.FromArgb(60, 255, 255, 255));
            Rect(x, y, w * MathEx.Clamp01(frac), h, fill);
        }

        private float S(float size) => size * Scale;

        public void Text(string s, float x, float y, float size, Color c, Font font = Font.ChaletLondon, Align align = Align.Left, bool shadow = false, bool outline = false)
        {
            if (string.IsNullOrEmpty(s)) return;
            float nx = x / W, ny = y / H;
            switch (align)
            {
                case Align.Center: Nat.Text(s, nx, ny, S(size), c, (int)font, 0, 0, 0, shadow, outline); break;
                case Align.Right: Nat.Text(s, nx, ny, S(size), c, (int)font, 2, 0, nx, shadow, outline); break;
                default: Nat.Text(s, nx, ny, S(size), c, (int)font, 1, 0, 0, shadow, outline); break;
            }
        }

        public float TextWidth(string s, float size, Font font = Font.ChaletLondon) => Nat.TextWidth(s, S(size), (int)font) * W;

        public float LineHeight(float size, Font font = Font.ChaletLondon) => Nat.LineHeight(S(size), (int)font) * H * 1.18f;

        /// <summary>Vertical offset so text appears centered in a row of the given height.</summary>
        public float TextTop(float rowY, float rowH, float size, Font font = Font.ChaletLondon) => rowY + (rowH - Nat.LineHeight(S(size), (int)font) * H) / 2f - 2f * Scale;

        public string Ellipsize(string s, float size, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(s) || TextWidth(s, size, font) <= maxWidth) return s ?? "";
            int lo = 0, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (TextWidth(s.Substring(0, mid) + "...", size, font) <= maxWidth) lo = mid; else hi = mid - 1;
            }
            return lo <= 0 ? "..." : s.Substring(0, lo).TrimEnd() + "...";
        }

        private readonly Dictionary<string, List<string>> _wrapCache = new Dictionary<string, List<string>>();

        public List<string> Wrap(string text, float size, Font font, float maxWidth)
        {
            text = text ?? "";
            string key = (int)font + "|" + size.ToString("0.000") + "|" + ((int)maxWidth) + "|" + text;
            if (_wrapCache.TryGetValue(key, out var cached)) return cached;
            if (_wrapCache.Count > 600) _wrapCache.Clear();
            var lines = new List<string>();
            foreach (string para in text.Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (string word in para.Split(' '))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    if (TextWidth(candidate, size, font) <= maxWidth) { line = candidate; continue; }
                    if (line.Length > 0) lines.Add(line);
                    line = word;
                    while (line.Length > 1 && TextWidth(line, size, font) > maxWidth)
                    {
                        int cut = line.Length - 1;
                        while (cut > 1 && TextWidth(line.Substring(0, cut), size, font) > maxWidth) cut--;
                        lines.Add(line.Substring(0, cut));
                        line = line.Substring(cut);
                    }
                }
                lines.Add(line);
            }
            _wrapCache[key] = lines;
            return lines;
        }

        /// <summary>Draws wrapped text; returns the height used.</summary>
        public float Paragraph(string text, float x, float y, float size, Color c, float maxWidth, Font font = Font.ChaletLondon, int maxLines = 60)
        {
            var lines = Wrap(text, size, font, maxWidth);
            float lh = LineHeight(size, font);
            int n = Math.Min(lines.Count, maxLines);
            for (int i = 0; i < n; i++)
            {
                string line = i == maxLines - 1 && lines.Count > maxLines ? Ellipsize(lines[i] + " ...", size, font, maxWidth) : lines[i];
                Text(line, x, y + i * lh, size, c, font);
            }
            return n * lh;
        }

        public float ParagraphHeight(string text, float size, Font font, float maxWidth) => Wrap(text, size, font, maxWidth).Count * LineHeight(size, font);

        public void Sprite(string dict, string name, float x, float y, float w, float h, Color c, float heading = 0)
        {
            Nat.Sprite(dict, name, (x + w / 2f) / W, (y + h / 2f) / H, w / W, h / H, heading, c);
        }

        public bool Hover(float x, float y, float w, float h) => MouseX >= x && MouseX <= x + w && MouseY >= y && MouseY <= y + h;

        // ------------------------------------------------------------------ world-anchored widgets (normalized input)

        public void WorldLabel(float nx, float ny, string name, string sub, string keys, bool isMember)
        {
            float x = nx * W, y = ny * H;
            float w = Math.Max(TextWidth(name, 0.36f, Font.ChaletComprimeCologne), Math.Max(TextWidth(sub, 0.27f), TextWidth(keys, 0.25f))) + 24f;
            float h = 74f;
            x -= w / 2f; y -= h + 10f;
            Rect(x, y, w, h, Color.FromArgb(200, 10, 13, 18));
            Rect(x, y, 3f, h, isMember ? Theme.Accent : Theme.Info);
            Text(name, x + 12, y + 4, 0.36f, Theme.Text, Font.ChaletComprimeCologne);
            Text(sub, x + 12, y + 30, 0.27f, Theme.TextDim);
            Text(keys, x + 12, y + 50, 0.25f, Theme.Accent);
        }

        public void WorldBanner(float nx, float ny, string title, string sub, Color accent)
        {
            float x = nx * W, y = ny * H;
            Text(title, x, y, 0.55f, Color.FromArgb(235, accent.R, accent.G, accent.B), Font.ChaletComprimeCologne, Align.Center, true, true);
            Text(sub, x, y + 34, 0.3f, Color.FromArgb(220, 230, 230, 230), Font.ChaletLondon, Align.Center, true, false);
        }
    }
}
