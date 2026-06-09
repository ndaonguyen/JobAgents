# CI/CD

Two GitHub Actions workflows back this repo.

## CI — `.github/workflows/ci.yml`

Runs on every push to `main` and every PR into `main`:

1. Checkout + install the SDK pinned by `global.json`.
2. Cache `~/.nuget/packages` (keyed on `Directory.Packages.props`).
3. `dotnet restore` → `build` (Release) → `test`.

`dotnet test` runs only the three `IsTestProject` test projects; the `JobAgents.Evals` console app is skipped (it needs live API keys and is not a test project).

## CD — `.github/workflows/cd.yml`

Runs on push to `main` (and manual dispatch):

1. **`image`** — builds the `Dockerfile` and pushes to **GHCR** at
   `ghcr.io/<owner>/jobagents` (tags: `latest` + commit SHA). Free, no extra account; uses the
   built-in `GITHUB_TOKEN`. Layer cache via GitHub Actions cache.
2. **`deploy-fly`** — deploys to Fly.io **only if** a `FLY_API_TOKEN` secret exists. Without it,
   the job logs a skip notice and the run stays green.

### Image visibility

First GHCR push creates a **private** package. To pull without auth, set the package to public:
GitHub → your profile → Packages → `jobagents` → Package settings → Change visibility → Public.

## Deploying to Fly.io (free tier)

One-time, from your machine (needs the [flyctl CLI](https://fly.io/docs/flyctl/install/)):

```bash
flyctl auth signup            # or: flyctl auth login
flyctl launch --no-deploy     # pick a unique app name; updates fly.toml
```

Then wire the pipeline + runtime secrets:

```bash
# CI/CD: let GitHub Actions deploy. Create a deploy token and add it as a repo secret.
flyctl tokens create deploy -x 999999h
#   GitHub repo → Settings → Secrets and variables → Actions → New secret
#   Name: FLY_API_TOKEN   Value: <token above>

# Runtime: the app reads its keys from configuration (section "JobAgents").
# In a container these come from env vars, with ":" written as "__".
flyctl secrets set \
  JobAgents__OpenAi__ApiKey=sk-... \
  JobAgents__Anthropic__ApiKey=sk-ant-... \
  JobAgents__Tavily__ApiKey=tvly-...
```

Next push to `main` builds the image and runs `flyctl deploy --remote-only` against `fly.toml`.

> The app writes run history to a local `results/` directory. Fly machines have ephemeral
> storage that resets on redeploy — fine for a demo. For durable history, attach a Fly Volume
> and point the app's results path at it.

## Run the container locally

```bash
docker build -t jobagents .
docker run --rm -p 8080:8080 \
  -e JobAgents__OpenAi__ApiKey=sk-... \
  -e JobAgents__Anthropic__ApiKey=sk-ant-... \
  -e JobAgents__Tavily__ApiKey=tvly-... \
  jobagents
# → http://localhost:8080
```
