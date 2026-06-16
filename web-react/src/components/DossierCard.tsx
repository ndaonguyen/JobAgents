import { useState } from 'react';
import type { JobDossier, SalaryEstimate } from '../types';
import { isListing, sourceHost } from '../sourceHost';

export interface ScoreSubmission {
  score: number;
  note: string | null;
}

interface Props {
  dossier: JobDossier;
  onResearchCompany?: () => void;
  onResearchSalary?: () => void;
  onResearchInterview?: () => void;
  researchingCompany?: boolean;
  researchingSalary?: boolean;
  researchingInterview?: boolean;
  enableFeedback?: boolean;
  onSubmitScore?: (s: ScoreSubmission) => Promise<void> | void;
}

function formatSalary(s: SalaryEstimate): string {
  if (s.low == null && s.median == null && s.high == null) return 'No reliable estimate.';
  const parts: string[] = [];
  if (s.low != null) parts.push(s.low.toLocaleString());
  if (s.median != null) parts.push(`~${s.median.toLocaleString()}`);
  if (s.high != null) parts.push(s.high.toLocaleString());
  return `${parts.join(' – ')} ${s.currency}`;
}

export default function DossierCard({
  dossier, onResearchCompany, onResearchSalary, onResearchInterview,
  researchingCompany, researchingSalary, researchingInterview, enableFeedback, onSubmitScore,
}: Props) {
  const { match } = dossier;
  const { posting } = match;
  const [humanScore, setHumanScore] = useState<string>('');
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  const summary = posting.summary;
  const description = posting.description;
  const hasFullDescription = !!description && description.trim() !== (summary ?? '').trim();
  const listing = isListing(posting.url);

  const submit = async () => {
    const n = Number(humanScore);
    if (!onSubmitScore || Number.isNaN(n) || n < 0 || n > 100) return;
    setSaving(true);
    try {
      await onSubmitScore({ score: n, note: note.trim() || null });
      setSaved(true);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="card mb-3">
      <div className="card-header d-flex justify-content-between align-items-center">
        <div>
          <strong>{posting.title}</strong>{' '}
          <span className="text-muted">{posting.company} · {posting.location}</span>
          {posting.url && <span className="badge bg-light text-dark border ms-1">{sourceHost(posting.url)}</span>}
          {posting.postedDate
            ? <span className="badge bg-light text-dark border ms-1">🗓 {posting.postedDate}</span>
            : <span className="badge bg-light text-muted border ms-1">🗓 date n/a</span>}
        </div>
        <span className="badge bg-primary">Fit {match.score}/100</span>
      </div>
      <div className="card-body">
        <p className="card-text">{match.rationale}</p>

        {match.matchedSkills.length > 0 && (
          <div className="mb-2">
            {match.matchedSkills.map((s) => <span key={s} className="badge bg-success me-1">{s}</span>)}
          </div>
        )}
        {match.gaps.length > 0 && (
          <div className="mb-2">
            {match.gaps.map((g) => <span key={g} className="badge bg-warning text-dark me-1">gap: {g}</span>)}
          </div>
        )}

        {(summary || hasFullDescription) && (
          <div className="mb-2">
            <h6>📋 Role &amp; requirements</h6>
            {summary && <p className="small mb-1">{summary}</p>}
            {hasFullDescription && (
              <details>
                <summary className="small text-primary" style={{ cursor: 'pointer' }}>Show full posting</summary>
                <div className="small text-muted mt-1" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{description}</div>
              </details>
            )}
          </div>
        )}

        <div className="row mt-3">
          <div className="col-md-6 mb-2">
            <h6>🏢 Company</h6>
            {dossier.company ? (
              <>
                <p className="small mb-1">{dossier.company.summary}</p>
                <ul className="small mb-0">{dossier.company.highlights.map((h, i) => <li key={i}>{h}</li>)}</ul>
              </>
            ) : onResearchCompany && (
              researchingCompany
                ? <span className="text-muted small"><span className="spinner-border spinner-border-sm" /> Researching company…</span>
                : <button className="btn btn-sm btn-outline-success" onClick={onResearchCompany}>🏢 Research company</button>
            )}
          </div>

          <div className="col-md-6 mb-2">
            <h6>💰 Salary</h6>
            {dossier.salary ? (
              <>
                <p className="small mb-1">{formatSalary(dossier.salary)}</p>
                <p className="small text-muted mb-0">{dossier.salary.basis}</p>
              </>
            ) : onResearchSalary && (
              researchingSalary
                ? <span className="text-muted small"><span className="spinner-border spinner-border-sm" /> Estimating salary…</span>
                : <button className="btn btn-sm btn-outline-success" onClick={onResearchSalary}>💰 Research salary</button>
            )}
          </div>
        </div>

        <div className="mt-2">
          <h6>🎤 Interview prep</h6>
          {dossier.interview && dossier.interview.likelyQuestions.length > 0 ? (
            <>
              <ul className="small mb-1">{dossier.interview.likelyQuestions.map((q, i) => <li key={i}>{q}</li>)}</ul>
              {dossier.interview.prepNotes.length > 0 && (
                <p className="small text-muted mb-0">{dossier.interview.prepNotes.join(' · ')}</p>
              )}
            </>
          ) : onResearchInterview && (
            researchingInterview
              ? <span className="text-muted small"><span className="spinner-border spinner-border-sm" /> Preparing interview guidance…</span>
              : <button className="btn btn-sm btn-outline-success" onClick={onResearchInterview}>🎤 Research interview prep</button>
          )}
        </div>

        {posting.url && (
          <div className="mt-2">
            <a className={`btn btn-sm ${listing ? 'btn-outline-secondary' : 'btn-outline-primary'}`}
               href={posting.url} target="_blank" rel="noreferrer">
              {listing ? 'Search results ↗' : 'View posting'}
            </a>
          </div>
        )}

        {enableFeedback && (
          <div className="border-top mt-3 pt-2">
            <label className="form-label small mb-1">
              Your score for this match <span className="text-muted">(feeds the eval calibration set)</span>
            </label>
            <div className="input-group input-group-sm" style={{ maxWidth: 460 }}>
              <input type="number" className="form-control" min={0} max={100} style={{ maxWidth: 90 }}
                     placeholder="0-100" value={humanScore} onChange={(e) => setHumanScore(e.target.value)} />
              <input type="text" className="form-control" placeholder="why? (optional)"
                     value={note} onChange={(e) => setNote(e.target.value)} />
              <button className="btn btn-outline-primary" type="button" onClick={submit}
                      disabled={saving || humanScore === '' || Number(humanScore) < 0 || Number(humanScore) > 100}>
                Save
              </button>
            </div>
            {saved && <span className="small text-success">Saved ✓ (agent said {match.score}/100)</span>}
          </div>
        )}
      </div>
    </div>
  );
}
