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

        /// <summary>업데이트 확인 결과.</summary>
        public enum CheckResult
        {
            UpdateReady, // 새 버전 다운로드 완료 → 재시작 시 적용
            UpToDate,    // 이미 최신 버전
            Unavailable  // 개발/미설치 환경이거나 확인 실패
        }

        /// <summary>업데이트 확인 후 조용히 다운로드. 결과 상태를 반환.</summary>
        public static async Task<CheckResult> CheckAndDownloadAsync()
        {
            try
            {
                _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
                if (!_mgr.IsInstalled) return CheckResult.Unavailable;  // 개발/미설치 → 확인 불가
                var info = await _mgr.CheckForUpdatesAsync();
                if (info == null) return CheckResult.UpToDate;          // 최신 버전
                await _mgr.DownloadUpdatesAsync(info);
                _pending = info;
                return CheckResult.UpdateReady;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
                return CheckResult.Unavailable;
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
