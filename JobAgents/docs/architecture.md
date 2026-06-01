# Architecture

JobAgents is a Clean Architecture .NET 9 solution. Dependencies point inward; Semantic Kernel and
Tavily are confined to the Infrastructure layer behind Application ports.

```
JobAgents.Web ──► JobAgents.Application ──► JobAgents.Domain
       │                    ▲
       └► JobAgents.Infrastructure ─┘  (implements Application ports, depends on Domain)
```

## Layers

### Domain (`src/JobAgents.Domain`)
Zero dependencies. Value objects and events only:
- `Runs/RunId`, `Agents/AgentId` (with `Coordinator`, `Search`, and indexed `ResumeMatch(i)` /
  `CompanyResearch(i)` / `SalaryAnalysis(i)` / `InterviewPrep(i)` factories), `Agents/AgentRunRequest`.
- `JobHunt/JobHuntTypes` — `SearchCriteria`, `JobPosting`, `JobMatch`, `CompanyInsight`,
  `SalaryEstimate`, `InterviewPrep`, `JobDossier`, `JobHuntResult`.
- `Events/AgentEvent` — abstract base with a stable `Kind` discriminator + sealed subclasses
  (`agent.started/token/finished/error`, `tool.called/result`, and the domain events
  `jobs.found`, `job.matched`, `company.researched`, `salary.analyzed`, `interview.prep`).

### Application (`src/JobAgents.Application`)
Ports + the use case. No SK, no Tavily.
- Ports: `IAgentEventBus`, `IOrchestrator`, `IUsageCalculator`, `IWorkingMemory`; config `JobHuntConfig`.
- `JobHunt/StartJobHuntUseCase` — subscribes to the run's stream *before* starting the Coordinator on
  a background task, and converts unhandled failures into a terminal System `AgentErrorEvent`.

### Infrastructure (`src/JobAgents.Infrastructure`)
- `EventBus/ChannelAgentEventBus` — one `Channel<AgentEvent>` per run; the subscriber stream ends on
  the System-level finished/error event, then the channel is removed.
- `Agents/AgentRunContext` — `AsyncLocal` (run, agent) so the function filter can attribute tool calls.
- `Agents/KernelFactory` — builds a fresh `Kernel` per agent/run, wires OpenAI chat completion + the
  Tavily plugin + the event-publishing function filter.
- `Agents/AgentRunner` (`IAgentRunner`) — runs one agent turn: streams the completion (publishing
  started/token/finished), enables auto tool-calling, and extracts real token usage.
- The five specialist agents (`SearchAgent`, `ResumeMatchAgent`, `CompanyResearchAgent`,
  `SalaryAnalysisAgent`, `InterviewPrepAgent`) — each owns a prompt + JSON output parsing only.
- `Agents/Coordinator` (`IOrchestrator`) — the pipeline + ranking + terminal event + usage aggregation.
- `Plugins/JobSearchPlugin` — a kernel function calling the Tavily REST API.
- `Pricing/ModelPricingCalculator`, `Memory/NullWorkingMemory`, `Configuration/JobAgentsOptions`.

### Web (`src/JobAgents.Web`)
Blazor Server (interactive server components). `Components/Pages/Home.razor` injects
`StartJobHuntUseCase`, iterates the event stream, and updates the UI per event over the Blazor
circuit — the live "Activity" log on the left, ranked `DossierCard`s on the right. `RunStore`
appends finished runs to `results/ui-{yyyyMMdd}.jsonl`; `/past-runs` reads them back.

## Event flow for one run

1. `StartJobHuntUseCase.Start` → `RunId`, subscribes to the bus, runs the Coordinator in the background.
2. Coordinator: `agent.started`/tokens (Coordinator) while parsing `SearchCriteria`.
3. Search agent runs (tokens + `tool.called`/`tool.result` for each Tavily call) → `jobs.found`.
4. Resume Matching fans out per posting → one `job.matched` each (concurrency = `MaxFanOutConcurrency`).
5. Coordinator ranks by score, takes `TopMatchesToExpand`.
6. For each top match, Company / Salary / Interview run in parallel → `company.researched`,
   `salary.analyzed`, `interview.prep`.
7. Coordinator synthesises a summary, assembles `JobHuntResult`, and publishes the terminal
   System `agent.finished` (FinalText = the result JSON; carries aggregated tokens + cost). The
   subscriber stream completes.

## Notes

- **Token usage** is best-effort: present only when the OpenAI connector emits a streaming usage
  chunk. When absent, cost surfaces as "—" rather than a misleading $0.00.
- **Resilience**: a failing expansion agent (company/salary/interview) degrades that field to `null`
  rather than aborting the run; a fatal error becomes a terminal System `agent.error`.
- **Out of scope (v1)**: vector working memory (only `NullWorkingMemory`), resume/cover-letter
  tailoring, and a standalone Critic agent (the Coordinator does the ranking/quality gate).
