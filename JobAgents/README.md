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

Requires the .NET 9 SDK, an **OpenAI** API key, and a **Tavily** API key (for web search).

```bash
cd src/JobAgents.Web
dotnet user-secrets set "JobAgents:OpenAi:ApiKey" "sk-..."
dotnet user-secrets set "JobAgents:Tavily:ApiKey" "tvly-..."
dotnet run
```

Open the printed URL, paste a resume + preferences, and hit **Find jobs**. Watch the activity log
stream Coordinator → Search → Resume Matching → Company / Salary / Interview, then the ranked dossiers
appear on the right. Finished runs are saved to `src/JobAgents.Web/results/ui-*.jsonl` and listed on
**/past-runs**.

The default model is `gpt-4o-mini` (configurable under `JobAgents:OpenAi:Model`).

## Tests

```bash
dotnet test
```

See [docs/architecture.md](docs/architecture.md) for the full event flow and design notes.
