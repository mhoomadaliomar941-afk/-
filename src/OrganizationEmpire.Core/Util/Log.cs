using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OrganizationEmpire.Core.Util
{
    public enum LogLevel { Debug = 0, Info = 1, Warning = 2, Error = 3 }

    /// <summary>
    /// Thread-safe file logger. Every line passes through <see cref="SecretRedactor"/> so API keys are never written to disk.
    /// </summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private static string _path;
        private static LogLevel _minLevel = LogLevel.Info;
        private static readonly List<string> Recent = new List<string>();
        private const long MaxBytes = 2 * 1024 * 1024;

        public static LogLevel MinLevel { get => _minLevel; set => _minLevel = value; }

        /// <summary>Optional extra sink (used by tests and the in-game debug overlay).</summary>
        public static Action<LogLevel, string> Sink { get; set; }

        public static void Initialize(string logFilePath, LogLevel minLevel)
        {
            lock (Sync)
            {
                _path = logFilePath;
                _minLevel = minLevel;
                try
                {
                    string dir = Path.GetDirectoryName(logFilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    if (File.Exists(logFilePath) && new FileInfo(logFilePath).Length > MaxBytes)
                    {
                        string old = logFilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(logFilePath, old);
                    }
                }
                catch
                {
                    // Logging must never crash the game.
                }
            }
        }

        public static void Debug(string msg) => Write(LogLevel.Debug, msg);
        public static void Info(string msg) => Write(LogLevel.Info, msg);
        public static void Warn(string msg) => Write(LogLevel.Warning, msg);
        public static void Error(string msg) => Write(LogLevel.Error, msg);

        public static void Error(string msg, Exception ex)
        {
            Write(LogLevel.Error, msg + " :: " + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace));
        }

        public static IReadOnlyList<string> RecentLines
        {
            get { lock (Sync) return Recent.ToArray(); }
        }

        public static void Write(LogLevel level, string msg)
        {
            if (level < _minLevel) return;
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                          + " [" + level.ToString().ToUpperInvariant() + "] " + SecretRedactor.Redact(msg ?? "");
            lock (Sync)
            {
                Recent.Add(line);
                if (Recent.Count > 60) Recent.RemoveAt(0);
                if (_path != null)
                {
                    try
                    {
                        File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                    }
                    catch
                    {
                        // ignore IO failures
                    }
                }
            }
            try { Sink?.Invoke(level, line); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Removes registered secrets and anything that looks like an API key from text before it is logged or displayed.
    /// </summary>
    public static class SecretRedactor
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> Secrets = new HashSet<string>(StringComparer.Ordinal);

        // Common key shapes: sk-..., sk-ant-..., AIza..., long bearer tokens.
        private static readonly Regex KeyPattern = new Regex(
            @"(sk-[A-Za-z0-9_\-]{8,}|AIza[0-9A-Za-z_\-]{20,}|(?<=[Bb]earer\s)[A-Za-z0-9_\-\.]{16,}|(?<=x-api-key[:=]\s?)[A-Za-z0-9_\-\.]{12,})",
            RegexOptions.Compiled);

        public static void Register(string secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 6) return;
            lock (Sync) Secrets.Add(secret);
        }

        public static void Clear()
        {
            lock (Sync) Secrets.Clear();
        }

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            lock (Sync)
            {
                foreach (string s in Secrets)
                {
                    if (text.IndexOf(s, StringComparison.Ordinal) >= 0) text = text.Replace(s, Mask(s));
                }
            }
            return KeyPattern.Replace(text, m => Mask(m.Value));
        }

        /// <summary>Returns a display-safe mask such as "sk-a…(51 chars)".</summary>
        public static string Mask(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "(not set)";
            string head = secret.Length > 4 ? secret.Substring(0, 4) : "";
            return head + "…(" + secret.Length.ToString(CultureInfo.InvariantCulture) + " chars, hidden)";
        }
    }
}
