using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace AgentHub.Common.Util
{
    /// <summary>
    /// GitHub Releases 기반 자동 업데이트. 조용히 다운로드 후 재시작 시 적용.
    /// 설치본(Velopack)에서만 동작하고, 개발/미설치 환경에서는 조용히 skip한다.
    /// </summary>
    public static class UpdateService
    {
        private const string RepoUrl = "https://github.com/YOOJUNGYU/agent-hub";
        private static UpdateManager _mgr;
        private static UpdateInfo _pending;

        /// <summary>업데이트 확인 후 조용히 다운로드. 적용 대기 상태가 되면 true.</summary>
        public static async Task<bool> CheckAndDownloadAsync()
        {
            try
            {
                _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
                if (!_mgr.IsInstalled) return false;          // 개발/미설치 → skip
                var info = await _mgr.CheckForUpdatesAsync();
                if (info == null) return false;               // 최신 버전
                await _mgr.DownloadUpdatesAsync(info);
                _pending = info;
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                return false;
            }
        }

        /// <summary>다운로드된 업데이트를 적용하고 재시작.</summary>
        public static void ApplyAndRestart()
        {
            try
            {
                if (_mgr != null && _pending != null)
                    _mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
            }
        }
    }
}
