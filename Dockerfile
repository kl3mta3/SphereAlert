FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 7227
# curl is used by the container HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SphereAlert/SphereAlert.csproj", "SphereAlert/"]
RUN dotnet restore "SphereAlert/SphereAlert.csproj"
COPY . .
RUN dotnet publish "SphereAlert/SphereAlert.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY entrypoint.sh /entrypoint.sh
RUN mkdir -p /data && chmod +x /entrypoint.sh

# The data volume holds the SQLite database, the encryption keyfile, and logs.
ENV SPHEREALERT_DATA_DIR=/data
ENV SPHEREALERT_LOG_LEVEL=Info

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -fsS http://localhost:7227/healthz || exit 1

ENTRYPOINT ["/entrypoint.sh"]
