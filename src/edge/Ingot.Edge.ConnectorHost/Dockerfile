ARG TARGETARCH
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# 根级 MSBuild 约定（ImplicitUsings/Nullable/LangVersion 等）—— 必须先于 restore/build 拷入，
# 否则各项目缺失隐式 using System，会报 CS0246: DateTimeOffset/IReadOnlyDictionary 找不到。
COPY Directory.Build.props ./

COPY src/shared/Ingot.Domain/Ingot.Domain.csproj src/shared/Ingot.Domain/
COPY src/shared/Ingot.Contracts/Ingot.Contracts.csproj src/shared/Ingot.Contracts/
COPY src/edge/Ingot.Edge.Application/Ingot.Edge.Application.csproj src/edge/Ingot.Edge.Application/
COPY src/edge/Ingot.Edge.Infrastructure/Ingot.Edge.Infrastructure.csproj src/edge/Ingot.Edge.Infrastructure/
COPY src/edge/Ingot.Edge.ConnectorHost/Ingot.Edge.ConnectorHost.csproj src/edge/Ingot.Edge.ConnectorHost/
RUN case "${TARGETARCH}" in \
    amd64) RID=linux-x64 ;; \
    arm64) RID=linux-arm64 ;; \
    *) echo "Unsupported Edge container architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac && dotnet restore src/edge/Ingot.Edge.ConnectorHost/Ingot.Edge.ConnectorHost.csproj --runtime "${RID}"

COPY src/shared/Ingot.Domain/ src/shared/Ingot.Domain/
COPY src/shared/Ingot.Contracts/ src/shared/Ingot.Contracts/
COPY src/edge/Ingot.Edge.Application/ src/edge/Ingot.Edge.Application/
COPY src/edge/Ingot.Edge.Infrastructure/ src/edge/Ingot.Edge.Infrastructure/
COPY src/edge/Ingot.Edge.ConnectorHost/ src/edge/Ingot.Edge.ConnectorHost/
RUN case "${TARGETARCH}" in \
    amd64) RID=linux-x64 ;; \
    arm64) RID=linux-arm64 ;; \
    *) echo "Unsupported Edge container architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac && dotnet publish src/edge/Ingot.Edge.ConnectorHost/Ingot.Edge.ConnectorHost.csproj \
    -c Release -o /app/publish --runtime "${RID}" --self-contained false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish/ ./

ENV ASPNETCORE_URLS=http://+:8001
EXPOSE 8001

ENTRYPOINT ["dotnet", "Ingot.Edge.ConnectorHost.dll"]
