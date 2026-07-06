using System;
using System.Diagnostics;
using System.Reflection;

namespace AgentHub.Common.Util
{
    public static class EtcUtil
    {
        public static DateTime GetBuildDateTime()
        {
            //1. Version 값
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version == null) return DateTime.MinValue;

            //2. Version Text의 세번째 값(Build Number)은 2000년 1월 1일부터 Build된 날짜까지의 총 일(Days) 수
            var refDate = new DateTime(2000, 1, 1);
            var buildDateTime = refDate.AddDays(version.Build);

            //3. Version Text의 네번째 값(Revision Number)은 자정으로부터 Build된 시간까지의 지나간 초(Second) 값
            buildDateTime = buildDateTime.AddSeconds(version.Revision * 2);

            return buildDateTime;
        }

        public static FileVersionInfo GetFileVersionInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            return FileVersionInfo.GetVersionInfo(assembly.Location);
        }
    }
}
