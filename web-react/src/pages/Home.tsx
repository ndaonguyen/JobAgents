import { useEffect, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { api, runHuntStream } from '../api';
import type { AgentEvent, JobDossier, JobHuntResult, SearchCriteria, SearchInputs } from '../types';
import DossierCard, { type ScoreSubmission } from '../components/DossierCard';
import { sourceHost } from '../sourceHost';

const ROLE_OPTIONS = [
  'Backend Engineer', 'Frontend Engineer', 'Full-stack Engineer',
  'Senior Backend Engineer', 'Senior Frontend Engineer', 'Senior Full-stack Engineer',
  'Staff Engineer', 'Tech Lead', 'Engineering Manager',
  'DevOps / SRE', 'Data Engineer', 'Machine Learning Engineer',
];
const LANGUAGE_OPTIONS = ['C#', 'Python', 'JavaScript', 'TypeScript', 'Java', 'Go', 'Rust', 'C++', 'Ruby', 'PHP', 'Kotlin', 'Swift'];
const WORKING_STYLES = ['Onsite', 'Hybrid', 'Remote'];
const CURRENCIES = ['USD', 'GBP', 'EUR', 'AUD', 'NZD', 'CAD', 'VND'];
const POSTED_WITHIN: [string, string][] = [['Any time', ''], ['Past week', 'week'], ['Past month', 'month'], ['Past year', 'year']];
const CITY_OPTIONS = [
  'Ho Chi Minh City', 'Hanoi', 'Da Nang', 'Singapore', 'Bangkok', 'Kuala Lumpur', 'Jakarta', 'Manila',
  'Tokyo', 'Seoul', 'Hong Kong', 'Shanghai', 'Beijing', 'Taipei', 'Bangalore', 'Mumbai', 'Hyderabad',
  'Sydney', 'Melbourne', 'Auckland', 'London', 'Dublin', 'Berlin', 'Munich', 'Amsterdam', 'Paris',
  'Madrid', 'Barcelona', 'Lisbon', 'Zurich', 'Stockholm', 'Warsaw', 'Dubai', 'Tel Aviv',
  'New York', 'San Francisco', 'Seattle', 'Austin', 'Boston', 'Los Angeles', 'Chicago', 'Toronto', 'Vancouver',
];
const ANYWHERE_SOURCE = 'Anywhere (web)';
const SOURCE_OPTIONS = ['ITviec', 'VietnamWorks', 'LinkedIn', 'TopCV', ANYWHERE_SOURCE];
const SEARCH_EFFORTS: [string, number, string][] = [['Light', 4, '(4)'], ['Normal', 6, '(6)'], ['Deep', 10, '(10)']];
const PAGE_SIZE = 5;

interface Activity { icon: string; title: string; detail: string; agentId?: string; }

function useToggleSet(initial: string[] = []) {
  const [set, setSet] = useState<Set<string>>(new Set(initial));
  const toggle = (v: string, on: boolean) => setSet((prev) => {
    const next = new Set(prev);
    if (on) next.add(v); else next.delete(v);
    return next;
  });
  return [set, setSet, toggle] as const;
}

const splitCsv = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean);
const truncate = (s: string, n: number) => (s.length <= n ? s : s.slice(0, n) + '…');
const dossierKey = (d: JobDossier) => d.match.posting.url || `${d.match.posting.title}|${d.match.posting.company}`;

