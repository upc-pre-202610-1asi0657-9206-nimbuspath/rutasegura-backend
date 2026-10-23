# Trip & Tracking Service — imagen multi-stage.
# Contexto de build: raíz del repositorio (necesita BuildingBlocks y Directory.Build.props).
#   docker build -f src/Services/Trip-Tracking/Dockerfile -t rutasegura/trip-tracking .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Trip-Tracking/src/ src/Services/Trip-Tracking/src/
RUN dotnet restore src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api/RutaSegura.TripTracking.Api.csproj
RUN dotnet publish src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api/RutaSegura.TripTracking.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# tzdata: las rutas se programan en hora de Lima (America/Lima).
RUN apt-get update && apt-get install -y --no-install-recommends tzdata curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=5 CMD curl -fsS http://localhost:8080/health/live || exit 1
USER $APP_UID
# "--migrate" aplica las migraciones de EF Core antes de atender tráfico (ver Persistence:MigrateOnStartup).
ENTRYPOINT ["dotnet", "RutaSegura.TripTracking.Api.dll"]
