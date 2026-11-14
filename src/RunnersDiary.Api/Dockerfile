# API image (ADR 0030 §5). Build context: the repository root.
#   docker build -f src/RunnersDiary.Api/Dockerfile -t rd-api .
# Base images are pinned by digest (Dependabot's docker ecosystem bumps tag + digest together).

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src

# Restore first, from the project files only, so source edits do not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/RunnersDiary.Api/RunnersDiary.Api.csproj src/RunnersDiary.Api/
COPY src/RunnersDiary.Domain/RunnersDiary.Domain.csproj src/RunnersDiary.Domain/
COPY src/RunnersDiary.Infrastructure/RunnersDiary.Infrastructure.csproj src/RunnersDiary.Infrastructure/
COPY src/RunnersDiary.ServiceDefaults/RunnersDiary.ServiceDefaults.csproj src/RunnersDiary.ServiceDefaults/
RUN dotnet restore src/RunnersDiary.Api/RunnersDiary.Api.csproj

COPY src/RunnersDiary.Api src/RunnersDiary.Api
COPY src/RunnersDiary.Domain src/RunnersDiary.Domain
COPY src/RunnersDiary.Infrastructure src/RunnersDiary.Infrastructure
COPY src/RunnersDiary.ServiceDefaults src/RunnersDiary.ServiceDefaults
# The demo dataset (runs.json + GPX files) is copied into the publish output by the Api project (ADR 0011).
COPY seed/demo seed/demo
# The synthetic demo race catalogue (M8) is content of the Api project too; it is only read when Races:DemoCatalogue:SeedOnStartup=true (never in Azure).
COPY seed/races seed/races
# Training Insights prompts, templates and demo fixtures are content of the Infrastructure project (ADR 0026 §6).
COPY prompts/training-insights prompts/training-insights

# OpenApiGenerateDocuments=false: the committed OpenAPI document is produced (and checked) by the api-contract CI job;
# generating it here would start the app during the image build.
RUN dotnet publish src/RunnersDiary.Api/RunnersDiary.Api.csproj -c Release \
    --no-restore -p:UseAppHost=false -p:OpenApiGenerateDocuments=false -o /app

# chiseled-extra: distroless, non-root (UID 1654 "app"), no shell, with ICU and tzdata, so IANA zones such as
# Europe/Warsaw resolve (ADR 0008) and culture data works without InvariantGlobalization.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:cae5b962bfb786606def03d250c6a645cc98839095c89e806e4bd8978a68115b AS final
WORKDIR /app
COPY --from=build /app .
# The base image already sets ASPNETCORE_HTTP_PORTS=8080 and runs as the non-root user `app`.
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "RunnersDiary.Api.dll"]
