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
        private static String _logPath;
        public static String LogPath {
            get { if (_logPath == null) { LogPath = Config.Current.LogPath ?? ""; } return _logPath; } 
            private set { Directory.CreateDirectory(value); _logPath = value; } 
        } 

        public static String LogName { get; private set; } = $"mailserver_{DateTime.Now.ToString("yyyy-MM")}.log";

        public static void WriteLine(LogType type, String @class, String method, String message, params String[] args)
        {
            WriteLine(type, @class, @method, String.Format(message, args));
        }

        public static void WriteLine(LogType type, String @class, String method, String message)
        {
            if (Config.Current.IsDebug || type != LogType.Debug)
            {
                String msg = $"[{DateTimeOffset.Now.ToString()}][{@class}][{method}]{message}";
                Console.WriteLine(msg);
                AppendLog(msg);
            }
        }

        private static void AppendLog(String message)
        {
            using (TextWriter tx = File.AppendText(Path.Combine(LogPath ?? "", LogName)))
            {
                tx.WriteLine(message);
            }
        }
    }
}
