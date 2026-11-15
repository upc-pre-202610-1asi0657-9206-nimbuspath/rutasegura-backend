# API Gateway (YARP): punto de entrada único. Se construye desde la raíz del repositorio:
#   docker build -f src/ApiGateway/Dockerfile -t rutasegura-gateway .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ApiGateway/RutaSegura.ApiGateway.csproj src/ApiGateway/
RUN dotnet restore src/ApiGateway/RutaSegura.ApiGateway.csproj
COPY src/ApiGateway/ src/ApiGateway/
RUN dotnet publish src/ApiGateway/RutaSegura.ApiGateway.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=10 CMD curl -fsS http://localhost:8080/health/live || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "RutaSegura.ApiGateway.dll"]
