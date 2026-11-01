[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskProject = 'notification-verify-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$taskArtifacts = Join-Path $taskRepository 'artifacts/test-results/notification-deployment'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
& (Join-Path $PSScriptRoot 'Initialize-NotificationLocal.ps1')
$taskCompose = @('--project-name', $taskProject, '--env-file', (Join-Path $taskRepository '.local/notification/compose.env'),
    '-f', (Join-Path $taskRepository 'docker-compose.yml'),
    '-f', (Join-Path $taskRepository 'docker/notification/verify.override.yml'))
function Invoke-Compose { & docker compose @taskCompose @args; if ($LASTEXITCODE -ne 0) { throw 'Fallo un paso de Docker Compose; revisa el estado del proyecto temporal.' } }
function Service-Url([string]$Service) {
    $taskBinding = Invoke-Compose port $Service 8080
    if ($taskBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Puerto del entorno aislado inesperado.' }
    "http://127.0.0.1:$($Matches[1])"
}
function Request([string]$Method, [string]$Url, [hashtable]$Headers = @{}, $Body = $null) {
    $taskArguments = @{ Method=$Method; Uri=$Url; Headers=$Headers; SkipHttpErrorCheck=$true; TimeoutSec=15 }
    if ($null -ne $Body) { $taskArguments.ContentType='application/json'; $taskArguments.Body=($Body | ConvertTo-Json -Depth 8 -Compress) }
    Invoke-WebRequest @taskArguments
}
function Expect($Response, [int]$Status, [string]$Description) {
    if ([int]$Response.StatusCode -ne $Status) { throw "$Description : HTTP $($Response.StatusCode), esperado $Status" }
    Write-Output "OK: $Description"
}
function Wait-Ready([string]$Url, [int]$Status) {
    $taskWaitDeadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        try { $taskWaitResponse = Request GET "$Url/health/ready" } catch { $taskWaitResponse=$null }
        if ($null -ne $taskWaitResponse -and [int]$taskWaitResponse.StatusCode -eq $Status) { return $taskWaitResponse }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $taskWaitDeadline)
    throw "Readiness no alcanzo HTTP $Status en el tiempo previsto."
}
$taskReport = [ordered]@{ project=$taskProject; checkedAtUtc=[DateTimeOffset]::UtcNow; fullBoardingFlowVerified=$false; firebaseRealVerified=$false; checks=@() }
try {
    if (!$SkipBuild) { Invoke-Compose build notification monolith trip-tracking gateway }
    Invoke-Compose up -d --wait --wait-timeout 240
    $taskGateway = Service-Url gateway
    $taskMonolith = Service-Url monolith
    $taskNotification = Service-Url notification
    Expect (Request GET "$taskGateway/health/ready") 200 'Readiness conjunto'
    Expect (Request GET "$taskGateway/api/v1/notifications") 401 'Gateway rechaza acceso anonimo'
    Expect (Request GET "$taskGateway/metrics") 404 'Gateway no publica metricas'
    Expect (Request GET "$taskNotification/metrics") 404 'Metricas requieren secreto'
    $taskMonitoring = @{ 'X-Metrics-Token' = [IO.File]::ReadAllText((Join-Path $taskRepository '.local/notification/NotificationMonitoring__Token')) }
    Expect (Request GET "$taskNotification/metrics" $taskMonitoring) 200 'Exportador Prometheus protegido'
    $taskMetrics = Request GET "$taskNotification/metrics" $taskMonitoring
    if ($taskMetrics.Content -notmatch '(?m)^notification_backlog_sample_available(?:\{[^\r\n]*\})? 1') { throw 'No se pudo muestrear el backlog real de MySQL.' }
    $taskOpenApi = Request GET "$taskNotification/openapi/v1.json" $taskMonitoring
    Expect $taskOpenApi 200 'OpenAPI disponible'
    [IO.File]::WriteAllText((Join-Path $taskArtifacts 'openapi.json'), $taskOpenApi.Content)
    $taskTokens = @()
    foreach ($taskIndex in 1..2) {
        $taskEmail = "admin.$taskIndex.$taskProject@verification.test"
        $taskPassword = 'TestOnly123!' + [Guid]::NewGuid().ToString('N')
        $taskProvision = Request POST "$taskMonolith/api/v1/organizations" @{'X-Provisioning-Key'='local-docker-provisioning-key-0123456789abcdef'} @{
            name="Verificacion $taskIndex $taskProject"; administrator=@{firstName='Admin';lastName='Prueba';email=$taskEmail;password=$taskPassword}
        }
        Expect $taskProvision 201 'Provisionamiento de organizacion aislada'
        $taskSignIn = Request POST "$taskGateway/api/v1/users/sign-in" @{} @{email=$taskEmail;password=$taskPassword}
        Expect $taskSignIn 200 'JWT real emitido por IAM a traves de Gateway'
        $taskTokens += ($taskSignIn.Content | ConvertFrom-Json).token
    }
    $taskA = @{Authorization="Bearer $($taskTokens[0])"}; $taskB = @{Authorization="Bearer $($taskTokens[1])"}
    $taskCreated = Request PUT "$taskGateway/api/v1/push-subscriptions/$([Guid]::NewGuid())" $taskA @{token="test-only-$taskProject";platform='Web'}
    Expect $taskCreated 200 'IAM vigente + Gateway + persistencia propia de Notification'
    $taskSubscription = ($taskCreated.Content | ConvertFrom-Json).id
    Expect (Request DELETE "$taskGateway/api/v1/push-subscriptions/$taskSubscription" $taskB) 404 'Otro transportista no puede desactivar la suscripcion'
    $taskOther = Request GET "$taskGateway/api/v1/push-subscriptions" $taskB
    Expect $taskOther 200 'Bandeja de suscripciones propia del otro transportista'
    if (@($taskOther.Content | ConvertFrom-Json).Count -ne 0) { throw 'Se filtro una suscripcion entre transportistas.' }
    Invoke-Compose restart notification
    # Los puertos publicados dinamicamente pueden cambiar al reiniciar el contenedor.
    $taskNotification = Service-Url notification
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        Start-Sleep -Seconds 2
        try { $taskRestarted = Request GET "$taskNotification/health/ready" } catch { $taskRestarted=$null }
    } while (($null -eq $taskRestarted -or $taskRestarted.StatusCode -ne 200) -and [DateTimeOffset]::UtcNow -lt $taskDeadline)
    Expect $taskRestarted 200 'Recuperacion tras reinicio del proceso'
    $taskStored = Request GET "$taskGateway/api/v1/push-subscriptions" $taskA
    Expect $taskStored 200 'Datos conservados tras reinicio'
    if (@($taskStored.Content | ConvertFrom-Json).Count -ne 1) { throw 'No se conservo la suscripcion.' }
    foreach ($taskDependency in @('redis', 'monolith', 'rabbitmq')) {
        Invoke-Compose stop $taskDependency
        Expect (Wait-Ready $taskNotification 503) 503 "Readiness detecta caida de $taskDependency"
        Expect (Request GET "$taskNotification/health/live") 200 'Liveness permanece disponible'
        Invoke-Compose start $taskDependency
        Expect (Wait-Ready $taskNotification 200) 200 "Readiness se recupera tras volver $taskDependency"
    }
    # Este fallo se registra como BLOQUEO conocido, no se suplanta un directorio inexistente.
    Expect (Request GET "$taskGateway/api/v1/notifications" $taskA) 503 'Directorio remoto pendiente: falla de forma cerrada'
    $taskReport.checks = @('compose-healthy','protected-metrics','mysql-backlog-sampled','openapi','real-iam-jwt','tenant-isolation','restart-persistence','redis-recovery','iam-recovery','rabbitmq-recovery')
    $taskReport.blockers = @('Monolith no publica los cuatro endpoints internos del directorio.', 'PARENT sin organizationId requiere contexto autorizado acordado.', 'Firebase real requiere proyecto/identidad/cliente.')
    $taskReport.outcome='passed-with-external-blockers'
}
catch
{
    $taskReport.outcome='failed'
    $taskReport.containerStates = @(Invoke-Compose ps --all --format json | ForEach-Object {
        $taskState = $_ | ConvertFrom-Json
        [pscustomobject]@{service=$taskState.Service;state=$taskState.State;health=$taskState.Health;exitCode=$taskState.ExitCode}
    })
    throw
}
finally {
    $taskReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskArtifacts 'deployment.json') -Encoding utf8
    # Solo el proyecto creado en ESTA ejecucion. Nunca se usa el proyecto rutasegura ni volumenes existentes.
    if ($taskProject -match '^notification-verify-[0-9a-f]{12}$') {
        & docker compose @taskCompose down --volumes --remove-orphans
        if ($LASTEXITCODE -ne 0) { Write-Warning "No se pudo retirar completamente $taskProject." }
    }
}
