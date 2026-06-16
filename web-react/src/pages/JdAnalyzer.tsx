import { useEffect, useState } from 'react';
import { api } from '../api';
import type { JdAnalysis } from '../types';

function scoreClass(score: number): string {
  if (score >= 85) return 'bg-success';
  if (score >= 70) return 'bg-primary';
  if (score >= 60) return 'bg-warning text-dark';
  return 'bg-danger';
}

function severityClass(severity: string): string {
  if (severity === 'critical') return 'bg-danger';
  if (severity === 'minor') return 'bg-secondary';
  return 'bg-warning text-dark';
}

export default function JdAnalyzer() {
  const [resume, setResume] = useState('');
  const [cvFromProfile, setCvFromProfile] = useState(false);
  const [jd, setJd] = useState('');
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<JdAnalysis | null>(null);
  const [cvStatus, setCvStatus] = useState<{ msg: string; failed: boolean } | null>(null);
  const [jdStatus, setJdStatus] = useState<{ msg: string; failed: boolean } | null>(null);

  useEffect(() => {
    api.profile().then((p) => {
      if (p?.resumeText) { setResume(p.resumeText); setCvFromProfile(true); }
    });
  }, []);

  const upload = async (file: File | undefined, isCv: boolean) => {
    if (!file) return;
    const set = isCv ? setCvStatus : setJdStatus;
    try {
      const { text } = await api.extract(file);
      if (isCv) { setResume(text); setCvFromProfile(false); } else setJd(text);
      set({ msg: `Loaded ${file.name} ✓`, failed: false });
    } catch (e) {
      set({ msg: (e as Error).message, failed: true });
    }
  };

  const analyze = async () => {
    if (!resume.trim() || !jd.trim()) return;
    setRunning(true); setError(null); setResult(null);
    try {
      const r = await api.jdAnalyze(resume, jd);
      if (r.overallScore === 0 && r.gaps.length === 0 && r.matchedStrengths.length === 0)
        setError("The analyzer didn't return a usable result. Please try again.");
      else setResult(r);
    } catch (e) {
      setError(`Analysis failed: ${(e as Error).message}`);
    } finally {
      setRunning(false);
    }
  };

  return (
    <>
      <h1>JD Analyzer</h1>
      <p className="text-muted">Paste a job description and analyse it against your CV — match score, strengths, gaps, advice.</p>

      <div className="row g-4">
        <div className="col-lg-6">
          <label className="form-label fw-semibold">Your CV</label>
          <div className="form-text mb-1">{cvFromProfile ? 'Loaded from your saved profile — edit to analyse a different CV.' : 'No saved profile found — paste your CV here.'}</div>
          <textarea className="form-control" rows={12} value={resume} onChange={(e) => setResume(e.target.value)}
                    placeholder="Paste your CV / resume text…" />
          <div className="mt-2">
            <input type="file" className="form-control form-control-sm" accept=".txt,.md,.pdf,.docx"
                   onChange={(e) => upload(e.target.files?.[0], true)} />
            {cvStatus && <div className={`small ${cvStatus.failed ? 'text-danger' : 'text-success'}`}>{cvStatus.msg}</div>}
          </div>
        </div>

        <div className="col-lg-6">
          <label className="form-label fw-semibold">Job description</label>
          <div className="form-text mb-1">Paste the full JD — responsibilities, requirements, must-have skills.</div>
          <textarea className="form-control" rows={12} value={jd} onChange={(e) => setJd(e.target.value)}
                    placeholder="Paste the job description here…" />
          <div className="mt-2">
            <input type="file" className="form-control form-control-sm" accept=".txt,.md,.pdf,.docx"
                   onChange={(e) => upload(e.target.files?.[0], false)} />
            {jdStatus && <div className={`small ${jdStatus.failed ? 'text-danger' : 'text-success'}`}>{jdStatus.msg}</div>}
          </div>
        </div>
      </div>

      <div className="d-flex align-items-center gap-3 mt-3">
        <button className="btn btn-primary" onClick={analyze} disabled={running || !resume.trim() || !jd.trim()}>
          {running ? <><span className="spinner-border spinner-border-sm me-1" /> Analysing…</> : 'Analyse gap'}
        </button>
        {error && <span className="text-danger">{error}</span>}
      </div>

      {result && (
        <>
          <hr className="my-4" />
          <div className="d-flex align-items-center gap-3 mb-3">
            <span className={`badge fs-5 ${scoreClass(result.overallScore)}`}>{result.overallScore}/100</span>
            <h4 className="mb-0">{result.verdict}</h4>
          </div>

          {result.summary && <div className="alert alert-info">{result.summary}</div>}

          <div className="row g-4">
            <div className="col-lg-6">
              <div className="card h-100">
                <div className="card-header fw-semibold">✅ Matched strengths</div>
                <div className="card-body">
                  {result.matchedStrengths.length === 0
                    ? <p className="text-muted mb-0">None identified.</p>
                    : <ul className="mb-0">{result.matchedStrengths.map((s, i) => <li key={i}>{s}</li>)}</ul>}
                </div>
              </div>
            </div>
            <div className="col-lg-6">
              <div className="card h-100">
                <div className="card-header fw-semibold">⚠️ Gaps</div>
                <div className="card-body">
                  {result.gaps.length === 0
                    ? <p className="text-muted mb-0">No significant gaps found.</p>
                    : <ul className="list-unstyled mb-0">
                        {result.gaps.map((g, i) => (
                          <li className="mb-2" key={i}>
                            <span className={`badge me-1 ${severityClass(g.severity)}`}>{g.severity}</span>
                            <strong>{g.requirement}</strong>
                            {g.advice && <div className="small text-muted">{g.advice}</div>}
                          </li>
                        ))}
                      </ul>}
                </div>
              </div>
            </div>
          </div>

          {result.missingKeywords.length > 0 && (
            <div className="mt-4">
              <h6>Missing keywords</h6>
              {result.missingKeywords.map((k, i) => <span key={i} className="badge bg-light text-dark border me-1 mb-1">{k}</span>)}
            </div>
          )}

          <div className="row g-4 mt-1">
            {result.cvSuggestions.length > 0 && (
              <div className="col-lg-6">
                <div className="card h-100">
                  <div className="card-header fw-semibold">✍️ Tailor your CV</div>
                  <div className="card-body"><ul className="mb-0">{result.cvSuggestions.map((s, i) => <li key={i}>{s}</li>)}</ul></div>
                </div>
              </div>
            )}
            {result.interviewTalkingPoints.length > 0 && (
              <div className="col-lg-6">
                <div className="card h-100">
                  <div className="card-header fw-semibold">🎤 Interview talking points</div>
                  <div className="card-body"><ul className="mb-0">{result.interviewTalkingPoints.map((p, i) => <li key={i}>{p}</li>)}</ul></div>
                </div>
              </div>
            )}
          </div>
        </>
      )}
    </>
  );
}
