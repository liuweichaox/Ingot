# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/shared/Ingot.Contracts/Ingot.Contracts.csproj src/shared/Ingot.Contracts/
COPY src/shared/Ingot.Domain/Ingot.Domain.csproj src/shared/Ingot.Domain/
COPY src/agent/Ingot.Agent/Ingot.Agent.csproj src/agent/Ingot.Agent/
COPY src/agent/Ingot.Agent.Providers/Ingot.Agent.Providers.csproj src/agent/Ingot.Agent.Providers/
COPY src/platform/Ingot.Platform.Infrastructure/Ingot.Platform.Infrastructure.csproj src/platform/Ingot.Platform.Infrastructure/
COPY src/platform/Ingot.Platform.Api/Ingot.Platform.Api.csproj src/platform/Ingot.Platform.Api/
RUN dotnet restore src/platform/Ingot.Platform.Api/Ingot.Platform.Api.csproj

COPY src/shared/Ingot.Domain/ src/shared/Ingot.Domain/
COPY src/shared/Ingot.Contracts/ src/shared/Ingot.Contracts/
COPY src/agent/Ingot.Agent/ src/agent/Ingot.Agent/
COPY src/agent/Ingot.Agent.Providers/ src/agent/Ingot.Agent.Providers/
COPY src/platform/Ingot.Platform.Infrastructure/ src/platform/Ingot.Platform.Infrastructure/
COPY src/platform/Ingot.Platform.Api/ src/platform/Ingot.Platform.Api/
RUN dotnet publish src/platform/Ingot.Platform.Api/Ingot.Platform.Api.csproj \
    -c Release -o /app/publish --no-restore

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8000
EXPOSE 8000

ENTRYPOINT ["dotnet", "Ingot.Platform.Api.dll"]
