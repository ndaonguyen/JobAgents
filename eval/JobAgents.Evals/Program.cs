using JobAgents.Evals;
using JobAgents.Infrastructure.Configuration;
using JobAgents.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ── Config + DI ────────────────────────────────────────────────────────────────────────────────
// Reuses the Web app's user-secrets (same UserSecretsId) so the OpenAI / Tavily keys just work.
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddInfrastructure(config);
using var provider = services.BuildServiceProvider();

// Optional sub-command: `dotnet run -- search-ab` runs the Search agent URL-wording A/B instead of
// the matcher eval. It drives live web search, so it's opt-in rather than part of the default run.
if (args.Length > 0 && string.Equals(args[0], "search-ab", StringComparison.OrdinalIgnoreCase))
    return await SearchAb.RunAsync(provider);

var options = provider.GetRequiredService<IOptions<JobAgentsOptions>>().Value;
if (string.IsNullOrWhiteSpace(options.OpenAi.ApiKey))
{
    Console.Error.WriteLine(
        "No OpenAI API key found. Set it via user-secrets (JobAgents:OpenAi:ApiKey) on the " +
        "jobagents-web-dev secret store, or the JobAgents__OpenAi__ApiKey environment variable.");
    return 2;
}

// The matcher now runs on Claude by default (the judge still uses the OpenAI model above).
if (string.IsNullOrWhiteSpace(options.Anthropic.ApiKey))
{
    Console.Error.WriteLine(
        "No Anthropic API key found (the resume matcher runs on Claude). Set it via user-secrets " +
        "(JobAgents:Anthropic:ApiKey) or the JobAgents__Anthropic__ApiKey environment variable.");
    return 2;
}

// Sub-command: `dotnet run -- feedback-cases [dir]` calibrates against human-scored real matches
// captured in the web app, instead of the synthetic golden set. Optional [dir] overrides where the
// feedback-*.jsonl files live (defaults to the web app's results directory).
if (args.Length > 0 && string.Equals(args[0], "feedback-cases", StringComparison.OrdinalIgnoreCase))
{
    var feedbackDir = args.Length > 1 ? args[1] : DefaultFeedbackDir();
    Console.WriteLine($"Loading feedback from: {feedbackDir}\n");

    var cases = await FeedbackCases.LoadAsync(feedbackDir);
    if (cases.Count == 0)
    {
        Console.Error.WriteLine(
            "No feedback found. Score some matches in the web app first (each result card has a " +
            "\"Your score for this match\" control), then re-run this command.");
        return 2;
    }

    return await EvalRunner.RunAsync(provider, cases, "feedback");
}

return await EvalRunner.RunAsync(provider, GoldenCases.Matches, "match eval");

// Walks up from the eval's working directory to the repo root (the folder holding JobAgents.sln) and
// returns the web app's results directory, where feedback-*.jsonl is written.
static string DefaultFeedbackDir()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobAgents.sln")))
        dir = dir.Parent;

    var root = dir?.FullName ?? Directory.GetCurrentDirectory();
    return Path.Combine(root, "src", "JobAgents.Web", "results");
}
