/**
 * Agent Hub activity event → the tool vocabulary the office animates.
 *
 * The office picks a room from the tool name (reading tools → 자료실, shell →
 * 작업실, everything else → 사무실) and the reading-room card parses the status
 * text (`Reading <path>`, `Fetching <url>`, `Searching code: <q>` …, see
 * office/toolUtils.ts parseReference). Claude events carry real tool names;
 * Codex runs nearly everything through `exec` (a shell command), so commands
 * are sorted by what they do.
 */

/** Agent Hub ActivityEvent (AgentHub/Common/Models/ActivityEvent.cs), camelCase JSON. */
export interface HubEvent {
  kind: string; // message | thinking | tool_use | tool_result | user_prompt | mode_change
  ts?: string;
  toolName?: string;
  summary?: string;
  text?: string;
  /** Full path / URL / query / shell command of reading tools (Codex exec: the command). */
  target?: string;
  /** tool_use id, or the tool_use a tool_result answers. */
  toolId?: string;
  /** Started with run_in_background (a background shell or agent). */
  background?: boolean;
  /** Background task id (in the tool_result, and in task_done when it finishes). */
  taskId?: string;
}

export function isShellTool(name: string | undefined): boolean {
  return name === 'Bash' || name === 'exec' || name === 'shell' || name === 'shell_command' || name === 'PowerShell';
}

/** Agent Hub summaries are "<ToolName>  <detail>" — the detail. */
export function detailOf(e: HubEvent): string {
  return afterName(e);
}

export interface OfficeTool {
  name: string;
  status: string;
}

export const READING_TOOLS = ['Read', 'Grep', 'Glob', 'WebFetch', 'WebSearch'];
export const SUBAGENT_TOOLS = ['Task', 'Agent'];

const READ_CMD = /^(cat|sed\s+-n|head|tail|less|nl|bat|wc|type|Get-Content)\b/i;
const SEARCH_CMD = /^(rg|grep|ag|find|fd|ls|dir|tree|Select-String|git\s+(log|show|diff|grep|status))\b/i;

export function toOfficeTool(e: HubEvent): OfficeTool {
  const name = e.toolName ?? 'tool';
  const target = e.target?.trim();
  switch (name) {
    case 'Read':
      return { name, status: `Reading ${target ?? afterName(e)}` };
    case 'Grep':
      return { name, status: `Searching code: ${target ?? afterName(e)}` };
    case 'Glob':
      return { name, status: `Searching files: ${target ?? afterName(e)}` };
    case 'WebFetch':
      return { name, status: `Fetching ${target ?? afterName(e)}` };
    case 'WebSearch':
      return { name, status: `Searching the web: ${target ?? afterName(e)}` };
    case 'Bash':
      return { name, status: `Running: ${afterName(e)}` };
    case 'apply_patch':
      return { name: 'Edit', status: `Editing ${target ?? afterName(e)}` };
    case 'exec':
    case 'shell':
    case 'shell_command':
    case 'exec_command':
    case 'local_shell':
      return fromCommand(target ?? afterName(e));
    default:
      if (name.startsWith('request_user_input')) return { name: 'AskUserQuestion', status: 'Waiting for your answer' };
      return { name, status: e.summary || name };
  }
}

/** Sort a shell command by intent: read a file, search, or run something. */
export function fromCommand(cmd: string): OfficeTool {
  const c = cmd.trim();
  if (READ_CMD.test(c)) {
    const file = c.split(/\s+/).find((t, i) => i > 0 && !t.startsWith('-') && /[./\\]/.test(t));
    return { name: 'Read', status: `Reading ${file ?? c}` };
  }
  if (SEARCH_CMD.test(c)) return { name: 'Grep', status: `Searching code: ${c}` };
  return { name: 'Bash', status: `Running: ${c}` };
}

/** Agent Hub summaries are "<ToolName>  <detail>" — keep the detail. */
function afterName(e: HubEvent): string {
  const s = e.summary ?? '';
  const name = e.toolName ?? '';
  return (s.startsWith(name) ? s.slice(name.length) : s).trim() || name;
}
