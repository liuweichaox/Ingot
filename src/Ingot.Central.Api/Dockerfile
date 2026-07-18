# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Ingot.Contracts/Ingot.Contracts.csproj src/Ingot.Contracts/
COPY src/Ingot.Domain/Ingot.Domain.csproj src/Ingot.Domain/
COPY src/Ingot.Agent/Ingot.Agent.csproj src/Ingot.Agent/
COPY src/Ingot.Agent.Infrastructure/Ingot.Agent.Infrastructure.csproj src/Ingot.Agent.Infrastructure/
COPY src/Ingot.Central.Infrastructure/Ingot.Central.Infrastructure.csproj src/Ingot.Central.Infrastructure/
COPY src/Ingot.Central.Api/Ingot.Central.Api.csproj src/Ingot.Central.Api/
RUN dotnet restore src/Ingot.Central.Api/Ingot.Central.Api.csproj

COPY src/Ingot.Domain/ src/Ingot.Domain/
COPY src/Ingot.Contracts/ src/Ingot.Contracts/
COPY src/Ingot.Agent/ src/Ingot.Agent/
COPY src/Ingot.Agent.Infrastructure/ src/Ingot.Agent.Infrastructure/
COPY src/Ingot.Central.Infrastructure/ src/Ingot.Central.Infrastructure/
COPY src/Ingot.Central.Api/ src/Ingot.Central.Api/
RUN dotnet publish src/Ingot.Central.Api/Ingot.Central.Api.csproj \
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

ENTRYPOINT ["dotnet", "Ingot.Central.Api.dll"]
