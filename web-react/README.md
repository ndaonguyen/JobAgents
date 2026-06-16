# JobAgents — React front end

A React (Vite + TypeScript) front end for JobAgents, running **alongside** the existing Blazor
Server app. Both talk to the same domain (Application + Infrastructure) and read/write the **same**
on-disk results store, so runs, saved CVs, settings and ideas are shared between the two UIs.

## Architecture

```
              ┌─────────────────────────────┐
              │  JobAgents.Application /     │
              │  JobAgents.Infrastructure    │  ← Coordinator, agents, event bus, Tavily, pricing
              └──────────────┬──────────────┘
                             │  (+ JobAgents.Shared: stores, resume extractor)
            ┌────────────────┴────────────────┐
            │                                 │
   ┌────────▼─────────┐             ┌─────────▼──────────┐
   │ JobAgents.Web     │            │ JobAgents.Api       │
   │ Blazor Server     │            │ ASP.NET minimal API │
   │ http://localhost: │            │ http://localhost:   │
   │ 5221              │            │ 5300  (REST + SSE)  │
   └───────────────────┘            └─────────┬──────────┘
                                              │  /api/* and /export/* (proxied by Vite)
                                    ┌─────────▼──────────┐
                                    │ web-react (Vite)    │
                                    │ http://localhost:   │
                                    │ 5173                │
                                    └────────────────────┘
```

- The Blazor app is **unchanged** and calls the domain in-process via DI.
- The React app is a browser SPA; it cannot use DI, so it talks to `JobAgents.Api` over HTTP.
- The job hunt streams agent events as **Server-Sent Events** (`POST /api/hunt/run`); every other
  call is plain JSON REST.
- The API reuses the Blazor app's **user-secrets** store (`UserSecretsId = jobagents-web-dev`), so the
  OpenAI / Anthropic / Tavily keys you already configured work with no re-entry.

## Run everything in parallel

Three terminals (or use `run-all.ps1` at the repo root):

```bash
# 1) Blazor Server (existing UI)         → http://localhost:5221
dotnet run --project src/JobAgents.Web

# 2) React backend API                   → http://localhost:5300
dotnet run --project src/JobAgents.Api

# 3) React dev server                    → http://localhost:5173
cd web-react && npm install && npm run dev
```

Open the Blazor app at **5221** and the React app at **5173** — both work at the same time against
the same data.

## Pages (full parity with Blazor)

| Route          | Mirrors Blazor page | Notes                                              |
|----------------|---------------------|----------------------------------------------------|
| `/`            | Home / Job Hunt     | Full form, SSE activity log, dossiers, feedback    |
| `/jd-analyzer` | JD Analyzer         | Resume vs one JD, file upload                       |
| `/past-runs`   | Past Runs           | Pin / rename / delete / view / run again / export   |
| `/roadmap`     | Roadmap             | Improvement-idea backlog                            |
| `/settings`    | Settings            | Per-agent models, depth, truncation, parallel       |

## Production build

```bash
cd web-react && npm run build   # → dist/ (static, served by any web server)
```
