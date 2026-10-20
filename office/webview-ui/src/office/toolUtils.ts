/** Map status prefixes back to tool names for animation selection */
const STATUS_TO_TOOL: Record<string, string> = {
  Reading: 'Read',
  // Longest prefix first: extractToolName returns the first match.
  'Searching the web': 'WebSearch',
  'Searching web': 'WebSearch',
  'Searching files': 'Glob',
  Searching: 'Grep',
  Globbing: 'Glob',
  Fetching: 'WebFetch',
  Writing: 'Write',
  Editing: 'Edit',
  Running: 'Bash',
  Task: 'Task',
};

export function extractToolName(status: string): string | null {
  for (const [prefix, tool] of Object.entries(STATUS_TO_TOOL)) {
    if (status.startsWith(prefix)) return tool;
  }
  const first = status.split(/[\s:]/)[0];
  return first || null;
}

// ── Provider capabilities (tool taxonomy for rendering decisions) ────────────
// Populated once by the `providerCapabilities` postMessage after `webviewReady`.
// Modules classifying tools (character animation, subagent creation gate) read
// from here instead of hardcoding Claude-specific tool names.

const providerCaps: {
  readingTools: Set<string>;
  subagentToolNames: Set<string>;
} = {
  readingTools: new Set(),
  subagentToolNames: new Set(),
};

export function setProviderCapabilities(caps: {
  readingTools: string[];
  subagentToolNames: string[];
}): void {
  providerCaps.readingTools = new Set(caps.readingTools);
  providerCaps.subagentToolNames = new Set(caps.subagentToolNames);
}

export function isReadingToolName(name: string | null | undefined): boolean {
  return typeof name === 'string' && providerCaps.readingTools.has(name);
}

export function isSubagentToolName(name: string | null | undefined): boolean {
  return typeof name === 'string' && providerCaps.subagentToolNames.has(name);
}

/** A reading-tool status turned into a reference-card row (see ToolOverlay). */
export interface ReferenceItem {
  icon: string;
  text: string;
  /** Set for web pages, so the card can link to them. */
  href?: string;
}

/** Parse the statuses the provider formats for reading tools:
 *  `Reading <path>`, `Fetching <url>`, `Searching the web: <q>`,
 *  `Searching code: <pattern>[ in <path>]`, `Searching files: <glob>`. */
export function parseReference(status: string): ReferenceItem | null {
  let m = /^Fetching (\S+)/.exec(status);
  if (m && m[1] !== 'web') {
    return { icon: '\u{1F310}', text: m[1].replace(/^https?:\/\//, ''), href: m[1] };
  }
  m = /^Searching (?:the )?web: (.+)/.exec(status);
  if (m) return { icon: '\u{1F50E}', text: `"${m[1]}"` };
  m = /^Searching code: (.+)/.exec(status);
  if (m) return { icon: '\u{1F50D}', text: m[1] };
  m = /^Searching files: (.+)/.exec(status);
  if (m) return { icon: '\u{1F5C2}\u{FE0F}', text: m[1] };
  m = /^Reading (.+)/.exec(status);
  if (m) return { icon: '\u{1F4C4}', text: m[1] };
  return null;
}
