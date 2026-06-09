# Multi-stage build for the Blazor Server Web app.
# Stage 1: restore + publish with the full SDK.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy the build/package coordination files first so restore layers cache well.
COPY *.sln Directory.Build.props Directory.Packages.props global.json ./
# Copy every project file, preserving paths, so `dotnet restore` sees the whole graph.
COPY src/ src/
COPY tests/ tests/
COPY eval/ eval/

RUN dotnet restore src/JobAgents.Web/JobAgents.Web.csproj
RUN dotnet publish src/JobAgents.Web/JobAgents.Web.csproj -c Release -o /app/publish --no-restore

# Stage 2: slim ASP.NET runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Kestrel listens on 8080 inside the container; the host maps it.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "JobAgents.Web.dll"]
