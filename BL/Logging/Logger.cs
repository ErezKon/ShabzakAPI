using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Logging
{
    public static class Logger
    {
        private static readonly object logMutex = new();
        private static readonly string logFileName;
        private static List<MemoryLog> memoryLogs = [];

        static Logger()
        {
            var dir = Directory.GetCurrentDirectory();
            Console.WriteLine($"Current Working Directory: {dir}");
            var logName = $"{DateTime.Now.ToLocalTime():ddMMyyyy.HHmmssfff}.log";
            logFileName = Path.Combine([dir, "Logs", logName]);
            Console.WriteLine($"Log File Name: {logFileName}");
            FileExtension.SafeCreate(logFileName);
        }
        public static void LogToMemory(string message, LogLevel lvl = LogLevel.Info, ConsoleColor color = ConsoleColor.Gray)
        {
            var msg = GenerateLog(message, lvl);
            memoryLogs.Add(new MemoryLog
            {
                Message = msg,
                Color = color
            });
        }

        public static void DumpMemoryLogs()
        {
            Task.Factory.StartNew(() =>
            {
                lock (logMutex)
                {
                    try
                    {
                        File.AppendAllLines(logFileName, memoryLogs.Select(ml => ml.Message));
                        foreach (MemoryLog log in memoryLogs)
                        {
                            Console.ForegroundColor = log.Color;
                            Console.WriteLine(log.Message);
                            Console.ForegroundColor = ConsoleColor.Gray;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Error in writing to log:\n {ex}");
                        Console.WriteLine();
                    }
                }
            });
            
        }
        public static void Log(string message, LogLevel lvl = LogLevel.Info, ConsoleColor color = ConsoleColor.Gray)
        {
            lock (logMutex)
            {
                var msg = GenerateLog(message, lvl);
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

        private static string GenerateLog(string message, LogLevel lvl = LogLevel.Info)
        {
            var stamp = DateTime.Now.ToLocalTime().ToString("dd/MM/yyyy.HH:mm:ss.fff");
            var level = lvl == LogLevel.Info ? "[I]" : lvl == LogLevel.Warning ? "[W]" : "[E]";
            var msg = $"{stamp} {level} - {message}\n";

            return msg;
        }
    }
}
