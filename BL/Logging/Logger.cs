using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Logging
{
    public static class Logger
    {
        private static readonly object logMutex = new();
        private static readonly string logFileName;

        static Logger()
        {
            var dir = Directory.GetCurrentDirectory();
            Console.WriteLine($"Current Working Directory: {dir}");
            var logName = $"{DateTime.Now.ToLocalTime():ddMMyyyy.HHmmssfff}.log";
            logFileName = Path.Combine([dir, "Logs", logName]);
            Console.WriteLine($"Log File Name: {logFileName}");
            FileExtension.SafeCreate(logFileName);
        }
        public static void Log(string message, LogLevel lvl = LogLevel.Info, ConsoleColor color = ConsoleColor.Gray)
        {
            lock (logMutex)
            {
                var stamp = DateTime.Now.ToLocalTime().ToString("ddMMyyyy.HHmmss.fff");
                var level = lvl == LogLevel.Info ? "[I]" : lvl == LogLevel.Warning ? "[W]" : "[E]";
                var msg = $"{stamp} {level} - {message}\n";
                Console.ForegroundColor = color;
                Console.WriteLine(msg);
                Console.ForegroundColor = ConsoleColor.Gray;

                try
                {
                    File.AppendAllText(logFileName, msg);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Error in writing to log:\n {ex}");
                    Console.WriteLine();
                }
            }
        }
    }
}
