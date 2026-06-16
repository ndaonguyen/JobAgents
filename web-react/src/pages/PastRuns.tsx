import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import type { PersistedRun } from '../types';
import DossierCard from '../components/DossierCard';

export default function PastRuns() {
  const nav = useNavigate();
  const [runs, setRuns] = useState<PersistedRun[] | null>(null);
  const [selected, setSelected] = useState<PersistedRun | null>(null);
  const [pinnedOnly, setPinnedOnly] = useState(false);
  const [search, setSearch] = useState('');
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [confirmClearAll, setConfirmClearAll] = useState(false);

  const reload = () => api.runs().then(setRuns);
  useEffect(() => { reload(); }, []);

  const filtered = useMemo(() =>
    (runs ?? []).filter((r) =>
      (!pinnedOnly || r.pinned) &&
      (!search.trim() || (r.title ?? '').toLowerCase().includes(search.toLowerCase()))),
    [runs, pinnedOnly, search]);

  const runAgain = (run: PersistedRun) => nav('/', { state: { inputs: run.inputs, autoRun: true } });

  if (runs === null) return <p className="text-muted">Loading…</p>;

  return (
    <>
      <h1>Past Runs</h1>
      <p className="text-muted">Pin the searches you reuse, rename them, or remove ones you no longer need.</p>

      {runs.length === 0 ? (
        <p className="text-muted">No runs saved yet. Start a job hunt on the home page.</p>
      ) : (
        <>
          <div className="d-flex gap-3 align-items-center mb-2 flex-wrap">
            <div className="form-check">
              <input className="form-check-input" type="checkbox" id="pinnedOnly" checked={pinnedOnly}
                     onChange={(e) => setPinnedOnly(e.target.checked)} />
              <label className="form-check-label" htmlFor="pinnedOnly">Pinned only</label>
            </div>
            <input className="form-control form-control-sm" style={{ maxWidth: 260 }} placeholder="Search title…"
                   value={search} onChange={(e) => setSearch(e.target.value)} />
            <div className="ms-auto d-flex gap-2 align-items-center">
              <a className="btn btn-sm btn-outline-primary" href="/export/runs">Export</a>
              {confirmClearAll ? (
                <>
                  <span className="small text-danger">Delete all {runs.length} runs?</span>
                  <button className="btn btn-sm btn-danger" onClick={async () => { await api.deleteAllRuns(); setConfirmClearAll(false); setSelected(null); reload(); }}>Yes, clear all</button>
                  <button className="btn btn-sm btn-outline-secondary" onClick={() => setConfirmClearAll(false)}>Cancel</button>
                </>
              ) : (
                <button className="btn btn-sm btn-outline-danger" onClick={() => setConfirmClearAll(true)}>Clear all</button>
              )}
            </div>
          </div>

          <table className="table table-sm align-middle">
            <thead>
              <tr>
                <th style={{ width: '1%' }}></th>
                <th>When (UTC)</th><th>Search</th><th>Top matches</th><th>Cost</th><th className="text-end">Actions</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((run) => (
                <tr key={run.runId}>
                  <td>
                    <button className={`btn btn-sm btn-link p-0 ${run.pinned ? 'text-warning' : 'text-muted'}`}
                            title={run.pinned ? 'Unpin' : 'Pin to top'}
                            onClick={async () => { await api.pinRun(run.runId, !run.pinned); reload(); }}>
                      {run.pinned ? '★' : '☆'}
                    </button>
                  </td>
                  <td>{new Date(run.completedAtUtc).toISOString().slice(0, 16).replace('T', ' ')}</td>
                  <td className="small">
                    {renamingId === run.runId ? (
                      <div className="input-group input-group-sm" style={{ maxWidth: 320 }}>
                        <input className="form-control" value={renameValue} onChange={(e) => setRenameValue(e.target.value)} />
                        <button className="btn btn-primary" onClick={async () => { if (renameValue.trim()) await api.renameRun(run.runId, renameValue.trim()); setRenamingId(null); reload(); }}>Save</button>
                        <button className="btn btn-outline-secondary" onClick={() => setRenamingId(null)}>Cancel</button>
                      </div>
                    ) : <strong>{run.title || 'Job search'}</strong>}
                  </td>
                  <td>{run.result.dossiers.length}</td>
                  <td>{run.estimatedCostUsd != null ? `$${run.estimatedCostUsd.toFixed(4)}` : '—'}</td>
                  <td className="text-end text-nowrap">
                    {run.inputs && <button className="btn btn-sm btn-success" onClick={() => runAgain(run)}>Run again</button>}{' '}
                    <button className="btn btn-sm btn-outline-secondary"
                            onClick={() => setSelected(selected?.runId === run.runId ? null : run)}>
                      {selected?.runId === run.runId ? 'Hide' : 'View'}
                    </button>{' '}
                    <button className="btn btn-sm btn-outline-secondary"
                            onClick={() => { setRenamingId(run.runId); setRenameValue(run.title); setConfirmDeleteId(null); }}>Rename</button>{' '}
                    {confirmDeleteId === run.runId ? (
                      <>
                        <button className="btn btn-sm btn-danger" onClick={async () => { await api.deleteRun(run.runId); setConfirmDeleteId(null); if (selected?.runId === run.runId) setSelected(null); reload(); }}>Delete?</button>
                        <button className="btn btn-sm btn-outline-secondary" onClick={() => setConfirmDeleteId(null)}>No</button>
                      </>
                    ) : (
                      <button className="btn btn-sm btn-outline-danger" onClick={() => setConfirmDeleteId(run.runId)}>Delete</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {selected && (
            <>
              <div className="alert alert-info">{selected.result.summary}</div>
              {selected.result.dossiers.map((d, i) => <DossierCard key={i} dossier={d} />)}
            </>
          )}
        </>
      )}
    </>
  );
}
