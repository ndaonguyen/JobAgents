import type {
  AgentModelConfig, CandidateProfile, ExpandPiece, ImprovementIdea, JdAnalysis,
  ModelOption, PersistedRun, SearchCriteria, SearchInputs,
} from './types';

async function json<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error((await safeMessage(res)) || `Request failed (${res.status})`);
  return res.status === 204 ? (undefined as T) : (res.json() as Promise<T>);
}

async function safeMessage(res: Response): Promise<string | null> {
  try {
    const body = await res.json();
    return body?.message ?? null;
  } catch {
    return null;
  }
}

const jsonHeaders = { 'Content-Type': 'application/json' };

export const api = {
  // ── Job hunt ──────────────────────────────────────────────────────────────
  analyze: (resume: string, preferences: string) =>
    fetch('/api/hunt/analyze', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ resume, preferences }) })
      .then(json<SearchCriteria>),

  expand: (piece: ExpandPiece, match: unknown, criteria: SearchCriteria | null) =>
    fetch(`/api/hunt/expand/${piece}`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ match, criteria }) })
      .then(json),

  // ── JD analyzer ───────────────────────────────────────────────────────────
  jdAnalyze: (resumeText: string, jobDescription: string) =>
    fetch('/api/jd/analyze', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ resumeText, jobDescription }) })
      .then(json<JdAnalysis>),

  // ── Resume file → text ──────────────────────────────────────────────────────
  extract: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return fetch('/api/extract', { method: 'POST', body: form }).then(json<{ text: string }>);
  },

  // ── Past runs ────────────────────────────────────────────────────────────
  runs: () => fetch('/api/runs').then(json<PersistedRun[]>),
  deleteAllRuns: () => fetch('/api/runs', { method: 'DELETE' }).then(json<void>),
  deleteRun: (id: string) => fetch(`/api/runs/${id}`, { method: 'DELETE' }).then(json<void>),
  renameRun: (id: string, title: string) =>
    fetch(`/api/runs/${id}/rename`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify({ title }) }).then(json<void>),
  pinRun: (id: string, pinned: boolean) =>
    fetch(`/api/runs/${id}/pin`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify({ pinned }) }).then(json<void>),

  // ── Ideas ────────────────────────────────────────────────────────────────
  ideas: () => fetch('/api/ideas').then(json<ImprovementIdea[]>),
  ideaStatuses: () => fetch('/api/ideas/statuses').then(json<string[]>),
  addIdea: (title: string, description: string) =>
    fetch('/api/ideas', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ title, description }) }).then(json<ImprovementIdea>),
  updateIdea: (id: string, title: string, description: string) =>
    fetch(`/api/ideas/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify({ title, description }) }).then(json<void>),
  setIdeaStatus: (id: string, status: string) =>
    fetch(`/api/ideas/${id}/status`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify({ status }) }).then(json<void>),
  deleteIdea: (id: string) => fetch(`/api/ideas/${id}`, { method: 'DELETE' }).then(json<void>),

  // ── Profile ──────────────────────────────────────────────────────────────
  profile: () => fetch('/api/profile').then(async (r) => (r.status === 204 ? null : (json<CandidateProfile>(r)))),
  saveProfile: (resumeText: string) =>
    fetch('/api/profile', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ resumeText }) }).then(json<void>),
  deleteProfile: () => fetch('/api/profile', { method: 'DELETE' }).then(json<void>),

  // ── Settings ─────────────────────────────────────────────────────────────
  settings: () => fetch('/api/settings').then(json<AgentModelConfig>),
  saveSettings: (config: AgentModelConfig) =>
    fetch('/api/settings', { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(config) }).then(json<void>),
  catalog: () => fetch('/api/settings/catalog').then(json<ModelOption[]>),

  // ── Feedback ─────────────────────────────────────────────────────────────
  feedback: (body: unknown) =>
    fetch('/api/feedback', { method: 'POST', headers: jsonHeaders, body: JSON.stringify(body) }).then(json<void>),
};

/** A single Server-Sent Event from the hunt stream: an event name plus its parsed JSON payload. */
export interface StreamFrame {
  event: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  data: any;
}

/**
 * POSTs a hunt request and reads the text/event-stream response, invoking `onFrame` for every
 * `event:/data:` frame. Resolves when the stream ends; aborts when `signal` fires.
 */
export async function runHuntStream(
  resume: string,
  inputs: SearchInputs,
  searchBoost: number,
  onFrame: (frame: StreamFrame) => void,
  signal: AbortSignal,
): Promise<void> {
  const res = await fetch('/api/hunt/run', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ resume, inputs, searchBoost }),
    signal,
  });
  if (!res.ok || !res.body) throw new Error(`Hunt failed to start (${res.status})`);

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let sep: number;
    while ((sep = buffer.indexOf('\n\n')) >= 0) {
      const raw = buffer.slice(0, sep);
      buffer = buffer.slice(sep + 2);
      onFrame(parseFrame(raw));
    }
  }
}

function parseFrame(raw: string): StreamFrame {
  let event = 'message';
  const dataLines: string[] = [];
  for (const line of raw.split('\n')) {
    if (line.startsWith('event:')) event = line.slice(6).trim();
    else if (line.startsWith('data:')) dataLines.push(line.slice(5).trim());
  }
  const data = dataLines.join('\n');
  try {
    return { event, data: data ? JSON.parse(data) : null };
  } catch {
    return { event, data };
  }
}
