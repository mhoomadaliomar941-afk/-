using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using OrganizationEmpire.Core.AI;

namespace OrganizationEmpire.Native
{
    /// <summary>
    /// Converts a key press into the character it produces on the user's keyboard layout (ToUnicodeEx),
    /// so the inline text fields in the menu and chat work with non-US layouts. Falls back to a US map.
    /// </summary>
    internal static class KeyText
    {
        [DllImport("user32.dll")]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        private static bool _nativeFailed;

        public static char? ToChar(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0;
            bool ctrl = (keyData & Keys.Control) != 0;
            bool alt = (keyData & Keys.Alt) != 0;
            if (ctrl && !alt) return null; // shortcuts, not text
            if (!_nativeFailed)
            {
                try
                {
                    var state = new byte[256];
                    if (shift) { state[0x10] = 0x80; state[0xA0] = 0x80; }
                    if (ctrl && alt) { state[0x11] = 0x80; state[0x12] = 0x80; state[0xA2] = 0x80; state[0xA5] = 0x80; } // AltGr
                    if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 0x01;
                    IntPtr layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
                    uint scan = MapVirtualKeyEx((uint)key, 0, layout);
                    var sb = new StringBuilder(8);
                    int n = ToUnicodeEx((uint)key, scan, state, sb, sb.Capacity, 4, layout);
                    if (n == 1 && !char.IsControl(sb[0])) return sb[0];
                    if (n != 0) return null;
                }
                catch (Exception)
                {
                    _nativeFailed = true;
                }
            }
            return UsFallback(key, shift);
        }

        private static char? UsFallback(Keys key, bool shift)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + (key - Keys.A));
                bool caps = Control.IsKeyLocked(Keys.CapsLock);
                return shift ^ caps ? char.ToUpperInvariant(c) : c;
            }
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                const string shifted = ")!@#$%^&*(";
                int i = key - Keys.D0;
                return shift ? shifted[i] : (char)('0' + i);
            }
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return (char)('0' + (key - Keys.NumPad0));
            switch (key)
            {
                case Keys.Space: return ' ';
                case Keys.OemPeriod: return shift ? '>' : '.';
                case Keys.Oemcomma: return shift ? '<' : ',';
                case Keys.OemQuestion: return shift ? '?' : '/';
                case Keys.OemSemicolon: return shift ? ':' : ';';
                case Keys.OemQuotes: return shift ? '"' : '\'';
                case Keys.OemMinus: return shift ? '_' : '-';
                case Keys.Oemplus: return shift ? '+' : '=';
                case Keys.OemOpenBrackets: return shift ? '{' : '[';
                case Keys.OemCloseBrackets: return shift ? '}' : ']';
                case Keys.OemPipe: return shift ? '|' : '\\';
                case Keys.Oemtilde: return shift ? '~' : '`';
                case Keys.Decimal: return '.';
                case Keys.Add: return '+';
                case Keys.Subtract: return '-';
                case Keys.Multiply: return '*';
                case Keys.Divide: return '/';
                default: return null;
            }
        }

        public static bool TryParseKey(string name, out Keys key)
        {
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (Enum.TryParse(name.Trim(), true, out Keys k) && k != Keys.None)
            {
                key = k & Keys.KeyCode;
                return true;
            }
            return false;
        }
    }

    /// <summary>Encrypts the API key with Windows DPAPI (current-user scope). Not available on non-Windows runtimes.</summary>
    internal sealed class DpapiKeyProtector : IKeyProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OrganizationEmpire.ApiKey.v1");

        public bool Available
        {
            get
            {
                try { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
                catch { return false; }
            }
        }

        public string Protect(string plain)
        {
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain ?? ""), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        public string Unprotect(string protectedValue)
        {
            byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
    }
}
