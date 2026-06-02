using JobAgents.Domain.JobHunt;

namespace JobAgents.Evals;

/// <summary>
/// One labelled match scenario: a resume + posting whose fit we already know, so we can assert the
/// agent's score lands in the expected band and surfaces the expected skills.
/// </summary>
public sealed record MatchCase(
    string Name,
    string Resume,
    JobPosting Posting,
    SearchCriteria Criteria,
    int MinScore,
    int MaxScore,
    string[] ExpectedMatchedSkills,
    int TargetScore);

/// <summary>The fixed, hand-labelled evaluation set. Small on purpose — calibration, not coverage.</summary>
public static class GoldenCases
{
    // A synthetic senior .NET backend engineer. Deliberately NOT the real CV, so the eval is shareable.
    public const string DotNetBackendResume =
        """
        Senior Software Engineer — 10 years building cloud backend systems.
        Deep expertise in C# / .NET and ASP.NET Core. Microservices, Domain-Driven Design,
        Clean Architecture, CQRS, event sourcing. Event-driven systems with Apache Kafka and Avro
        (Schema Registry). Data stores: MongoDB, PostgreSQL, OpenSearch / Elasticsearch.
        Cloud & DevOps: AWS, Terraform, Docker, GitHub Actions CI/CD. Earlier career: Python, PHP,
        and some Objective-C (an iOS driver app, 2016-2017). Led multi-repository features, mentored
        engineers, shipped continuously to production. Worked remote and hybrid across Singapore,
        the UK and Australia/New Zealand.
        """;

    public static IReadOnlyList<MatchCase> Matches { get; } =
    [
        new MatchCase(
            Name: "strong-fit-dotnet-backend",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Senior Backend Engineer (.NET)",
                Company: "Acme Cloud",
                Location: "Remote",
                Url: "https://example.com/jobs/senior-dotnet",
                Summary: "Senior backend role building .NET microservices on AWS.",
                Description:
                    "We need a senior backend engineer with strong C# / .NET and ASP.NET Core. " +
                    "You will design event-driven microservices using Apache Kafka, deploy on AWS with " +
                    "Terraform, and apply DDD / Clean Architecture. MongoDB experience a plus. 7+ years."),
            Criteria: new SearchCriteria(
                Roles: ["Senior Backend Engineer"],
                Locations: ["Remote"],
                Seniority: "Senior",
                MustHaveSkills: ["C#", ".NET", "Microservices", "Kafka", "AWS"],
                NiceToHaveSkills: ["MongoDB", "Terraform"],
                WorkStyles: ["Remote", "Hybrid"],
                SalaryExpectation: null),
            MinScore: 72,
            MaxScore: 100,
            ExpectedMatchedSkills: ["C#", ".NET", "Kafka", "AWS"],
            TargetScore: 90),

        new MatchCase(
            Name: "weak-fit-ios-swift",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Senior iOS Engineer (Swift)",
                Company: "Mobile First",
                Location: "Hybrid - London",
                Url: "https://example.com/jobs/senior-ios",
                Summary: "Senior native iOS engineer building consumer apps in Swift.",
                Description:
                    "Senior iOS engineer with 6+ years of Swift and SwiftUI. Deep knowledge of UIKit, " +
                    "Core Data, the iOS SDK, App Store release processes and mobile performance tuning. " +
                    "This is a hands-on native mobile role; no backend work."),
            Criteria: new SearchCriteria(
                Roles: ["Senior iOS Engineer"],
                Locations: ["London"],
                Seniority: "Senior",
                MustHaveSkills: ["Swift", "SwiftUI", "iOS", "UIKit"],
                NiceToHaveSkills: ["Core Data"],
                WorkStyles: ["Hybrid"],
                SalaryExpectation: null),
            MinScore: 0,
            MaxScore: 50,
            ExpectedMatchedSkills: [],
            TargetScore: 20),

        new MatchCase(
            Name: "mid-fit-fullstack-react",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Senior Full-Stack Engineer",
                Company: "Webly",
                Location: "Remote",
                Url: "https://example.com/jobs/senior-fullstack",
                Summary: "Full-stack role: React front end with a .NET back end.",
                Description:
                    "Senior full-stack engineer. Front end in React + TypeScript (primary, ~60% of the " +
                    "role); back end in C# / .NET. Node.js tooling. You should be comfortable owning UI " +
                    "components and REST APIs end to end."),
            Criteria: new SearchCriteria(
                Roles: ["Senior Full-Stack Engineer"],
                Locations: ["Remote"],
                Seniority: "Senior",
                MustHaveSkills: ["React", "TypeScript", "C#", ".NET"],
                NiceToHaveSkills: ["Node.js"],
                WorkStyles: ["Remote"],
                SalaryExpectation: null),
            MinScore: 40,
            MaxScore: 78,
            ExpectedMatchedSkills: ["C#", ".NET"],
            TargetScore: 60),
    ];
}
