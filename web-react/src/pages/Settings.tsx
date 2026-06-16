import { useEffect, useState } from 'react';
import { api } from '../api';
import type { AgentModelConfig, ModelOption, SearchDepthSettings } from '../types';

const DEFAULT_DEPTH: SearchDepthSettings = {
  sourcing: true, sourcingFallback: false, companyResearch: false, salaryAnalysis: false,
};

const AGENT_SLOTS: { label: string; key: keyof AgentModelConfig }[] = [
  { label: 'Coordinator', key: 'coordinatorModel' },
  { label: 'Search', key: 'searchModel' },
  { label: 'Resume match', key: 'resumeMatchModel' },
  { label: 'Company research', key: 'companyResearchModel' },
  { label: 'Salary analysis', key: 'salaryAnalysisModel' },
  { label: 'Interview prep', key: 'interviewPrepModel' },
];

const DEPTH_SLOTS: { key: keyof SearchDepthSettings; label: string; desc: string }[] = [
  { key: 'sourcing', label: 'Job sourcing (selected sites)', desc: 'The Search agent’s main query on the sites you pick — niche boards need advanced.' },
  { key: 'sourcingFallback', label: 'Job sourcing whole-web fallback', desc: 'Retry across the open web when the site-restricted query finds nothing.' },
  { key: 'companyResearch', label: 'Company research', desc: 'Looking up culture, reputation and news for a matched employer.' },
  { key: 'salaryAnalysis', label: 'Salary analysis', desc: 'Market salary range for the role, location and seniority.' },
];

const LIMIT_SLOTS: { key: keyof AgentModelConfig; label: string; desc: string }[] = [
  { key: 'maxResumeChars', label: 'Resume', desc: 'CV characters sent to the matcher. 0 = no cap.' },
  { key: 'maxDescriptionChars', label: 'Job description', desc: 'Posting-description characters sent to the matcher. 0 = no cap.' },
  { key: 'maxSearchResultChars', label: 'Web-search result body', desc: 'Characters kept from each web result. 0 = no cap.' },
];

export default function Settings() {
  const [config, setConfig] = useState<AgentModelConfig | null>(null);
  const [catalog, setCatalog] = useState<ModelOption[]>([]);
  const [status, setStatus] = useState('');

  useEffect(() => {
    api.settings().then(setConfig);
    api.catalog().then(setCatalog);
  }, []);

  if (!config) return <p className="text-muted">Loading…</p>;
  const depth = config.searchDepth ?? DEFAULT_DEPTH;

  const save = async (next: AgentModelConfig) => {
    setConfig(next);
    await api.saveSettings(next);
    setStatus('Saved ✓');
  };

  return (
    <div style={{ maxWidth: 900 }}>
      <div className="d-flex flex-wrap align-items-center justify-content-between mb-4">
        <div>
          <h1 className="mb-1">Settings</h1>
          <p className="text-muted mb-0">Saved automatically on every change, applied to your next hunt.</p>
        </div>
        {status && <span className="badge bg-success">{status}</span>}
      </div>

      <section className="card mb-4">
        <div className="card-header">Search behaviour</div>
        <div className="card-body">
          <div className="form-check form-switch">
            <input className="form-check-input" type="checkbox" role="switch" checked={config.parallelSearch}
                   onChange={(e) => save({ ...config, parallelSearch: e.target.checked })} />
            <label className="form-check-label">
              Parallel search
              <span className="text-muted d-block small">On: research 2 matches at once. Off: one by one.</span>
            </label>
          </div>
        </div>
      </section>

      <section className="card mb-4">
        <div className="card-header">Search depth per call</div>
        <div className="card-body">
          <p className="text-muted small mb-3">Tavily bills <strong>advanced</strong> at 2 credits, <strong>basic</strong> at 1.</p>
          {DEPTH_SLOTS.map((d) => {
            const advanced = depth[d.key];
            return (
              <div className="d-flex justify-content-between align-items-center border-bottom py-2" key={d.key}>
                <div>
                  <span className="fw-semibold">{d.label}</span>
                  <span className="text-muted small d-block">{d.desc}</span>
                </div>
                <div className="d-flex align-items-center gap-2">
                  <span className={`badge ${advanced ? 'bg-warning text-dark' : 'bg-secondary'}`}>
                    {advanced ? 'advanced · 2 credits' : 'basic · 1 credit'}
                  </span>
                  <div className="form-check form-switch m-0">
                    <input className="form-check-input" type="checkbox" role="switch" checked={advanced}
                           onChange={(e) => save({ ...config, searchDepth: { ...depth, [d.key]: e.target.checked } })} />
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </section>

      <section className="card mb-4">
        <div className="card-header">Input limits (truncation)</div>
        <div className="card-body">
          <p className="text-muted small mb-3">Cap text each agent sees to cut tokens / cost. <strong>0 = no cap.</strong></p>
          <div className="row g-3">
            {LIMIT_SLOTS.map((l) => (
              <div className="col-12 col-md-4" key={l.key}>
                <label className="form-label fw-semibold">{l.label}</label>
                <input type="number" min={0} step={100} className="form-control form-control-sm"
                       value={config[l.key] as number}
                       onChange={(e) => {
                         const n = Math.max(0, Number(e.target.value) || 0);
                         save({ ...config, [l.key]: n });
                       }} />
                <span className="text-muted small d-block">{l.desc}</span>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="card mb-4">
        <div className="card-header">Agent models</div>
        <div className="card-body">
          <div className="row g-3">
            {AGENT_SLOTS.map((slot) => (
              <div className="col-12 col-md-6 col-lg-4" key={slot.key}>
                <label className="form-label fw-semibold">{slot.label}</label>
                <select className="form-select form-select-sm" value={(config[slot.key] as string) ?? ''}
                        onChange={(e) => save({ ...config, [slot.key]: e.target.value || null })}>
                  {catalog.map((opt) => <option key={opt.id} value={opt.id}>{opt.label}</option>)}
                </select>
              </div>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}
