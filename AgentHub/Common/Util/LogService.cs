using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using NLog.Windows.Forms;

namespace AgentHub.Common.Util
{
    public class LogService
    {
        private static LogService _instance;
        private readonly Logger _logger;

        public LogService()
        {
            var config = new LoggingConfiguration();

            var logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logFolderPath)) Directory.CreateDirectory(logFolderPath);

            var fileTarget = new FileTarget("logFile")
            {
                FileName = Path.Combine(logFolderPath, "${shortdate}.log"),
                Layout = "[${date} - ${level}] | ${logger} | ${callsite:className=true:methodName=true} | ${message} | ${exception:format=ToString:maxInnerExceptionLevel=3}"
            };

            var messageBoxTarget = new MessageBoxTarget
            {
                Name = "logMessageBox",
                Layout = "${date} | ${level} | ${logger} | ${callsite:className=true:methodName=true} | ${message} | ${exception:format=ToString:maxInnerExceptionLevel=3}",
                Caption = "${level}"
            };
            var wrapper = new AsyncTargetWrapper(messageBoxTarget);

            config.AddTarget(fileTarget);
            config.AddTarget("logMessageBox", wrapper);

            config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);   
            config.AddRule(LogLevel.Fatal, LogLevel.Fatal, wrapper);  

            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();

            _logger = LogManager.GetCurrentClassLogger();
        }

        public static LogService Instance => _instance ??= new LogService();

        public void Error(string msg) => Log(LogLevel.Error, msg, null);

        public void Error(string msg, Exception ex) => Log(LogLevel.Error, msg, ex);

        public void Error(Exception ex) => Log(LogLevel.Error, "Error", ex);

        private void Log(LogLevel logLevel, string message, Exception exception)
        {
            var t = typeof(LogService);
            _logger.Log(t, GetLogEventInfoType(t, message, exception, logLevel));
            LogManager.Flush(TimeSpan.FromMinutes(5));
        }

        private static LogEventInfo GetLogEventInfoType(Type loggerType, string message, Exception exception, LogLevel logLevel)
            => new LogEventInfo
            {
                Level = logLevel,
                LoggerName = loggerType.ToString(),
                Message = message,
                Exception = exception,
                TimeStamp = DateTime.Now
            };
    }
}
