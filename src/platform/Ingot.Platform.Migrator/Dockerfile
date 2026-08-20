# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/shared/Ingot.Domain/Ingot.Domain.csproj src/shared/Ingot.Domain/
COPY src/shared/Ingot.Contracts/Ingot.Contracts.csproj src/shared/Ingot.Contracts/
COPY src/shared/Ingot.Agent.Contracts/Ingot.Agent.Contracts.csproj src/shared/Ingot.Agent.Contracts/
COPY src/agent/Ingot.Agent/Ingot.Agent.csproj src/agent/Ingot.Agent/
COPY src/platform/Ingot.Platform.Application/Ingot.Platform.Application.csproj src/platform/Ingot.Platform.Application/
COPY src/platform/Ingot.Platform.Infrastructure/Ingot.Platform.Infrastructure.csproj src/platform/Ingot.Platform.Infrastructure/
COPY src/platform/Ingot.Platform.Migrator/Ingot.Platform.Migrator.csproj src/platform/Ingot.Platform.Migrator/
RUN dotnet restore src/platform/Ingot.Platform.Migrator/Ingot.Platform.Migrator.csproj

COPY src/shared/Ingot.Domain/ src/shared/Ingot.Domain/
COPY src/shared/Ingot.Contracts/ src/shared/Ingot.Contracts/
COPY src/shared/Ingot.Agent.Contracts/ src/shared/Ingot.Agent.Contracts/
COPY src/agent/Ingot.Agent/ src/agent/Ingot.Agent/
COPY src/platform/Ingot.Platform.Application/ src/platform/Ingot.Platform.Application/
COPY src/platform/Ingot.Platform.Infrastructure/ src/platform/Ingot.Platform.Infrastructure/
COPY src/platform/Ingot.Platform.Migrator/ src/platform/Ingot.Platform.Migrator/
RUN dotnet publish src/platform/Ingot.Platform.Migrator/Ingot.Platform.Migrator.csproj \
    -c Release -o /app/publish --no-restore

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Ingot.Platform.Migrator.dll"]
