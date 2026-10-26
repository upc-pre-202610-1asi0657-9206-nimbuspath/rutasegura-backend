# Build desde la raiz. Como los Dockerfiles del backend, el SDK del contenedor se fija mediante la imagen.
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Notification/src/ src/Services/Notification/src/
RUN dotnet restore src/Services/Notification/src/RutaSegura.Notification.Api/RutaSegura.Notification.Api.csproj
RUN dotnet publish src/Services/Notification/src/RutaSegura.Notification.Api/RutaSegura.Notification.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 CMD curl -fsS http://localhost:8080/health/ready || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "RutaSegura.Notification.Api.dll"]
