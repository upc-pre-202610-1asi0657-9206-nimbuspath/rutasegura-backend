[CmdletBinding()]
param([string]$DockerExecutable = 'docker')

$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRepository 'src/Services/Notification/tests/RutaSegura.Notification.Persistence.Tests/RutaSegura.Notification.Persistence.Tests.csproj'
$taskResults = Join-Path $taskRepository 'artifacts/test-results/notification-persistence'
$taskContainerName = 'rutasegura-notification-tests-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$taskPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$taskPreviousConnection = $env:NOTIFICATION_TEST_MYSQL
$taskContainerId = $null

try {
    & $DockerExecutable version --format '{{.Server.Version}}'
    if ($LASTEXITCODE -ne 0) { throw 'Inicia el motor de Docker Desktop antes de ejecutar las pruebas.' }

    $taskHealth = 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql --protocol=TCP -h127.0.0.1 -uroot -e "SELECT 1" >/dev/null 2>&1'
    $taskRunResult = & $DockerExecutable run --detach --rm --name $taskContainerName `
        --label 'rutasegura.purpose=notification-persistence-tests' --publish '127.0.0.1::3306' `
        --tmpfs '/var/lib/mysql' --env "MYSQL_ROOT_PASSWORD=$taskPassword" --env 'MYSQL_ROOT_HOST=%' `
        --health-cmd $taskHealth --health-interval 2s --health-timeout 3s --health-retries 45 `
        mysql:8.0 --character-set-server=utf8mb4 --collation-server=utf8mb4_bin
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el contenedor MySQL de pruebas.' }
    $taskContainerId = ($taskRunResult | Select-Object -Last 1).Trim()
    if ($taskContainerId -notmatch '^[0-9a-f]{64}$') { throw 'Identificador de contenedor inesperado.' }

    Write-Output "Esperando MySQL 8.0 temporal: $taskContainerName"
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
        $taskHealthStatus = & $DockerExecutable inspect --format '{{.State.Health.Status}}' $taskContainerId
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar el contenedor de pruebas.' }
        if ($taskHealthStatus -eq 'healthy') { break }
        if ($taskHealthStatus -eq 'unhealthy') { throw 'MySQL de pruebas no alcanzo un estado saludable.' }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    if ($taskHealthStatus -ne 'healthy') { throw 'Se agoto la espera por MySQL de pruebas.' }

    $taskBinding = & $DockerExecutable port $taskContainerId '3306/tcp'
    if ($LASTEXITCODE -ne 0 -or $taskBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Publicacion local de puerto inesperada.' }
    $taskPort = $Matches[1]
    $env:NOTIFICATION_TEST_MYSQL = "Server=127.0.0.1;Port=$taskPort;UserID=root;Password=$taskPassword;SslMode=Disabled;Pooling=False;Connection Timeout=10"
    & dotnet test $taskProject -c Release -p:NuGetAudit=false --verbosity minimal --logger trx --results-directory $taskResults
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas de persistencia fallaron; revisa los resultados TRX.' }
}
finally {
    $env:NOTIFICATION_TEST_MYSQL = $taskPreviousConnection
    if ($taskContainerId -match '^[0-9a-f]{64}$') {
        & $DockerExecutable stop --time 10 $taskContainerId | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Output 'Contenedor temporal retirado; sus datos de prueba eran efimeros.' }
        else { Write-Warning "No se pudo detener el contenedor de pruebas $taskContainerName." }
    }
}
