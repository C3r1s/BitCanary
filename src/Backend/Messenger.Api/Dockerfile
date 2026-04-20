# syntax=docker/dockerfile:1.7
# Build stage — per D-04 (sdk:10.0) and D-05 (build context = src/)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Layer 1: .slnx + backend/shared .csproj files only (per D-06, csproj-first for cache).
# Client .csproj is intentionally NOT copied — restore targets Messenger.Api.csproj directly
# to avoid pulling in client/tests projects that Messenger.slnx references outside src/.
COPY Messenger.slnx ./
COPY Backend/Messenger.Api/Messenger.Api.csproj Backend/Messenger.Api/
COPY Backend/Messenger.Application/Messenger.Application.csproj Backend/Messenger.Application/
COPY Backend/Messenger.Domain/Messenger.Domain.csproj Backend/Messenger.Domain/
COPY Backend/Messenger.Infrastructure/Messenger.Infrastructure.csproj Backend/Messenger.Infrastructure/
COPY Shared/Messenger.Shared.Contracts/Messenger.Shared.Contracts.csproj Shared/Messenger.Shared.Contracts/

# Layer 2: restore — cached unless any .csproj above changed.
# Target the Api .csproj (NOT the .slnx) because the .slnx references projects outside src/.
# Preview NuGet packages are pinned by exact version in .csproj — no --include-prerelease flag needed.
RUN dotnet restore Backend/Messenger.Api/Messenger.Api.csproj

# Layer 3: source — changes frequently, isolated from restore layer.
COPY Backend/ Backend/
COPY Shared/ Shared/

# Layer 4: publish (restore runs here too to generate assets files in publish layer).
RUN dotnet publish Backend/Messenger.Api/Messenger.Api.csproj \
    -c Release -o /app/publish

# Runtime stage — per D-04 (aspnet:10.0 Ubuntu Noble) and D-07 (non-root).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user (Ubuntu Noble syntax — groupadd/useradd; image does not ship addgroup/adduser).
RUN groupadd --system appgroup \
 && useradd --system --gid appgroup --no-create-home appuser

# Copy published output from build stage.
COPY --from=build /app/publish .

# Pre-create storage directory and give appuser ownership BEFORE switching user,
# so the bind mount target (./storage:/app/storage from compose) is writable.
RUN mkdir -p /app/storage \
 && chown -R appuser:appgroup /app/storage

# Bind to 0.0.0.0:8080 inside the container (host port mapping handled by compose — D-03).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER appuser
ENTRYPOINT ["dotnet", "Messenger.Api.dll"]
