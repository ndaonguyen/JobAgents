import { useEffect, useState } from 'react';
import { api } from '../api';
import type { ImprovementIdea } from '../types';

function statusClass(status: string): string {
  switch (status) {
    case 'Done': return 'bg-success';
    case 'In Progress': return 'bg-primary';
    case 'Planned': return 'bg-info text-dark';
    default: return 'bg-secondary';
  }
}

export default function Roadmap() {
  const [ideas, setIdeas] = useState<ImprovementIdea[]>([]);
  const [statuses, setStatuses] = useState<string[]>([]);
  const [adding, setAdding] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editDescription, setEditDescription] = useState('');

  const reload = () => api.ideas().then(setIdeas);
  useEffect(() => { reload(); api.ideaStatuses().then(setStatuses); }, []);

  const add = async () => {
    if (!newTitle.trim()) return;
    await api.addIdea(newTitle, newDescription);
    setNewTitle(''); setNewDescription(''); setAdding(false);
    reload();
  };

  const saveEdit = async () => {
    if (!editingId || !editTitle.trim()) return;
    await api.updateIdea(editingId, editTitle, editDescription);
    setEditingId(null);
    reload();
  };

  return (
    <>
      <div className="d-flex align-items-start justify-content-between flex-wrap gap-2">
        <div>
          <h1 className="mb-0">Roadmap</h1>
          <p className="text-muted mb-0">Future improvements for engineers to pick up.</p>
        </div>
        <button className="btn btn-primary" onClick={() => { setAdding(!adding); setNewTitle(''); setNewDescription(''); }}>
          <span className="me-1">{adding ? '✕' : '➕'}</span> {adding ? 'Close' : 'New idea'}
        </button>
      </div>

      {adding && (
        <div className="card border-primary my-3">
          <div className="card-body">
            <div className="mb-2">
              <label className="form-label fw-semibold">Title</label>
              <input className="form-control" value={newTitle} onChange={(e) => setNewTitle(e.target.value)}
                     placeholder="Short summary of the improvement" />
            </div>
            <div className="mb-2">
              <label className="form-label fw-semibold">Description</label>
              <textarea className="form-control" rows={5} value={newDescription}
                        onChange={(e) => setNewDescription(e.target.value)}
                        placeholder="What should be built and why…" />
            </div>
            <button className="btn btn-primary" onClick={add} disabled={!newTitle.trim()}>Save idea</button>
            <button className="btn btn-link" onClick={() => setAdding(false)}>Cancel</button>
          </div>
        </div>
      )}

      {ideas.length === 0 ? (
        <div className="alert alert-light border mt-3">No ideas yet. Click <strong>New idea</strong> to add the first one.</div>
      ) : (
        <div className="mt-3">
          {ideas.map((idea) => (
            <div className="card mb-2" key={idea.id}>
              <div className="card-body">
                {editingId === idea.id ? (
                  <>
                    <div className="mb-2">
                      <label className="form-label fw-semibold">Title</label>
                      <input className="form-control" value={editTitle} onChange={(e) => setEditTitle(e.target.value)} />
                    </div>
                    <div className="mb-2">
                      <label className="form-label fw-semibold">Description</label>
                      <textarea className="form-control" rows={5} value={editDescription}
                                onChange={(e) => setEditDescription(e.target.value)} />
                    </div>
                    <button className="btn btn-primary btn-sm" onClick={saveEdit} disabled={!editTitle.trim()}>Save</button>
                    <button className="btn btn-link btn-sm" onClick={() => setEditingId(null)}>Cancel</button>
                  </>
                ) : (
                  <>
                    <div className="d-flex align-items-start justify-content-between gap-2">
                      <h5 className="mb-1">{idea.title}</h5>
                      <div className="d-flex align-items-center gap-2 flex-shrink-0">
                        <span className={`badge ${statusClass(idea.status)}`}>{idea.status}</span>
                        <select className="form-select form-select-sm" style={{ width: 'auto' }}
                                value={idea.status}
                                onChange={async (e) => { await api.setIdeaStatus(idea.id, e.target.value); reload(); }}>
                          {statuses.map((s) => <option key={s} value={s}>{s}</option>)}
                        </select>
                        <button className="btn btn-sm btn-outline-secondary" title="Edit"
                                onClick={() => { setEditingId(idea.id); setEditTitle(idea.title); setEditDescription(idea.description); }}>✏️</button>
                        <button className="btn btn-sm btn-outline-danger" title="Delete"
                                onClick={async () => { await api.deleteIdea(idea.id); reload(); }}>🗑</button>
                      </div>
                    </div>
                    {idea.description && <p className="mb-1" style={{ whiteSpace: 'pre-wrap' }}>{idea.description}</p>}
                    <div className="small text-muted">Added {new Date(idea.createdAtUtc).toLocaleString()} </div>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  );
}
