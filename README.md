# JobAgents

A transparent, multi-agent **end-to-end job hunt** assistant built on Semantic Kernel + Blazor Server,
structured as a Clean Architecture .NET solution. It mirrors the engineering pattern of its sibling
project [ResearchAgents/AgentScope](../../ResearchAgents): the agent graph executes live in the UI —
every tool call, token, and decision is visible in real time.

You paste your **resume** and **preferences**; a **Coordinator** agent fans out to specialists that
source live jobs, match them to you, and then research the company, salary, and interview prep for
your best matches.

## Agent topology

```
Coordinator (IOrchestrator)
  ├── Search Agent              → Tavily web search → job postings
  ├── Resume Matching Agent     → fan-out per posting → fit score + gaps
  ├── Company Research Agent    → fan-out per top match → company insight
  ├── Salary Analysis Agent     → fan-out per top match → salary estimate
  └── Interview Preparation Agent → fan-out per top match → likely Qs + prep
```

The Coordinator is the `IOrchestrator` implementation: it parses the resume + preferences into
structured `SearchCriteria`, runs the **Search** agent, fans out **Resume Matching** over every
posting (concurrency-gated), ranks and selects the top *N*, then fans out **Company / Salary /
Interview** for each of those, and finally synthesises a short summary. It owns the terminal
System-level event carrying the aggregated `JobHuntResult` and token cost.

## Architecture

Clean Architecture with dependencies pointing inward; vertical slices organised by feature:

```
src/
  JobAgents.Domain          # value objects, JobHunt records, AgentEvent hierarchy (zero deps)
  JobAgents.Application      # ports (IOrchestrator, IAgentEventBus, …) + StartJobHuntUseCase
  JobAgents.Infrastructure   # Semantic Kernel agents, Coordinator, Tavily plugin, event bus, pricing
  JobAgents.Web             # Blazor Server UI (live activity log + ranked dossiers) + run persistence
tests/
  JobAgents.Domain.Tests
  JobAgents.Application.Tests
  JobAgents.Infrastructure.Tests
```

Key patterns (ported from ResearchAgents):

- **Per-run event channels** (`ChannelAgentEventBus`) — concurrent runs are fully isolated.
- **`AsyncLocal` run context** — fanned-out agents attribute their tool events to the right run/agent.
- **`IFunctionInvocationFilter`** captures every tool call onto the event bus with no per-tool wiring.
- **Application never references Semantic Kernel** — SK lives only in Infrastructure, behind ports.
- **Live token streaming** straight over the Blazor Server circuit (no separate SignalR hub needed).
- **Truthful cost** — `ModelPricingCalculator` returns `null` for unknown models so the UI shows "—".

## Running it

Requires the .NET 9 SDK, an **OpenAI** API key, an **Anthropic** API key (the resume matcher runs on
Claude), and a **Tavily** API key (for web search).

```bash
cd src/JobAgents.Web
dotnet user-secrets set "JobAgents:OpenAi:ApiKey" "sk-..."
dotnet user-secrets set "JobAgents:Anthropic:ApiKey" "sk-ant-..."
dotnet user-secrets set "JobAgents:Tavily:ApiKey" "tvly-..."
dotnet run
```

You can target specific job sites via the **Sources** selector (ITviec, VietnamWorks, LinkedIn,
TopCV, or the whole web) — these map to Tavily's `include_domains`, so no separate job-board API or
extra keys are needed. Leaving it empty (or choosing "Anywhere") searches the whole web.

Open the printed URL, paste a resume + preferences, and hit **Find jobs**. Watch the activity log
stream Coordinator → Search → Resume Matching → Company / Salary / Interview, then the ranked dossiers
appear on the right. Finished runs are saved to `src/JobAgents.Web/results/ui-*.jsonl` and listed on
**/past-runs**.

The default model is `gpt-4o-mini` (configurable under `JobAgents:OpenAi:Model`). The **resume
matcher** runs on Claude `claude-haiku-4-5` by default (configurable under `JobAgents:Anthropic:Model`);
any agent whose model id starts with `claude` is routed to Anthropic's OpenAI-compatible endpoint,
everything else stays on OpenAI.

## Tests

```bash
dotnet test
```

Deterministic agent behaviour is covered here too — e.g. posting **dedupe**, and the Coordinator's
**company / salary research dedup** (two top matches at the same employer, or the same
role+location+seniority, trigger one research call, not two).

**End-to-end** (`tests/JobAgents.E2E`, Playwright) drives the real Blazor UI — form state, settings
persistence, CV save/forget. It needs a running app, so it's excluded from `dotnet test` by default:

```bash
dotnet run --project src/JobAgents.Web   # terminal 1 (http://localhost:5221)
dotnet test tests/JobAgents.E2E          # terminal 2
```

## CI / CD

- **CI** (`.github/workflows/ci.yml`) — build, unit/integration tests, and a dedicated **E2E** job that
  boots the app and runs Playwright. A **SonarCloud** scan publishes coverage and gates on
  **≥ 80 % coverage of new code** ("clean as you code") via the *SonarCloud Code Analysis* check,
  required on `main` through branch protection.
- **CD** (`.github/workflows/cd.yml`) — on push to `main`, runs the test gate, builds a container image,
  pushes it to GHCR, and (if `FLY_API_TOKEN` is set) deploys to Fly.io by SHA, so *tested == deployed*.

## Evaluations

LLM behaviour can't be pinned by ordinary unit tests, so the `eval/JobAgents.Evals` console project
scores the agents against labelled cases and invariants. It reuses the Web app's user-secrets (same
`UserSecretsId`), so the OpenAI / Anthropic / Tavily keys you set above just work — no extra setup.
Each command exits `0` when everything passes and `1` otherwise, so they double as CI gates.

```bash
cd eval/JobAgents.Evals

dotnet run                 # resume-matcher golden cases (default)   — needs OpenAI + Anthropic
dotnet run -- interview    # interview-prep eval                     — needs OpenAI
dotnet run -- search-eval  # search-agent property eval (live)       — needs OpenAI + Tavily
dotnet run -- search-ab    # search URL-wording A/B (live, one-off)   — needs OpenAI + Tavily
```

- **`dotnet run`** (matcher) — runs 10 hand-labelled resume↔posting cases, 3 trials each, and checks
  three things per case: the fit **score** lands in the expected band, the expected **skills** are
  surfaced, and an **LLM judge** confirms no skill was hallucinated. Prints a scorecard with score MAE,
  tokens and estimated cost.
- **`interview`** — for labelled (posting + known-gaps) cases, checks the prep is **structurally sound**
  (sane question/note counts, no blanks or duplicates) and uses an **LLM judge** to confirm the
  questions are role-relevant and the notes address the candidate's gaps.
- **`search-eval`** — search has no fixed ground truth, so it asserts **invariants** (well-formed
  postings, dedup by company+title, search-budget adherence) and reports **URL-veracity metrics**
  (reachability + whether the page mentions the company) to catch invented or stale postings. Hits live
  web search and fetches URLs, so it's opt-in and indicative.
- **`search-ab`** — one-off A/B of two URL-quality prompt wordings on the Vietnamese job boards;
  reports how many distinct postings each returns and the detail-vs-listing URL split.

The `interview` and `search` evals don't need the Anthropic key; only the default matcher eval does.

See [docs/architecture.md](docs/architecture.md) for the full event flow and design notes.
