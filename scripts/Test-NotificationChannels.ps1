[CmdletBinding()]
param([string]$DockerExecutable = 'docker')

$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskCore = Join-Path $taskRepository 'src/Services/Notification/tests/RutaSegura.Notification.Tests/RutaSegura.Notification.Tests.csproj'
$taskResults = Join-Path $taskRepository 'artifacts/test-results/notification-api-channels'

# API/JWT, contratos y proxy HTTP/WebSocket reales, sin depender de IAM/Roster productivos.
& dotnet test $taskCore -c Release --verbosity minimal --logger trx --results-directory $taskResults
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de core/API/Gateway.' }

# Cada script provisiona y retira solo sus contenedores efimeros; no usa las bases del usuario.
& (Join-Path $PSScriptRoot 'Test-NotificationPersistence.ps1') -DockerExecutable $DockerExecutable
& (Join-Path $PSScriptRoot 'Test-NotificationMessaging.ps1') -DockerExecutable $DockerExecutable
Write-Output 'Verificacion de Notifications completada. Firebase real y directorios productivos requieren configuracion externa.'
