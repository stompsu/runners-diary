# Migration image (ADR 0032): EF Core migration bundle + go-sqlcmd for the grant script. Build context: the repo root.
#   docker build -f infra/docker/migrate.Dockerfile -t rd-migrate .
# The bundle is self-contained, so the runtime image only needs the OS libraries (runtime-deps).

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS bundle
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/dotnet-tools.json .config/dotnet-tools.json
COPY src/RunnersDiary.Api/RunnersDiary.Api.csproj src/RunnersDiary.Api/
COPY src/RunnersDiary.Domain/RunnersDiary.Domain.csproj src/RunnersDiary.Domain/
COPY src/RunnersDiary.Infrastructure/RunnersDiary.Infrastructure.csproj src/RunnersDiary.Infrastructure/
COPY src/RunnersDiary.ServiceDefaults/RunnersDiary.ServiceDefaults.csproj src/RunnersDiary.ServiceDefaults/
RUN dotnet tool restore && dotnet restore src/RunnersDiary.Api/RunnersDiary.Api.csproj

COPY src/RunnersDiary.Api src/RunnersDiary.Api
COPY src/RunnersDiary.Domain src/RunnersDiary.Domain
COPY src/RunnersDiary.Infrastructure src/RunnersDiary.Infrastructure
COPY src/RunnersDiary.ServiceDefaults src/RunnersDiary.ServiceDefaults
COPY seed/demo seed/demo
# The synthetic demo race catalogue is content of the Api project too (M8); the bundle build needs the file to exist.
COPY seed/races seed/races
COPY prompts/training-insights prompts/training-insights

# The bundle command builds and publishes the startup project for the target runtime itself. OpenApiGenerateDocuments=false
# (an MSBuild property, read from the environment) keeps the build from starting the app to write the OpenAPI document.
ENV OpenApiGenerateDocuments=false
RUN case "$TARGETARCH" in arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac \
    && dotnet ef migrations bundle --self-contained -r "$rid" \
       --project src/RunnersDiary.Infrastructure --startup-project src/RunnersDiary.Api \
       --configuration Release --verbose --force -o /out/efbundle

# go-sqlcmd: pinned release, verified by SHA-256 (published by the project with the release assets).
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble@sha256:9cab4d7d886226d47b5489ef9abf0544e2f109fde11a8acc190217610af26c00 AS sqlcmd
ARG TARGETARCH
ARG SQLCMD_VERSION=v1.10.0
ARG SQLCMD_SHA256_AMD64=92516d98c63d99b0994de5b61350c91f6915f9b76f139a59039fbcb225c2e987
ARG SQLCMD_SHA256_ARM64=9faaa981f9c374f319ac796dedb4678499b8596c87d5b6c512e9b0e7a3b74f8e
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl bzip2 \
    && rm -rf /var/lib/apt/lists/*
RUN case "$TARGETARCH" in arm64) arch=arm64; sha="$SQLCMD_SHA256_ARM64" ;; *) arch=amd64; sha="$SQLCMD_SHA256_AMD64" ;; esac \
    && curl -fsSL -o /tmp/sqlcmd.tar.bz2 "https://github.com/microsoft/go-sqlcmd/releases/download/${SQLCMD_VERSION}/sqlcmd-linux-${arch}.tar.bz2" \
    && echo "${sha}  /tmp/sqlcmd.tar.bz2" | sha256sum -c - \
    && mkdir /out && tar -xjf /tmp/sqlcmd.tar.bz2 -C /out sqlcmd

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble@sha256:9cab4d7d886226d47b5489ef9abf0544e2f109fde11a8acc190217610af26c00 AS final
WORKDIR /migrate
COPY --from=bundle /out/efbundle ./efbundle
COPY --from=sqlcmd /out/sqlcmd ./sqlcmd
COPY infra/sql/grant-app-users.sql ./grant-app-users.sql
COPY --chmod=0755 infra/docker/migrate.sh ./migrate.sh
USER 1654
ENTRYPOINT ["/migrate/migrate.sh"]
