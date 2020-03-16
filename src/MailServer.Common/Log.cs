using System;
using System.IO;

namespace MailServer.Common {
    public enum LogType {
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    public static class Log {
        public static readonly String LogPath = Config.Current.LogPath;

        public static String LogName { get; private set; } = $"mailserver_{DateTime.Now.ToString("yyyy-MM")}.log";

        public static void WriteLine(LogType type, String @class, String method, String message, params String[] args)
        {
            if (Config.Current.IsDebug || type != LogType.Debug)
            {
                String msg = $"[{DateTimeOffset.Now.ToString()}][{@class}][{method}]{String.Format(message, args)}";
                Console.WriteLine(msg);
                AppendLog(msg);
            }
        }

        private static void AppendLog(String message)
        {
            using (TextWriter tx = File.AppendText(Path.Combine(LogPath, LogName)))
            {
                tx.WriteLine(message);
            }
        }
    }
}
