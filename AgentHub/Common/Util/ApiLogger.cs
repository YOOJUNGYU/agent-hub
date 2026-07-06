using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace AgentHub.Common.Util
{
    public static class ApiLogger
    {
        private static readonly string LogFolder = "logs.Api";
        private static readonly string LogFilePrefix = "log-";
        private static readonly string LogFileName = $"{LogFilePrefix}.txt";
        private static readonly string LogFilePattern = $"{LogFilePrefix}*.txt";

        public static void Initialize()
        {
            RemoveOldLogs();
            var logFileFullPathName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFolder, LogFileName);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logFileFullPathName, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("======== Start ========");
        }

        public static void Error(Exception ex, string msg = "")
            => Log.Error(ex, msg);

        public static void Info(string msg)
            => Log.Information(msg);

        private static void RemoveOldLogs()
        {
            Task.Run(() =>
            {
                var logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogFolder);
                var currentDate = DateTime.Today;
                if (!Directory.Exists(logFolderPath)) return;
                var logFiles = Directory.GetFiles(logFolderPath, LogFilePattern);
                foreach (var logFile in logFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(logFile);
                    if (!fileName.StartsWith(LogFilePrefix)) continue;
                    var datePart = fileName.Substring(4);
                    if (!DateTime.TryParseExact(datePart, "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var fileDate)) continue;
                    if ((currentDate - fileDate).TotalDays > 30)
                        File.Delete(logFile);
                }
            });
        }
    }
}
