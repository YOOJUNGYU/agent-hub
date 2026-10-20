using System;
using System.Collections.Generic;
using System.Linq;
using AgentHub.Server.Agents;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentHub.Tests
{
    /// <summary>
    /// 오피스 뷰(캐릭터 방 이동·자료실 카드·생각 말풍선)가 쓰는 활동 이벤트:
    /// 신형 Codex custom_tool_call(exec/apply_patch), reasoning 요약, 조회 도구의 전체 대상(Target).
    /// </summary>
    public class OfficeActivityTests
    {
        private static readonly DateTime Now = DateTime.Parse("2026-10-20T05:00:10Z").ToUniversalTime();

        private static string Exec(string callId, string cmd) =>
            new JObject
            {
                ["timestamp"] = "2026-10-20T05:00:01Z",
                ["type"] = "response_item",
                ["payload"] = new JObject
                {
                    ["type"] = "custom_tool_call", ["call_id"] = callId, ["name"] = "exec",
                    ["input"] = "text(await tools.exec_command({cmd:" + JToken.FromObject(cmd).ToString(Newtonsoft.Json.Formatting.None) + ",\"max_output_tokens\":2000}));"
                }
            }.ToString(Newtonsoft.Json.Formatting.None);

        private static string Line(string type, JObject payload, string ts = "2026-10-20T05:00:02Z") =>
            new JObject { ["timestamp"] = ts, ["type"] = type, ["payload"] = payload }.ToString(Newtonsoft.Json.Formatting.None);

        [Fact]
        public void Codex_exec_becomes_tool_use_with_the_shell_command()
        {
            var ev = CodexTranscriptParser.ParseEvents(new List<string>
            {
                Exec("c1", "rg -n \"VisitStatus\" src"),
                Line("response_item", new JObject { ["type"] = "custom_tool_call_output", ["call_id"] = "c1", ["output"] = "ok" }),
            }, 200);

            Assert.Equal(new[] { "tool_use", "tool_result" }, ev.Select(e => e.Kind));
            Assert.Equal("exec", ev[0].ToolName);
            Assert.Equal("rg -n \"VisitStatus\" src", ev[0].Target);
            Assert.Contains("rg -n", ev[0].Summary);
        }

        [Fact]
        public void Codex_apply_patch_targets_the_patched_file()
        {
            var patch = "*** Begin Patch\n*** Update File: src/modules/visit-status.ts\n@@\n-a\n+b\n*** End Patch";
            var ev = CodexTranscriptParser.ParseEvents(new List<string>
            {
                Line("response_item", new JObject { ["type"] = "custom_tool_call", ["call_id"] = "c2", ["name"] = "apply_patch", ["input"] = patch }),
            }, 200);

            Assert.Equal("src/modules/visit-status.ts", ev.Single().Target);
            Assert.Equal("apply_patch  src/modules/visit-status.ts", ev.Single().Summary);
        }

        [Fact]
        public void Codex_summary_tracks_custom_tool_calls_as_the_current_task()
        {
            var lines = new List<string>
            {
                Line("session_meta", new JObject { ["cwd"] = "/home/u/proj" }, "2026-10-20T05:00:00Z"),
                Line("event_msg", new JObject { ["type"] = "task_started" }, "2026-10-20T05:00:00Z"),
                Exec("c1", "pnpm test visit-status"),
            };

            var s = CodexTranscriptParser.Summarize("sid", lines, Now);

            Assert.Equal("exec", s.ToolName);
            Assert.Contains("pnpm test visit-status", s.CurrentTask);
            Assert.True(s.Working); // 결과(custom_tool_call_output)가 아직 없음
        }

        [Fact]
        public void Codex_reasoning_summary_text_is_kept()
        {
            var ev = CodexTranscriptParser.ParseEvents(new List<string>
            {
                Line("response_item", new JObject
                {
                    ["type"] = "reasoning",
                    ["summary"] = new JArray(new JObject { ["type"] = "summary_text", ["text"] = "정렬 훅부터 본다" }),
                    ["encrypted_content"] = "x"
                }),
                Line("response_item", new JObject { ["type"] = "reasoning", ["summary"] = new JArray(), ["encrypted_content"] = "y" }),
            }, 200);

            Assert.Equal("정렬 훅부터 본다", ev[0].Text);
            Assert.Null(ev[1].Text); // 암호화만 있는 사고는 본문 없음
        }

        [Fact]
        public void Claude_reading_tools_carry_their_full_target()
        {
            Assert.Equal("/home/u/a/b.ts", TranscriptParser.ToolTarget("Read", new JObject { ["file_path"] = "/home/u/a/b.ts" }));
            Assert.Equal("https://x.dev/p", TranscriptParser.ToolTarget("WebFetch", new JObject { ["url"] = "https://x.dev/p" }));
            Assert.Equal("foo  in src", TranscriptParser.ToolTarget("Grep", new JObject { ["pattern"] = "foo", ["path"] = "src" }));
            Assert.Null(TranscriptParser.ToolTarget("Bash", new JObject { ["command"] = "ls" }));
        }
    }
}
