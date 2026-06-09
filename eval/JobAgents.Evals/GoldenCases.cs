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

        // ── Leadership: tech lead who stays hands-on. The resume's "led multi-repository features,
        //    mentored engineers" plus deep .NET should land this as a strong fit.
        new MatchCase(
            Name: "strong-fit-engineering-team-lead",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Engineering Team Lead (.NET)",
                Company: "Northwind Platforms",
                Location: "Remote",
                Url: "https://example.com/jobs/team-lead-dotnet",
                Summary: "Hands-on team lead guiding a squad of backend engineers on a .NET platform.",
                Description:
                    "Lead a squad of 5-7 backend engineers while staying hands-on in the codebase. " +
                    "Strong C# / .NET and ASP.NET Core required. You will own technical direction for " +
                    "event-driven microservices, mentor engineers, run code reviews, and drive delivery " +
                    "to production. Experience with DDD / Clean Architecture and AWS expected. 8+ years, " +
                    "with prior experience leading or mentoring engineers."),
            Criteria: new SearchCriteria(
                Roles: ["Engineering Team Lead", "Tech Lead"],
                Locations: ["Remote"],
                Seniority: "Lead",
                MustHaveSkills: ["C#", ".NET", "Microservices", "Leadership", "AWS"],
                NiceToHaveSkills: ["Kafka", "Clean Architecture"],
                WorkStyles: ["Remote", "Hybrid"],
                SalaryExpectation: null),
            MinScore: 72,
            MaxScore: 100,
            ExpectedMatchedSkills: ["C#", ".NET", "AWS"],
            TargetScore: 86),

        // ── Leadership trap: a PEOPLE-management role (hiring, performance, budget) where the
        //    candidate's experience is mostly IC + informal mentoring. Should be a partial fit, not strong.
        new MatchCase(
            Name: "mid-fit-engineering-manager-people",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Engineering Manager",
                Company: "Skyline Software",
                Location: "Hybrid - Sydney",
                Url: "https://example.com/jobs/engineering-manager",
                Summary: "People-first engineering manager owning hiring, growth and delivery for two teams.",
                Description:
                    "Manage two engineering teams (12 people total). This is a PEOPLE-MANAGEMENT role: " +
                    "hiring and headcount planning, performance management and career growth, running " +
                    "1:1s, quarterly roadmap and budget ownership, and cross-functional stakeholder " +
                    "management. You will NOT be writing production code day-to-day. 3+ years of formal " +
                    "people-management experience required; a technical background is expected but secondary."),
            Criteria: new SearchCriteria(
                Roles: ["Engineering Manager"],
                Locations: ["Sydney"],
                Seniority: "Manager",
                MustHaveSkills: ["People Management", "Hiring", "Performance Management", "Roadmap Ownership"],
                NiceToHaveSkills: ["C#", ".NET"],
                WorkStyles: ["Hybrid"],
                SalaryExpectation: null),
            MinScore: 35,
            MaxScore: 68,
            ExpectedMatchedSkills: [],
            TargetScore: 40),

        // ── Adjacent infra role: strong on Terraform/AWS/Docker/CI but the central platform skill
        //    (Kubernetes) is absent from the resume — the "missing core requirement caps at 70" rule.
        new MatchCase(
            Name: "mid-fit-platform-devops-k8s",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Platform / DevOps Engineer",
                Company: "Orbit Infra",
                Location: "Remote",
                Url: "https://example.com/jobs/platform-devops",
                Summary: "Platform engineer owning the company's Kubernetes-based delivery platform.",
                Description:
                    "Own and operate our internal platform. Primary requirement: deep Kubernetes " +
                    "experience (cluster operations, Helm, autoscaling) — this is the core of the role. " +
                    "Strong Terraform, AWS, Docker and CI/CD (GitHub Actions) also required. You will " +
                    "build paved-road tooling for product teams. Some software engineering background helps."),
            Criteria: new SearchCriteria(
                Roles: ["Platform Engineer", "DevOps Engineer"],
                Locations: ["Remote"],
                Seniority: "Senior",
                MustHaveSkills: ["Kubernetes", "Terraform", "AWS", "Docker"],
                NiceToHaveSkills: ["GitHub Actions"],
                WorkStyles: ["Remote"],
                SalaryExpectation: null),
            MinScore: 50,
            MaxScore: 78,
            ExpectedMatchedSkills: ["Terraform", "AWS", "Docker"],
            TargetScore: 60),

        // ── Streaming/data role: Kafka + Avro + Schema Registry are a real match, but the orchestration
        //    stack (Spark/Airflow/dbt) is absent. Genuine mid fit.
        new MatchCase(
            Name: "mid-fit-data-streaming-engineer",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Data Streaming Engineer",
                Company: "Lakeside Data",
                Location: "Remote",
                Url: "https://example.com/jobs/data-streaming",
                Summary: "Streaming-focused data engineer building real-time pipelines on Kafka.",
                Description:
                    "Build real-time data pipelines. Strong Apache Kafka and Avro / Schema Registry " +
                    "experience required (you have this if you've run event-driven systems). Also expected: " +
                    "Spark or Flink for stream processing, Airflow for orchestration, and dbt for " +
                    "transformations, with Python as the primary language. Data-warehouse modelling a plus."),
            Criteria: new SearchCriteria(
                Roles: ["Data Engineer", "Streaming Engineer"],
                Locations: ["Remote"],
                Seniority: "Senior",
                MustHaveSkills: ["Kafka", "Avro", "Spark", "Airflow", "Python"],
                NiceToHaveSkills: ["dbt"],
                WorkStyles: ["Remote"],
                SalaryExpectation: null),
            MinScore: 42,
            MaxScore: 74,
            ExpectedMatchedSkills: ["Kafka"],
            TargetScore: 58),

        // ── Clear mismatch on seniority AND domain: a junior front-end role. Should score low.
        new MatchCase(
            Name: "weak-fit-junior-frontend-react",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Junior Frontend Engineer (React)",
                Company: "Pixel Studio",
                Location: "Onsite - Melbourne",
                Url: "https://example.com/jobs/junior-frontend",
                Summary: "Early-career front-end engineer building UI in React and TypeScript.",
                Description:
                    "Junior front-end role (1-3 years). Build user interfaces in React and TypeScript, " +
                    "style with CSS/Tailwind, and collaborate with designers. No backend responsibilities. " +
                    "We are specifically hiring at the junior level for growth into the team."),
            Criteria: new SearchCriteria(
                Roles: ["Junior Frontend Engineer"],
                Locations: ["Melbourne"],
                Seniority: "Junior",
                MustHaveSkills: ["React", "TypeScript", "CSS"],
                NiceToHaveSkills: ["Tailwind"],
                WorkStyles: ["Onsite"],
                SalaryExpectation: null),
            MinScore: 0,
            MaxScore: 38,
            ExpectedMatchedSkills: [],
            TargetScore: 16),

        // ── Grounding trap: primary language (Go) plus Kubernetes / gRPC are ALL absent from the resume.
        //    Backend domain overlaps, but the missing core skills must keep the score down — and the
        //    matcher must NOT copy Go/Kubernetes/gRPC into matchedSkills.
        new MatchCase(
            Name: "weak-fit-golang-backend-core",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Backend Engineer (Go)",
                Company: "Comet Systems",
                Location: "Remote",
                Url: "https://example.com/jobs/golang-backend",
                Summary: "Backend engineer building high-throughput services in Go.",
                Description:
                    "Build high-throughput backend services in Go (Golang) — this is the primary language " +
                    "and a hard requirement. Deep Kubernetes and gRPC experience required. You'll work on " +
                    "distributed systems at scale. Other backend languages will not substitute for hands-on Go."),
            Criteria: new SearchCriteria(
                Roles: ["Backend Engineer"],
                Locations: ["Remote"],
                Seniority: "Senior",
                MustHaveSkills: ["Go", "Kubernetes", "gRPC"],
                NiceToHaveSkills: ["Microservices"],
                WorkStyles: ["Remote"],
                SalaryExpectation: null),
            MinScore: 10,
            MaxScore: 55,
            ExpectedMatchedSkills: [],
            TargetScore: 32),

        // ── Top of the band: a principal architect role that maps almost 1:1 onto the resume's
        //    DDD / Clean Architecture / CQRS / event-sourcing / cloud experience.
        new MatchCase(
            Name: "strong-fit-principal-architect",
            Resume: DotNetBackendResume,
            Posting: new JobPosting(
                Title: "Principal Software Architect (.NET)",
                Company: "Meridian Cloud",
                Location: "Remote",
                Url: "https://example.com/jobs/principal-architect",
                Summary: "Principal architect setting technical direction for a .NET event-driven platform.",
                Description:
                    "Set architecture and technical direction across teams. Deep C# / .NET, Domain-Driven " +
                    "Design, Clean Architecture, CQRS and event sourcing required. Design event-driven " +
                    "microservices on AWS with Kafka. Define standards, review designs, and mentor senior " +
                    "engineers. 10+ years, with a track record of leading large-scale system design."),
            Criteria: new SearchCriteria(
                Roles: ["Principal Architect", "Software Architect"],
                Locations: ["Remote"],
                Seniority: "Principal",
                MustHaveSkills: ["C#", ".NET", "Domain-Driven Design", "Clean Architecture", "AWS"],
                NiceToHaveSkills: ["Kafka", "CQRS"],
                WorkStyles: ["Remote"],
                SalaryExpectation: null),
            MinScore: 78,
            MaxScore: 100,
            ExpectedMatchedSkills: ["C#", ".NET", "AWS"],
            TargetScore: 92),
    ];
}
