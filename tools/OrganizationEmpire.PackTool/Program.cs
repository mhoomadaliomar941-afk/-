using System.IO.Compression;
using System.Text;
using OrganizationEmpire.Core.Config;

// Usage: PackTool <output-data-dir>
//        PackTool --glyph-spec          (prints the glyph list tools/glyphgen renders)
// Writes OrganizationEmpire.ini (defaults), sample flag PNGs, the Arabic glyph textures and ar.txt.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: PackTool <data-dir> | --glyph-spec");
    return 2;
}
if (args[0] == "--glyph-spec")
{
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.Write(GlyphSpec.Build());
    return 0;
}
string dataDir = args[0];
Directory.CreateDirectory(Path.Combine(dataDir, "flags"));
Directory.CreateDirectory(Path.Combine(dataDir, "saves"));
Directory.CreateDirectory(Path.Combine(dataDir, "logs"));

var cfg = new ModConfig();
cfg.Sanitize();
File.WriteAllText(Path.Combine(dataDir, "OrganizationEmpire.ini"), IniFile.Write(cfg, ModConfig.Header));
Console.WriteLine("wrote OrganizationEmpire.ini");

const int W = 512, H = 320;
Flags.Write(Path.Combine(dataDir, "flags", "example_tricolor.png"), W, H, (x, y) =>
    y < H / 3 ? Rgb(20, 20, 20) : y < 2 * H / 3 ? Rgb(176, 30, 35) : Rgb(242, 201, 76));
Flags.Write(Path.Combine(dataDir, "flags", "example_nordic_cross.png"), W, H, (x, y) =>
{
    bool v = x > 150 && x < 210, h = y > 130 && y < 190;
    bool vi = x > 166 && x < 194, hi = y > 146 && y < 174;
    if (vi || hi) return Rgb(242, 242, 242);
    if (v || h) return Rgb(31, 58, 147);
    return Rgb(85, 107, 47);
});
Flags.Write(Path.Combine(dataDir, "flags", "example_star_banner.png"), W, H, (x, y) =>
{
    if (Flags.InStar(x, y, W / 2, H / 2, 92, 38)) return Rgb(242, 242, 242);
    bool band = y > 110 && y < 210;
    return band ? Rgb(176, 30, 35) : Rgb(30, 30, 30);
});
Flags.Write(Path.Combine(dataDir, "flags", "example_diagonal.png"), W, H, (x, y) =>
{
    float d = x / (float)W - y / (float)H;
    if (Math.Abs(d) < 0.08f) return Rgb(242, 201, 76);
    return d > 0 ? Rgb(39, 174, 96) : Rgb(20, 20, 20);
});
Flags.Write(Path.Combine(dataDir, "flags", "template_512x320.png"), W, H, (x, y) =>
    (x < 4 || y < 4 || x >= W - 4 || y >= H - 4) ? Rgb(255, 255, 255) : ((x / 32 + y / 32) % 2 == 0 ? Rgb(60, 60, 60) : Rgb(90, 90, 90)));

File.WriteAllText(Path.Combine(dataDir, "flags", "README.txt"),
    "Custom faction flags\r\n" +
    "====================\r\n" +
    "Put PNG, JPG, BMP or DDS images in this folder, then pick them in-game:\r\n" +
    "F7 > Flag Designer > Custom flag image.\r\n\r\n" +
    "Recommended size: 512 x 320 pixels (8:5). Any size works; it is stretched to the flag frame.\r\n" +
    "Keep files under ~1 MB. ScriptHookV keeps loaded textures until the game restarts,\r\n" +
    "so the mod loads at most 24 different custom images per session.\r\n\r\n" +
    "Custom images are displayed in the mod's menus and HUD. GTA V cannot put a custom image\r\n" +
    "onto a 3D flag pole at runtime; the HQ flag pole uses one of the game's flag models.\r\n" +
    "See docs/FLAG_ASSET_PIPELINE.md for how to replace a flag prop texture with OpenIV.\r\n");
File.WriteAllText(Path.Combine(dataDir, "saves", "README.txt"), "Organization Empire save slots (JSON). Each slot keeps a .bak copy of the previous save.\r\n");
Console.WriteLine("wrote sample flags");
return 0;

static uint Rgb(int r, int g, int b) => (uint)((r << 24) | (g << 16) | (b << 8) | 0xFF);

static class Flags
{
    public static void Write(string path, int w, int h, Func<int, int, uint> pixel)
    {
        var raw = new byte[(w * 4 + 1) * h];
        int o = 0;
        for (int y = 0; y < h; y++)
        {
            raw[o++] = 0; // filter: none
            for (int x = 0; x < w; x++)
            {
                uint c = pixel(x, y);
                raw[o++] = (byte)(c >> 24);
                raw[o++] = (byte)(c >> 16);
                raw[o++] = (byte)(c >> 8);
                raw[o++] = (byte)c;
            }
        }
        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(fs, "IHDR", ihdr);
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
            Chunk(fs, "IDAT", ms.ToArray());
        }
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    public static bool InStar(int x, int y, int cx, int cy, float outer, float inner)
    {
        // Point-in-polygon test for a 5-pointed star.
        var pts = new (float X, float Y)[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            float r = i % 2 == 0 ? outer : inner;
            pts[i] = (cx + (float)Math.Cos(a) * r, cy + (float)Math.Sin(a) * r);
        }
        bool inside = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
        {
            if ((pts[i].Y > y) != (pts[j].Y > y) && x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                inside = !inside;
        }
        return inside;
    }

    private static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length);
        s.Write(len);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crcInput = new byte[t.Length + data.Length];
        Buffer.BlockCopy(t, 0, crcInput, 0, t.Length);
        Buffer.BlockCopy(data, 0, crcInput, t.Length, data.Length);
        var crc = new byte[4]; BE(crc, 0, Crc32(crcInput));
        s.Write(crc);
    }

    private static uint[]? _table;
    private static uint Crc32(byte[] data)
    {
        if (_table == null)
        {
            _table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                _table[n] = c;
            }
        }
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = _table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}


/// <summary>Every character the in-game glyph renderer may need, with the text that renders its shape (ZWJ context for Arabic forms).</summary>
static class GlyphSpec
{
    public static string Build()
    {
        var sb = new StringBuilder("[");
        bool first = true;
        void Add(char code, string text)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"code\":").Append((int)code).Append(",\"text\":\"");
            foreach (char c in text) sb.Append("\\u").Append(((int)c).ToString("x4"));
            sb.Append("\"}");
        }
        // Presentation forms are rendered from their own code points: Cairo maps them directly, and
        // their advances then match the joined word exactly (ZWJ contexts pick up extra spacing).
        foreach (char c in OrganizationEmpire.Core.Localization.ArabicText.AllShapedForms()) Add(c, c.ToString());
        foreach (char c in "\u060C\u061B\u061F\u0640\u066A\u066B\u066C") Add(c, c.ToString());
        for (char c = '\u0660'; c <= '\u0669'; c++) Add(c, c.ToString());
        for (char c = '!'; c <= '~'; c++) Add(c, c.ToString());
        foreach (char c in "\u00A3\u00A9\u00AB\u00B0\u00B7\u00BB\u00D7\u2013\u2014\u2018\u2019\u201C\u201D\u2022\u2026\u20AC\u2190\u2192\u2191\u2193") Add(c, c.ToString());
        for (char c = '\u00C0'; c <= '\u00FF'; c++) if (c != '\u00D7' && c != '\u00F7') Add(c, c.ToString());
        return sb.Append(']').ToString();
    }
}
