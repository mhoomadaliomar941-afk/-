using System;
using System.Collections.Generic;
using System.Globalization;

namespace OrganizationEmpire.Core.Util
{
    /// <summary>Engine-independent 3D vector used by the simulation and save files.</summary>
    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static Vec3 Zero => new Vec3(0, 0, 0);
        public bool IsZero => X == 0 && Y == 0 && Z == 0;

        public float DistanceTo(Vec3 o)
        {
            float dx = X - o.X, dy = Y - o.Y, dz = Z - o.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public float DistanceTo2D(Vec3 o)
        {
            float dx = X - o.X, dy = Y - o.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", X, Y, Z);
        }
    }

    /// <summary>RGBA color with hex helpers. Stored in save files as "#RRGGBB" strings.</summary>
    public struct Rgb
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public Rgb(int r, int g, int b, int a = 255)
        {
            R = (byte)MathEx.Clamp(r, 0, 255);
            G = (byte)MathEx.Clamp(g, 0, 255);
            B = (byte)MathEx.Clamp(b, 0, 255);
            A = (byte)MathEx.Clamp(a, 0, 255);
        }

        public Rgb WithAlpha(int a) => new Rgb(R, G, B, a);

        public Rgb Scale(float f) => new Rgb((int)(R * f), (int)(G * f), (int)(B * f), A);

        public Rgb Lerp(Rgb o, float t)
        {
            return new Rgb((int)(R + (o.R - R) * t), (int)(G + (o.G - G) * t), (int)(B + (o.B - B) * t), (int)(A + (o.A - A) * t));
        }

        public float Luminance => (0.2126f * R + 0.7152f * G + 0.0722f * B) / 255f;

        public string ToHex() => "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");

        public static Rgb Parse(string hex, Rgb fallback)
        {
            return TryParse(hex, out Rgb c) ? c : fallback;
        }

        public static bool TryParse(string hex, out Rgb color)
        {
            color = new Rgb(255, 255, 255);
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string h = hex.Trim().TrimStart('#');
            if (h.Length != 6 && h.Length != 8) return false;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
            if (h.Length == 6) color = new Rgb((int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
            else color = new Rgb((int)((v >> 24) & 0xFF), (int)((v >> 16) & 0xFF), (int)((v >> 8) & 0xFF), (int)(v & 0xFF));
            return true;
        }

        public int DistanceSq(Rgb o)
        {
            int dr = R - o.R, dg = G - o.G, db = B - o.B;
            return dr * dr + dg * dg + db * db;
        }

        public override string ToString() => ToHex();
    }

    public static class MathEx
    {
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
        public static long Clamp(long v, long min, long max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Pct(float v) => Clamp(v, 0f, 100f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }
    }

    /// <summary>
    /// Small deterministic PRNG (xorshift*). Deterministic seeding keeps the simulation reproducible in tests
    /// and NPC profiles stable for the same world ped.
    /// </summary>
    public sealed class Rng
    {
        private ulong _state;

        public Rng(int seed)
        {
            _state = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL;
            if (_state == 0) _state = 0x2545F4914F6CDD1DUL;
        }

        public static Rng FromTime() => new Rng(Environment.TickCount ^ Guid.NewGuid().GetHashCode());

        public ulong NextULong()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public float NextFloat() => (float)NextDouble();

        public float Range(float min, float max) => min + (float)NextDouble() * (max - min);

        /// <summary>Integer in [min, maxExclusive).</summary>
        public int Next(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextULong() % (ulong)(maxExclusive - min));
        }

        public int Next(int maxExclusive) => Next(0, maxExclusive);

        public bool Chance(float probability01) => NextDouble() < probability01;

        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0) return default(T);
            return list[Next(list.Count)];
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>Stable hash for strings across runtimes (string.GetHashCode is randomized on .NET Core).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char c in s ?? "") h = (h ^ c) * 16777619;
                return h;
            }
        }
    }

    public static class Ids
    {
        private static int _counter;

        public static string New(string prefix)
        {
            int c = System.Threading.Interlocked.Increment(ref _counter);
            return prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + c.ToString("x", CultureInfo.InvariantCulture);
        }
    }

    public static class TextUtil
    {
        /// <summary>
        /// Makes user/AI supplied text safe for GTA's text renderer: '~' starts formatting codes and
        /// angle brackets can be interpreted as markup, so they are replaced. Control characters are dropped.
        /// </summary>
        public static string SafeForGame(string s, int maxLength = 0)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '~') sb.Append('-');
                else if (c == '<') sb.Append('(');
                else if (c == '>') sb.Append(')');
                else if (c == '\t') sb.Append(' ');
                else if (c == '\n') sb.Append('\n');
                else if (char.IsControl(c)) continue;
                else if (c == '‘' || c == '’') sb.Append('\'');
                else if (c == '“' || c == '”') sb.Append('"');
                else if (c == '—' || c == '–') sb.Append('-');
                else if (c == '…') sb.Append("...");
                else sb.Append(c);
            }
            string r = sb.ToString();
            if (maxLength > 0 && r.Length > maxLength) r = r.Substring(0, maxLength);
            return r;
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            if (max <= 1) return s.Substring(0, max);
            return s.Substring(0, max - 1) + "…";
        }

        public static string Money(long amount)
        {
            string sign = amount < 0 ? "-" : "";
            return sign + "$" + Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);
        }

        public static string Pct(float v) => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture) + "%";

        public static string F(float v, int decimals = 0) => v.ToString("F" + decimals, CultureInfo.InvariantCulture);

        public static string Humanize(string enumName)
        {
            if (string.IsNullOrEmpty(enumName)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < enumName.Length; i++)
            {
                char c = enumName[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(enumName[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Initials(string name, int max = 3)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (char.IsLetterOrDigit(p[0])) sb.Append(char.ToUpperInvariant(p[0]));
                if (sb.Length >= max) break;
            }
            return sb.Length == 0 ? "?" : sb.ToString();
        }

        /// <summary>Word-wraps by character count (used by tests and the offline UI fallback).</summary>
        public static List<string> WrapByChars(string text, int maxChars)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
            foreach (string para in text.Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (string word in para.Split(' '))
                {
                    if (line.Length == 0) line = word;
                    else if (line.Length + 1 + word.Length <= maxChars) line += " " + word;
                    else { lines.Add(line); line = word; }
                    while (line.Length > maxChars) { lines.Add(line.Substring(0, maxChars)); line = line.Substring(maxChars); }
                }
                lines.Add(line);
            }
            return lines;
        }
    }
}
