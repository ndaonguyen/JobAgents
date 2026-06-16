import { NavLink, Route, Routes } from 'react-router-dom';
import Home from './pages/Home';
import JdAnalyzer from './pages/JdAnalyzer';
import PastRuns from './pages/PastRuns';
import Roadmap from './pages/Roadmap';
import Settings from './pages/Settings';

const links = [
  { to: '/', label: 'Job Hunt', end: true },
  { to: '/jd-analyzer', label: 'JD Analyzer' },
  { to: '/past-runs', label: 'Past Runs' },
  { to: '/roadmap', label: 'Roadmap' },
  { to: '/settings', label: 'Settings' },
];

export default function App() {
  return (
    <div className="d-flex flex-column min-vh-100">
      <nav className="navbar navbar-expand navbar-dark bg-dark px-3">
        <span className="navbar-brand">JobAgents <span className="badge bg-info text-dark">React</span></span>
        <ul className="navbar-nav flex-row gap-3">
          {links.map((l) => (
            <li className="nav-item" key={l.to}>
              <NavLink
                to={l.to}
                end={l.end}
                className={({ isActive }) => `nav-link ${isActive ? 'active fw-bold' : ''}`}
              >
                {l.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      <main className="container-fluid py-4 px-4 flex-grow-1">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/jd-analyzer" element={<JdAnalyzer />} />
          <Route path="/past-runs" element={<PastRuns />} />
          <Route path="/roadmap" element={<Roadmap />} />
          <Route path="/settings" element={<Settings />} />
        </Routes>
      </main>
    </div>
  );
}