export default function Home() {
  const location = useLocation();

  const [resume, setResume] = useState('');
  const [resumeMode, setResumeMode] = useState<'paste' | 'upload'>('paste');
  const [uploadStatus, setUploadStatus] = useState<{ msg: string; failed: boolean } | null>(null);
  const [savedCvExists, setSavedCvExists] = useState(false);
  const [profileStatus, setProfileStatus] = useState<string | null>(null);

  const [selectedRoles, , toggleRole] = useToggleSet();
  const [selectedLangs, , toggleLang] = useToggleSet();
  const [workingStyles, , toggleStyle] = useToggleSet();
  const [selectedSources, , toggleSource] = useToggleSet();
  const [location_, setLocation] = useState('Ho Chi Minh City');
  const [customLocation, setCustomLocation] = useState('');
  const [salaryMin, setSalaryMin] = useState<string>('');
  const [salaryMax, setSalaryMax] = useState<string>('');
  const [currency, setCurrency] = useState('USD');
  const [other, setOther] = useState('');
  const [minMatchScore, setMinMatchScore] = useState(60);
  const [searchEffort, setSearchEffort] = useState(6);
  const [postedWithin, setPostedWithin] = useState('month');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [researchCompany, setResearchCompany] = useState(false);
  const [researchSalary, setResearchSalary] = useState(false);

  const [analyzing, setAnalyzing] = useState(false);
  const [criteria, setCriteria] = useState<SearchCriteria | null>(null);
  const [mustHave, setMustHave] = useState('');
  const [niceToHave, setNiceToHave] = useState('');
  const [editWorkStyles, , toggleEditStyle] = useToggleSet();

  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [activity, setActivity] = useState<Activity[]>([]);
  const [result, setResult] = useState<JobHuntResult | null>(null);
  const [dossiers, setDossiers] = useState<JobDossier[]>([]);
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);
  const [lastRunId, setLastRunId] = useState<string | null>(null);
  const [searchBoost, setSearchBoost] = useState(0);

  const [costs, setCosts] = useState({ search: 0, match: 0, other: 0, tavily: 0, searchTavily: 0, researchTavily: 0 });
  const [researching, setResearching] = useState<{ company: Set<string>; salary: Set<string>; interview: Set<string> }>(
    { company: new Set(), salary: new Set(), interview: new Set() });

  const abortRef = useRef<AbortController | null>(null);
  const hasExactDates = !!fromDate || !!toDate;
  const effectiveLocation = customLocation.trim() || location_;

  useEffect(() => {
    api.profile().then((p) => {
      if (p?.resumeText) {
        setResume(p.resumeText);
        setSavedCvExists(true);
        setProfileStatus(`Loaded your saved CV (updated ${p.updatedAtUtc.slice(0, 10)}).`);
      }
    });
    // "Run again" deep-link from Past Runs.
    const state = location.state as { inputs?: SearchInputs; autoRun?: boolean } | null;
    if (state?.inputs) applyInputs(state.inputs);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function applyInputs(i: SearchInputs) {
    if (i.roles) (i.roles).forEach((r) => toggleRole(r, true));
    if (i.languages) i.languages.forEach((l) => toggleLang(l, true));
    if (i.workingStyles) i.workingStyles.forEach((s) => toggleStyle(s, true));
    const loc = i.location ?? '';
    if (loc === '' || loc === 'Remote' || CITY_OPTIONS.includes(loc)) { setLocation(loc); setCustomLocation(''); }
    else { setLocation(''); setCustomLocation(loc); }
    setSalaryMin(i.salaryMin?.toString() ?? '');
    setSalaryMax(i.salaryMax?.toString() ?? '');
    setCurrency(i.currency || 'USD');
    setOther(i.other ?? '');
    (i.sources ?? []).forEach((s) => toggleSource(s, true));
    setMinMatchScore(Math.min(100, Math.max(0, i.minMatchScore)));
    setSearchEffort(SEARCH_EFFORTS.some(([, v]) => v === i.searchEffort) ? i.searchEffort : 6);
    setResearchCompany(i.researchCompany);
    setResearchSalary(i.researchSalary);
    setPostedWithin(POSTED_WITHIN.some(([, v]) => v === (i.postedWithin ?? '')) ? (i.postedWithin ?? '') : 'month');
    setFromDate(i.startDate ?? '');
    setToDate(i.endDate ?? '');
    if (i.criteria) {
      setCriteria(i.criteria);
      setMustHave(i.criteria.mustHaveSkills.join(', '));
      setNiceToHave(i.criteria.niceToHaveSkills.join(', '));
      i.criteria.workStyles.forEach((s) => toggleEditStyle(s, true));
    }
  }

  function buildPreferences(): string {
    const lines: string[] = [];
    const roles = ROLE_OPTIONS.filter((r) => selectedRoles.has(r));
    const langs = LANGUAGE_OPTIONS.filter((l) => selectedLangs.has(l));
    const styles = WORKING_STYLES.filter((s) => workingStyles.has(s));
    if (roles.length) lines.push(`Target roles: ${roles.join(', ')}`);
    if (langs.length) lines.push(`Languages / tech: ${langs.join(', ')}`);
    if (styles.length) lines.push(`Working style: ${styles.join(', ')}`);
    if (effectiveLocation) lines.push(`Location: ${effectiveLocation}`);
    return lines.join('\n');
  }

  function currentInputs(): SearchInputs {
    const overrideCriteria: SearchCriteria | null = criteria
      ? { ...criteria, mustHaveSkills: splitCsv(mustHave), niceToHaveSkills: splitCsv(niceToHave), workStyles: WORKING_STYLES.filter((s) => editWorkStyles.has(s)) }
      : null;
    return {
      roles: ROLE_OPTIONS.filter((r) => selectedRoles.has(r)),
      languages: LANGUAGE_OPTIONS.filter((l) => selectedLangs.has(l)),
      workingStyles: WORKING_STYLES.filter((s) => workingStyles.has(s)),
      location: effectiveLocation,
      salaryMin: salaryMin ? Number(salaryMin) : null,
      salaryMax: salaryMax ? Number(salaryMax) : null,
      currency,
      other: other.trim(),
      sources: SOURCE_OPTIONS.filter((s) => selectedSources.has(s)),
      minMatchScore,
      postedWithin,
      startDate: fromDate || null,
      endDate: toDate || null,
      criteria: overrideCriteria,
      searchEffort,
      researchCompany,
      researchSalary,
    };
  }

  async function onUpload(file: File | undefined) {
    if (!file) return;
    try {
      const { text } = await api.extract(file);
      setResume(text);
      setUploadStatus({ msg: `Loaded ${file.name} (${text.length.toLocaleString()} characters).`, failed: false });
    } catch (e) {
      setUploadStatus({ msg: (e as Error).message, failed: true });
    }
  }

  const saveCv = async () => {
    if (!resume.trim()) return;
    try { await api.saveProfile(resume); setSavedCvExists(true); setProfileStatus("CV saved — it'll be here next time."); }
    catch (e) { setProfileStatus(`Couldn't save CV: ${(e as Error).message}`); }
  };
  const forgetCv = async () => { await api.deleteProfile(); setSavedCvExists(false); setProfileStatus('Saved CV removed.'); };

  const analyze = async () => {
    if (analyzing || running || !resume.trim()) return;
    setAnalyzing(true); setError(null);
    try {
      const c = await api.analyze(resume, buildPreferences());
      setCriteria(c);
      setMustHave(c.mustHaveSkills.join(', '));
      setNiceToHave(c.niceToHaveSkills.join(', '));
      const styles = c.workStyles.length ? c.workStyles : WORKING_STYLES.filter((s) => workingStyles.has(s));
      styles.forEach((s) => toggleEditStyle(s, true));
    } catch (e) { setError(`Couldn't analyze criteria: ${(e as Error).message}`); }
    finally { setAnalyzing(false); }
  };

  function handleEvent(evt: AgentEvent) {
    switch (evt.kind) {
      case 'agent.started':
        setActivity((a) => [...a, { icon: '▶️', title: `${evt.role} started`, detail: '', agentId: evt.agentId.value }]);
        break;
      case 'agent.token':
        setActivity((a) => a.map((it) => it.agentId === evt.agentId.value && it.icon === '▶️' ? { ...it, detail: it.detail + (evt.delta ?? '') } : it));
        break;
      case 'agent.finished':
        if (evt.agentId.value !== 'system') {
          const spent = evt.estimatedCostUsd ?? 0;
          setCosts((c) => evt.agentId.value === 'search' ? { ...c, search: c.search + spent }
            : evt.agentId.value.startsWith('resume-match') ? { ...c, match: c.match + spent }
            : { ...c, other: c.other + spent });
          setActivity((a) => a.map((it) => it.agentId === evt.agentId.value && it.icon === '▶️' ? { ...it, icon: '✅' } : it));
        }
        break;
      case 'tool.called':
        setActivity((a) => [...a, { icon: '🔧', title: evt.toolName ?? 'tool', detail: truncate(evt.argumentsJson ?? '', 140) }]);
        break;
      case 'websearch.requested':
        setCosts((c) => {
          const n = c.tavily + 1;
          setActivity((a) => [...a, { icon: '🌐', title: `Web search #${n}${evt.isFallback ? ' (fallback)' : ''}`, detail: truncate(evt.query ?? '', 140) }]);
          return evt.agentId.value === 'search'
            ? { ...c, tavily: n, searchTavily: c.searchTavily + 1 }
            : { ...c, tavily: n, researchTavily: c.researchTavily + 1 };
        });
        break;
      case 'jobs.found': {
        const bySource = Object.entries((evt.postings ?? []).reduce<Record<string, number>>((m, p) => {
          const h = sourceHost(p.url); m[h] = (m[h] ?? 0) + 1; return m;
        }, {})).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k}: ${v}`).join(' · ');
        setActivity((a) => [...a, { icon: '🔎', title: `Found ${evt.postings?.length ?? 0} postings`, detail: bySource }]);
        break;
      }
      case 'job.matched':
        setActivity((a) => [...a, { icon: '📋', title: `${evt.match?.posting.title} @ ${evt.match?.posting.company}`, detail: `Fit ${evt.match?.score}/100` }]);
        break;
      case 'company.researched':
        setActivity((a) => [...a, { icon: '🏢', title: `Researched ${evt.insight?.company}`, detail: '' }]);
        break;
      case 'salary.analyzed':
        setActivity((a) => [...a, { icon: '💰', title: 'Salary analysed', detail: evt.estimate?.basis ?? '' }]);
        break;
      case 'interview.prep':
        setActivity((a) => [...a, { icon: '🎤', title: `Interview prep ready (${evt.prep?.likelyQuestions.length ?? 0} questions)`, detail: '' }]);
        break;
    }
  }

  async function runHunt(boost: number) {
    if (running) return;
    setRunning(true); setError(null); setResult(null); setDossiers([]);
    setActivity([]); setVisibleCount(PAGE_SIZE);
    setCosts({ search: 0, match: 0, other: 0, tavily: 0, searchTavily: 0, researchTavily: 0 });
    const inputs = currentInputs();
    try { await api.saveProfile(resume); setSavedCvExists(true); } catch { /* best-effort */ }

    const ctrl = new AbortController();
    abortRef.current = ctrl;
    try {
      await runHuntStream(resume, inputs, boost, (frame) => {
        if (frame.event === 'fatal') { setError(frame.data?.message ?? 'Run failed.'); return; }
        if (frame.event === 'run.saved') { setLastRunId(frame.data?.runId ?? null); return; }
        if (frame.event === 'done') return;
        const evt = frame.data as AgentEvent;
        handleEvent(evt);
        if (evt.kind === 'agent.finished' && evt.agentId.value === 'system' && evt.finalText) {
          try {
            const r = JSON.parse(evt.finalText) as JobHuntResult;
            setResult(r); setDossiers(r.dossiers);
          } catch { setError("The run didn't return a usable result."); }
        }
        if (evt.kind === 'agent.error' && evt.agentId.value === 'system') setError(evt.message ?? 'Run failed.');
      }, ctrl.signal);
    } catch (e) {
      if ((e as Error).name !== 'AbortError') setError((e as Error).message);
    } finally {
      setRunning(false);
    }
  }

  const totalCost = costs.search + costs.match + costs.other;

  async function researchPiece(d: JobDossier, piece: 'company' | 'salary' | 'interview') {
    const key = dossierKey(d);
    setResearching((r) => ({ ...r, [piece]: new Set(r[piece]).add(key) }));
    try {
      const res = await api.expand(piece, d.match, result?.criteria ?? null);
      setDossiers((list) => list.map((x) => dossierKey(x) === key
        ? piece === 'company' ? { ...x, company: res as JobDossier['company'] }
        : piece === 'salary' ? { ...x, salary: res as JobDossier['salary'] }
        : { ...x, interview: res as JobDossier['interview'] }
        : x));
    } catch (e) {
      setError(`Couldn't research ${piece}: ${(e as Error).message}`);
    } finally {
      setResearching((r) => { const next = new Set(r[piece]); next.delete(key); return { ...r, [piece]: next }; });
    }
  }

  async function saveFeedback(d: JobDossier, s: ScoreSubmission) {
    if (!result || !resume.trim()) return;
    try {
      await api.feedback({
        runId: lastRunId ?? '', posting: d.match.posting, criteria: result.criteria,
        agentScore: d.match.score, agentMatchedSkills: d.match.matchedSkills,
        resume, humanScore: s.score, note: s.note,
      });
    } catch { /* best-effort */ }
  }

  const checkboxGrid = (options: string[], set: Set<string>, toggle: (v: string, on: boolean) => void, idPrefix: string, scroll = true) => (
    <div className={`border rounded p-2 ${scroll ? '' : ''}`} style={scroll ? { maxHeight: 150, overflowY: 'auto' } : undefined}>
      {options.map((o) => (
        <div className="form-check" key={o}>
          <input className="form-check-input" type="checkbox" id={`${idPrefix}-${o}`}
                 checked={set.has(o)} onChange={(e) => toggle(o, e.target.checked)} />
          <label className="form-check-label" htmlFor={`${idPrefix}-${o}`}>{o}</label>
        </div>
      ))}
    </div>
  );

  return (
    <div className="row g-4">
      <div className="col-12">
        <h1>Job Hunt</h1>
        <p className="text-muted">Paste your resume and what you're after. A coordinator agent sources live jobs, matches them to you, then researches company, salary and interview prep for your best matches.</p>
      </div>

      <div className="col-lg-6">
        {/* Resume */}
        <div className="mb-3">
          <label className="form-label fw-bold d-block">Resume</label>
          <div className="btn-group btn-group-sm mb-2">
            <button type="button" className={`btn ${resumeMode === 'paste' ? 'btn-primary' : 'btn-outline-primary'}`} onClick={() => setResumeMode('paste')}>Paste text</button>
            <button type="button" className={`btn ${resumeMode === 'upload' ? 'btn-primary' : 'btn-outline-primary'}`} onClick={() => setResumeMode('upload')}>Upload file</button>
          </div>
          {resumeMode === 'paste' ? (
            <textarea className="form-control" rows={10} value={resume} onChange={(e) => setResume(e.target.value)} placeholder="Paste your resume text here..." />
          ) : (
            <>
              <input type="file" className="form-control" accept=".txt,.md,.pdf,.docx" onChange={(e) => onUpload(e.target.files?.[0])} />
              <div className="form-text">PDF, Word (.docx), or text (.txt, .md). Max 5 MB.</div>
              {uploadStatus && <div className={`small mt-1 ${uploadStatus.failed ? 'text-danger' : 'text-success'}`}>{uploadStatus.msg}</div>}
              {resume && <div className="border rounded p-2 mt-2 bg-light" style={{ maxHeight: 200, overflowY: 'auto', whiteSpace: 'pre-wrap', fontSize: '0.85rem' }}>{resume}</div>}
            </>
          )}
          <div className="mt-2 d-flex align-items-center gap-2 flex-wrap">
            <button type="button" className="btn btn-sm btn-outline-secondary" onClick={saveCv} disabled={!resume.trim()}>💾 Save CV</button>
            {savedCvExists && <button type="button" className="btn btn-sm btn-outline-danger" onClick={forgetCv}>🗑 Forget saved CV</button>}
            {profileStatus && <span className="small text-muted">{profileStatus}</span>}
          </div>
        </div>

        {/* Preferences */}
        <label className="form-label fw-bold d-block">Preferences</label>
        <div className="row g-2">
          <div className="col-sm-6">
            <label className="form-label small mb-0">Roles <span className="text-muted">(pick any)</span></label>
            {checkboxGrid(ROLE_OPTIONS, selectedRoles, toggleRole, 'role')}
          </div>
          <div className="col-sm-6">
            <label className="form-label small mb-0">Languages / tech <span className="text-muted">(pick any)</span></label>
            {checkboxGrid(LANGUAGE_OPTIONS, selectedLangs, toggleLang, 'lang')}
          </div>
        </div>

        <div className="mt-2">
          <label className="form-label small mb-0 d-block">Working style</label>
          {WORKING_STYLES.map((s) => (
            <div className="form-check form-check-inline" key={s}>
              <input className="form-check-input" type="checkbox" id={`ws-${s}`} checked={workingStyles.has(s)} onChange={(e) => toggleStyle(s, e.target.checked)} />
              <label className="form-check-label" htmlFor={`ws-${s}`}>{s}</label>
            </div>
          ))}
        </div>

        <div className="mt-2">
          <label className="form-label small mb-0 d-block">Sources <span className="text-muted">(which sites to scan — none = whole web)</span></label>
          {SOURCE_OPTIONS.map((s) => (
            <div className="form-check form-check-inline" key={s}>
              <input className="form-check-input" type="checkbox" id={`src-${s}`} checked={selectedSources.has(s)} onChange={(e) => toggleSource(s, e.target.checked)} />
              <label className="form-check-label" htmlFor={`src-${s}`}>{s}</label>
            </div>
          ))}
        </div>

        <div className="row g-2 mt-1 align-items-end">
          <div className="col-sm-4">
            <label className="form-label small mb-0">Location</label>
            <select className="form-select" value={location_} disabled={!!customLocation.trim()} onChange={(e) => setLocation(e.target.value)}>
              <option value="">Anywhere</option>
              <option value="Remote">Remote</option>
              {CITY_OPTIONS.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
            <input className="form-control form-control-sm mt-1" value={customLocation} onChange={(e) => setCustomLocation(e.target.value)} placeholder="or type a custom location" />
          </div>
          <div className="col-sm-5">
            <label className="form-label small mb-0">Expected salary range</label>
            <div className="input-group">
              <input type="number" className="form-control" value={salaryMin} onChange={(e) => setSalaryMin(e.target.value)} placeholder="min" min={0} />
              <span className="input-group-text">–</span>
              <input type="number" className="form-control" value={salaryMax} onChange={(e) => setSalaryMax(e.target.value)} placeholder="max" min={0} />
            </div>
          </div>
          <div className="col-sm-3">
            <label className="form-label small mb-0">Currency</label>
            <select className="form-select" value={currency} onChange={(e) => setCurrency(e.target.value)}>
              {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </div>
        </div>

        <div className="row g-2 mt-1 align-items-end">
          <div className="col-sm-4">
            <label className="form-label small mb-0">Posted within</label>
            <select className="form-select" value={postedWithin} disabled={hasExactDates} onChange={(e) => setPostedWithin(e.target.value)}>
              {POSTED_WITHIN.map(([label, value]) => <option key={value} value={value}>{label}</option>)}
            </select>
          </div>
          <div className="col-sm-8">
            <label className="form-label small mb-0">Or exact dates <span className="text-muted">(overrides the above)</span></label>
            <div className="input-group">
              <input type="date" className="form-control" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
              <span className="input-group-text">→</span>
              <input type="date" className="form-control" value={toDate} onChange={(e) => setToDate(e.target.value)} />
              {hasExactDates && <button className="btn btn-outline-secondary" type="button" onClick={() => { setFromDate(''); setToDate(''); }}>Clear</button>}
            </div>
          </div>
        </div>

        <div className="mt-2">
          <label className="form-label small mb-0">Minimum fit score: <strong>{minMatchScore}</strong>/100</label>
          <input type="range" className="form-range" min={0} max={100} step={5} value={minMatchScore} onChange={(e) => setMinMatchScore(Number(e.target.value))} />
        </div>

        <div className="mt-2">
          <label className="form-label small mb-0 d-block">Search effort <span className="text-muted">(how many web searches to run)</span></label>
          {SEARCH_EFFORTS.map(([label, value, hint]) => (
            <div className="form-check form-check-inline" key={value}>
              <input className="form-check-input" type="radio" name="searchEffort" id={`effort-${value}`} checked={searchEffort === value} onChange={() => setSearchEffort(value)} />
              <label className="form-check-label small" htmlFor={`effort-${value}`}>{label} <span className="text-muted">{hint}</span></label>
            </div>
          ))}
        </div>

        <div className="mt-2">
          <label className="form-label small mb-0">Anything else <span className="text-muted">(optional)</span></label>
          <textarea className="form-control" rows={2} value={other} onChange={(e) => setOther(e.target.value)} placeholder="e.g. fintech domains, no on-call, visa sponsorship" />
        </div>

        {/* Analyze */}
        <div className="d-flex flex-wrap align-items-center gap-2 my-2">
          <button type="button" className="btn btn-outline-primary" onClick={analyze} disabled={running || analyzing || !resume.trim()}>
            {analyzing ? <><span className="spinner-border spinner-border-sm me-2" /> Analyzing…</> : 'Analyze criteria'}
          </button>
          <span className="text-muted small">Optional — review inferred skills before searching.</span>
        </div>

        {criteria && (
          <div className="card border-primary mb-3">
            <div className="card-body py-2">
              <h6 className="card-title mb-2">Review &amp; edit criteria</h6>
              <div className="mb-2">
                <label className="form-label small mb-0">Must-have skills <span className="text-muted">(comma-separated)</span></label>
                <input className="form-control form-control-sm" value={mustHave} onChange={(e) => setMustHave(e.target.value)} />
              </div>
              <div className="mb-2">
                <label className="form-label small mb-0">Nice-to-have skills <span className="text-muted">(comma-separated)</span></label>
                <input className="form-control form-control-sm" value={niceToHave} onChange={(e) => setNiceToHave(e.target.value)} />
              </div>
              <div>
                <label className="form-label small mb-0 d-block">Work mode</label>
                {WORKING_STYLES.map((s) => (
                  <div className="form-check form-check-inline" key={s}>
                    <input className="form-check-input" type="checkbox" id={`wm-${s}`} checked={editWorkStyles.has(s)} onChange={(e) => toggleEditStyle(s, e.target.checked)} />
                    <label className="form-check-label" htmlFor={`wm-${s}`}>{s}</label>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}

        <div className="mb-3">
          <label className="form-label small mb-1 d-block">Extra research <span className="text-muted">(off by default — each adds web searches)</span></label>
          <div className="btn-group btn-group-sm">
            <button type="button" className={`btn ${researchCompany ? 'btn-primary' : 'btn-outline-primary'}`} onClick={() => setResearchCompany(!researchCompany)}>🏢 Company research: {researchCompany ? 'on' : 'off'}</button>
            <button type="button" className={`btn ${researchSalary ? 'btn-primary' : 'btn-outline-primary'}`} onClick={() => setResearchSalary(!researchSalary)}>💰 Salary research: {researchSalary ? 'on' : 'off'}</button>
          </div>
        </div>

        <button className="btn btn-primary" onClick={() => { setSearchBoost(0); runHunt(0); }} disabled={running || !resume.trim()}>
          {running ? <><span className="spinner-border spinner-border-sm me-2" /> Hunting…</> : (criteria ? 'Search with these' : 'Find jobs')}
        </button>
        {result && !running && (
          <button type="button" className="btn btn-outline-secondary btn-sm ms-3" disabled={!resume.trim()}
                  onClick={() => { const b = searchBoost + 1; setSearchBoost(b); runHunt(b); }}>🔁 Search harder</button>
        )}

        {error && <div className="alert alert-danger mt-3">{error}</div>}

        <h5 className="mt-4">Activity</h5>
        <div className="border rounded p-2" style={{ maxHeight: 460, overflowY: 'auto' }}>
          {activity.length === 0 && <span className="text-muted">No activity yet.</span>}
          {activity.map((item, i) => (
            <div className="small mb-1" key={i}>
              <span>{item.icon}</span> <strong>{item.title}</strong>
              {item.detail && <div className="text-muted ms-3">{item.detail}</div>}
            </div>
          ))}
        </div>
      </div>

      {/* Results */}
      <div className="col-lg-6">
        <h5>Results</h5>
        {(running || result) && (
          <div className="card mb-3">
            <div className="card-body py-2">
              <div className="d-flex flex-wrap gap-3 align-items-center small">
                <span className="badge bg-secondary fs-6">Total: ${totalCost.toFixed(4)}</span>
                <span>🧩 Matching: <strong>${costs.match.toFixed(4)}</strong></span>
                <span>🔎 Search: <strong>${costs.search.toFixed(4)}</strong></span>
                {costs.other > 0 && <span>📊 Research: <strong>${costs.other.toFixed(4)}</strong></span>}
                <span>🌐 Tavily: <strong>{costs.tavily}</strong> <span className="text-muted">(🔎 {costs.searchTavily} · 🏢 {costs.researchTavily})</span></span>
              </div>
            </div>
          </div>
        )}

        {!result ? (
          <p className="text-muted">Ranked job dossiers will appear here when the run finishes.</p>
        ) : (
          <>
            <div className="alert alert-info">{result.summary}</div>
            <p className="text-muted small">{dossiers.length} match{dossiers.length === 1 ? '' : 'es'} scoring ≥ {minMatchScore}/100.</p>
            {dossiers.slice(0, visibleCount).map((d) => {
              const key = dossierKey(d);
              return (
                <DossierCard key={key} dossier={d}
                  onResearchCompany={() => researchPiece(d, 'company')}
                  onResearchSalary={() => researchPiece(d, 'salary')}
                  onResearchInterview={() => researchPiece(d, 'interview')}
                  researchingCompany={researching.company.has(key)}
                  researchingSalary={researching.salary.has(key)}
                  researchingInterview={researching.interview.has(key)}
                  enableFeedback onSubmitScore={(s) => saveFeedback(d, s)} />
              );
            })}
            {dossiers.length > visibleCount && (
              <button className="btn btn-outline-primary w-100" onClick={() => setVisibleCount((v) => v + PAGE_SIZE)}>
                Load more ({dossiers.length - visibleCount} remaining)
              </button>
            )}
          </>
        )}
      </div>
    </div>
  );
}
