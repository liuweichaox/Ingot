FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Ingot.Domain/Ingot.Domain.csproj src/Ingot.Domain/
COPY src/Ingot.Contracts/Ingot.Contracts.csproj src/Ingot.Contracts/
COPY src/Ingot.Application/Ingot.Application.csproj src/Ingot.Application/
COPY src/Ingot.Infrastructure/Ingot.Infrastructure.csproj src/Ingot.Infrastructure/
COPY src/Ingot.Connector.Host/Ingot.Connector.Host.csproj src/Ingot.Connector.Host/
RUN dotnet restore src/Ingot.Connector.Host/Ingot.Connector.Host.csproj

COPY src/Ingot.Domain/ src/Ingot.Domain/
COPY src/Ingot.Contracts/ src/Ingot.Contracts/
COPY src/Ingot.Application/ src/Ingot.Application/
COPY src/Ingot.Infrastructure/ src/Ingot.Infrastructure/
COPY src/Ingot.Connector.Host/ src/Ingot.Connector.Host/
RUN dotnet publish src/Ingot.Connector.Host/Ingot.Connector.Host.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish/ ./

ENV ASPNETCORE_URLS=http://+:8001
EXPOSE 8001

ENTRYPOINT ["dotnet", "Ingot.Connector.Host.dll"]
