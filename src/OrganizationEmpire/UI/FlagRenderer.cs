using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using System.IO;
using GTA.UI;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.UI
{
    /// <summary>
    /// Draws faction flags in the interface: procedural patterns from rectangles, an emblem (game textures where
    /// available, otherwise a monogram), or a custom PNG/JPG/DDS from the flags folder via ScriptHookV textures.
    /// </summary>
    internal sealed class FlagRenderer
    {
        private readonly ModContext _mod;
        private readonly Dictionary<string, CustomSprite> _custom = new Dictionary<string, CustomSprite>();
        private const int MaxCustomTextures = 24;

        public FlagRenderer(ModContext mod) { _mod = mod; }

        /// <summary>
        /// ScriptHookV draws custom textures above all native UI, so while a modal dialog or popup is open
        /// the pattern version is shown instead to avoid covering the dialog.
        /// </summary>
        public bool SuppressCustomImages { get; set; }

        public static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".dds", ".bmp" };

        public List<string> AvailableImages()
        {
            var list = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(_mod.FlagsDir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(SupportedExtensions, ext) >= 0) list.Add(Path.GetFileName(f));
                }
            }
            catch (Exception ex) { Log.Debug("Flag folder scan failed: " + ex.Message); }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public void Draw(Renderer r, FlagDesign flag, float x, float y, float w, float h, bool allowCustom = true)
        {
            if (flag == null) return;
            // A flag is an image: in the mirrored (Arabic) layout it moves but is never flipped.
            bool own = r.Mirror && !r.Unmirrored;
            if (own) r.BeginUnmirrored(x, w);
            try { DrawFlag(r, flag, x, y, w, h, allowCustom); }
            finally { if (own) r.EndUnmirrored(); }
        }

        private void DrawFlag(Renderer r, FlagDesign flag, float x, float y, float w, float h, bool allowCustom)
        {
            r.Rect(x - 2, y - 2, w + 4, h + 4, Color.FromArgb(160, 0, 0, 0));
            if (allowCustom && !SuppressCustomImages && !string.IsNullOrEmpty(flag.CustomImage) && DrawCustom(r, flag.CustomImage, x, y, w, h)) return;
            Color c1 = Rgb.Parse(flag.Color1, new Rgb(30, 30, 30)).ToColor();
            Color c2 = Rgb.Parse(flag.Color2, new Rgb(176, 30, 35)).ToColor();
            Color c3 = Rgb.Parse(flag.Color3, new Rgb(242, 242, 242)).ToColor();
            switch (flag.Pattern)
            {
                case FlagPattern.Solid:
                    r.Rect(x, y, w, h, c1); break;
                case FlagPattern.HorizontalBicolor:
                    r.Rect(x, y, w, h / 2, c1); r.Rect(x, y + h / 2, w, h / 2, c2); break;
                case FlagPattern.HorizontalTricolor:
                    r.Rect(x, y, w, h / 3, c1); r.Rect(x, y + h / 3, w, h / 3, c2); r.Rect(x, y + 2 * h / 3, w, h / 3 + 0.5f, c3); break;
                case FlagPattern.VerticalBicolor:
                    r.Rect(x, y, w / 2, h, c1); r.Rect(x + w / 2, y, w / 2, h, c2); break;
                case FlagPattern.VerticalTricolor:
                    r.Rect(x, y, w / 3, h, c1); r.Rect(x + w / 3, y, w / 3, h, c2); r.Rect(x + 2 * w / 3, y, w / 3 + 0.5f, h, c3); break;
                case FlagPattern.NordicCross:
                    r.Rect(x, y, w, h, c1);
                    r.Rect(x + w * 0.28f, y, h * 0.22f, h, c2); r.Rect(x, y + h * 0.39f, w, h * 0.22f, c2);
                    r.Rect(x + w * 0.28f + h * 0.06f, y, h * 0.1f, h, c3); r.Rect(x, y + h * 0.45f, w, h * 0.1f, c3);
                    break;
                case FlagPattern.CenterCross:
                    r.Rect(x, y, w, h, c1);
                    r.Rect(x + w / 2 - h * 0.1f, y, h * 0.2f, h, c2); r.Rect(x, y + h * 0.4f, w, h * 0.2f, c2);
                    break;
                case FlagPattern.Bordered:
                    r.Rect(x, y, w, h, c1);
                    r.Rect(x + h * 0.12f, y + h * 0.12f, w - h * 0.24f, h * 0.76f, c2);
                    r.Border(x + h * 0.16f, y + h * 0.16f, w - h * 0.32f, h * 0.68f, Math.Max(1.5f, h * 0.025f), c3);
                    break;
                case FlagPattern.Quartered:
                    r.Rect(x, y, w / 2, h / 2, c1); r.Rect(x + w / 2, y, w / 2, h / 2, c2);
                    r.Rect(x, y + h / 2, w / 2, h / 2, c2); r.Rect(x + w / 2, y + h / 2, w / 2, h / 2, c1);
                    break;
                case FlagPattern.Canton:
                    for (int i = 0; i < 7; i++) r.Rect(x, y + i * h / 7, w, h / 7 + 0.5f, i % 2 == 0 ? c2 : c3);
                    r.Rect(x, y, w * 0.42f, h * 4 / 7, c1);
                    break;
                case FlagPattern.FiveStripes:
                    for (int i = 0; i < 5; i++) r.Rect(x, y + i * h / 5, w, h / 5 + 0.5f, i % 2 == 0 ? c1 : c2);
                    break;
                case FlagPattern.CenterBand:
                    r.Rect(x, y, w, h, c1);
                    r.Rect(x, y + h * 0.3f, w, h * 0.4f, c2);
                    r.Rect(x, y + h * 0.3f, w, Math.Max(1.5f, h * 0.03f), c3);
                    r.Rect(x, y + h * 0.7f - Math.Max(1.5f, h * 0.03f), w, Math.Max(1.5f, h * 0.03f), c3);
                    break;
            }
            DrawEmblem(r, flag, flag.Pattern == FlagPattern.Canton ? x + w * 0.21f : x + w / 2, flag.Pattern == FlagPattern.Canton ? y + h * 2 / 7 : y + h / 2, h * (flag.Pattern == FlagPattern.Canton ? 0.34f : 0.5f));
        }

        public void DrawEmblem(Renderer r, FlagDesign flag, float cx, float cy, float size)
        {
            bool own = r.Mirror && !r.Unmirrored;
            if (own) r.BeginUnmirrored(cx - size / 2, size);
            try { DrawEmblemCore(r, flag, cx, cy, size); }
            finally { if (own) r.EndUnmirrored(); }
        }

        private void DrawEmblemCore(Renderer r, FlagDesign flag, float cx, float cy, float size)
        {
            Color ec = Rgb.Parse(flag.EmblemColor, new Rgb(242, 201, 76)).ToColor();
            string tex = null;
            switch (flag.Emblem)
            {
                case EmblemType.None: return;
                case EmblemType.Star: tex = "shop_new_star"; break;
                case EmblemType.Crown: tex = "mp_hostcrown"; break;
                case EmblemType.Spade: tex = "card_suit_spades"; break;
                case EmblemType.Club: tex = "card_suit_clubs"; break;
                case EmblemType.Diamond: tex = "card_suit_diamonds"; break;
                case EmblemType.Heart: tex = "card_suit_hearts"; break;
                case EmblemType.Shield:
                    // Drawn from rectangles: a stepped heater shield.
                    float sw = size * 0.8f, sh = size;
                    for (int i = 0; i < 6; i++)
                    {
                        float f = i < 3 ? 1f : 1f - (i - 2) * 0.22f;
                        r.Rect(cx - sw * f / 2, cy - sh / 2 + i * sh / 6, sw * f, sh / 6 + 0.5f, ec);
                    }
                    return;
                case EmblemType.CustomImage:
                    if (!SuppressCustomImages && !string.IsNullOrEmpty(flag.CustomImage) && DrawCustom(r, flag.CustomImage, cx - size / 2, cy - size / 2, size, size)) return;
                    break;
            }
            if (tex != null && Nat.HasTexture("commonmenu", tex))
            {
                r.Sprite("commonmenu", tex, cx - size / 2, cy - size / 2, size, size, ec);
                return;
            }
            // Fallback / monogram
            string text = string.IsNullOrWhiteSpace(flag.MonogramText) ? "?" : flag.MonogramText.Trim().ToUpperInvariant();
            if (text.Length > 3) text = text.Substring(0, 3);
            float textSize = size / 64f * 0.9f / r.Scale;
            float lh = r.LineHeight(textSize, Font.ChaletComprimeCologne) / 1.18f;
            r.Text(text, cx, cy - lh / 2 - 2, textSize, ec, Font.ChaletComprimeCologne, Align.Center, true, false);
        }

        private bool DrawCustom(Renderer r, string file, float x, float y, float w, float h)
        {
            try
            {
                string path = Path.Combine(_mod.FlagsDir, Path.GetFileName(file));
                if (!File.Exists(path)) return false;
                if (!_custom.TryGetValue(path, out var sprite))
                {
                    if (_custom.Count >= MaxCustomTextures) return false; // ScriptHookV textures can't be freed; keep a sane limit
                    sprite = new CustomSprite(path, new SizeF(1, 1), new PointF(0, 0));
                    _custom[path] = sprite;
                }
                // CustomSprite uses SHVDN's 1280x720 reference space.
                sprite.Position = new PointF(r.ScreenLeft(x, w) / r.W * Screen.Width, y / r.H * Screen.Height);
                sprite.Size = new SizeF(w / r.W * Screen.Width, h / r.H * Screen.Height);
                sprite.Draw();
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("Custom flag draw failed: " + ex.Message);
                return false;
            }
        }
    }
}
