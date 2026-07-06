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

        public static LogService CreateInstance() => Instance;

        public void Fatal(string msg) => Log(LogLevel.Fatal, msg, null);

        public void Fatal(string msg, Exception ex) => Log(LogLevel.Fatal, msg, ex);

        public void Error(string msg) => Log(LogLevel.Error, msg, null);

        public void Error(string msg, Exception ex) => Log(LogLevel.Error, msg, ex);

        public void Error(Exception ex) => Log(LogLevel.Error, "Error", ex);

        public void Warn(string msg) => Log(LogLevel.Warn, msg, null);

        public void Info(string msg) => Log(LogLevel.Info, msg, null);

        public void Debug(string msg) => Log(LogLevel.Debug, msg, null);

        public void Debug(string msg, Exception ex) => Log(LogLevel.Debug, msg, ex);

        public void Debug(Exception ex) => Log(LogLevel.Debug, "Debug", ex);

        public void Trace(string msg) => Log(LogLevel.Trace, msg, null);

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

        public void WriteDebug(string msg) => System.Diagnostics.Debug.WriteLine(msg);
    }
}
