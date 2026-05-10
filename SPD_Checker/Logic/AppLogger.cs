using System;
using System.IO;
using System.Text;

namespace SPD_Checker.Logic
{
    public enum LogLevel { INFO, WARN, ERROR, FATAL }

    public static class AppLogger
    {
        private const int RETENTION_DAYS = 7;
        private static readonly object _lock = new object();
        private static string _logDir;
        private static bool _initialized;

        public static string LogDirectory => _logDir;

        public static void Init()
        {
            if (_initialized) return;
            try
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _logDir = Path.Combine(baseDir, "SPD_Studio", "logs");
                Directory.CreateDirectory(_logDir);
                _initialized = true;
                CleanupOldLogs();
                Info("AppLogger", "==== SPD Studio 시작 ====");
            }
            catch
            {
                _initialized = false;
            }
        }

        public static void Info (string source, string message) => Write(LogLevel.INFO,  source, message, null);
        public static void Warn (string source, string message) => Write(LogLevel.WARN,  source, message, null);
        public static void Error(string source, string message, Exception ex = null) => Write(LogLevel.ERROR, source, message, ex);
        public static void Fatal(string source, string message, Exception ex = null) => Write(LogLevel.FATAL, source, message, ex);

        public static string GetLatestLogPath()
        {
            if (!_initialized || _logDir == null) return null;
            string today = Path.Combine(_logDir, $"app_{DateTime.Now:yyyyMMdd}.log");
            return File.Exists(today) ? today : _logDir;
        }

        private static void Write(LogLevel level, string source, string message, Exception ex)
        {
            if (!_initialized) return;
            try
            {
                string path = Path.Combine(_logDir, $"app_{DateTime.Now:yyyyMMdd}.log");
                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] [");
                sb.Append(level.ToString().PadRight(5)).Append("] [").Append(source ?? "-").Append("] ");
                sb.AppendLine(message ?? "");
                if (ex != null)
                {
                    sb.Append("    ").Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message);
                    if (!string.IsNullOrEmpty(ex.StackTrace)) sb.AppendLine(ex.StackTrace);
                    var inner = ex.InnerException;
                    while (inner != null)
                    {
                        sb.Append("    --> ").Append(inner.GetType().FullName).Append(": ").AppendLine(inner.Message);
                        inner = inner.InnerException;
                    }
                }
                lock (_lock) File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { /* 로그 자체 실패는 삼킴 */ }
        }

        private static void CleanupOldLogs()
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-RETENTION_DAYS);
                foreach (string f in Directory.GetFiles(_logDir, "app_*.log"))
                {
                    if (File.GetLastWriteTime(f) < cutoff)
                        try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }
    }
}
