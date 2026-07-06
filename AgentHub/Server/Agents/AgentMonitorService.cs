using System;
using System.Collections.Generic;
using System.Threading;
using AgentHub.Common.Models;
using AgentHub.Common.Util;
using AgentHub.Server.Socket;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// Claude 에이전트 모니터링 데이터의 소스(seam).
    /// 현재는 mock 데이터를 타이머로 변화시켜 /ws/agents 로 push한다.
    /// 실제 연동 시 이 클래스 내부(데이터 생성부)만 교체하면 된다.
    /// </summary>
    public static class AgentMonitorService
    {
        private static readonly object Lock = new object();

        private static readonly List<AgentStatus> Agents = new List<AgentStatus>
        {
            new AgentStatus { Id = "agent-1", Name = "Claude Code", Status = "working", CurrentTask = "refactor EmbedIOServer.cs", Progress = 42 },
            new AgentStatus { Id = "agent-2", Name = "Docs Writer",  Status = "idle",    CurrentTask = "",                          Progress = 0 },
            new AgentStatus { Id = "agent-3", Name = "Test Runner",  Status = "error",   CurrentTask = "build failed: CS0246",      Progress = 0 },
            new AgentStatus { Id = "agent-4", Name = "Reviewer",     Status = "working", CurrentTask = "reviewing PR #12",          Progress = 18 },
        };

        private static Timer _timer;
        private static AgentMonitorModule _module;
        private static int _tick;

        /// <summary>현재 에이전트 목록(복사본).</summary>
        public static List<AgentStatus> CurrentAgents()
        {
            lock (Lock)
            {
                Stamp();
                return new List<AgentStatus>(Agents);
            }
        }

        /// <summary>WebSocket 실시간 메시지 형태: { type:"agents", agents:[...] }</summary>
        public static string CurrentAgentsMessage() =>
            Json.Serialize(new { type = "agents", agents = CurrentAgents() });

        /// <summary>REST 스냅샷 형태: { agents:[...] }</summary>
        public static string CurrentAgentsSnapshot() =>
            Json.Serialize(new { agents = CurrentAgents() });

        public static void Start(AgentMonitorModule module)
        {
            _module = module;
            _timer?.Dispose();
            _timer = new Timer(_ => Tick(), null, 2500, 2500);
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _module = null;
        }

        private static void Stamp()
        {
            var now = DateTime.UtcNow.ToString("o");
            foreach (var a in Agents) a.UpdatedAt = now;
        }

        private static void Tick()
        {
            try
            {
                lock (Lock)
                {
                    _tick++;
                    foreach (var a in Agents)
                    {
                        if (a.Status != "working") continue;
                        a.Progress += 7;
                        if (a.Progress >= 100)
                        {
                            a.Progress = 100;
                            a.Status = "idle";
                            a.CurrentTask = "완료";
                        }
                    }

                    // 데모용: 주기적으로 유휴 에이전트에 새 작업 부여
                    if (_tick % 4 == 0)
                    {
                        var idle = Agents.Find(x => x.Status == "idle");
                        if (idle != null)
                        {
                            idle.Status = "working";
                            idle.Progress = 5;
                            idle.CurrentTask = $"작업 #{_tick}";
                        }
                    }
                }

                _module?.BroadcastMessageAsync(CurrentAgentsMessage());
            }
            catch (Exception ex)
            {
                LogService.Instance.Error(ex);
            }
        }
    }
}
