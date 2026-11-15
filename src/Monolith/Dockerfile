# Monolito RutaSegura (Iam + Administration) — imagen multi-stage.
# Contexto de build: raíz del repositorio (necesita Directory.Build.props).
#   docker build -f src/Monolith/Dockerfile -t rutasegura/monolith .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Monolith/Monolith.csproj src/Monolith/
RUN dotnet restore src/Monolith/Monolith.csproj
COPY src/Monolith/ src/Monolith/
RUN dotnet publish src/Monolith/Monolith.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# tzdata: los horarios de las rutas se interpretan en America/Lima. curl: healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=10 CMD curl -fsS http://localhost:8080/health || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "Monolith.dll"]
