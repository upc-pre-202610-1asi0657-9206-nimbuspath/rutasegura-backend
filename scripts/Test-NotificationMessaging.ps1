[CmdletBinding()]
param([string]$DockerExecutable = 'docker')

$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRepository 'src/Services/Notification/tests/RutaSegura.Notification.Messaging.Tests/RutaSegura.Notification.Messaging.Tests.csproj'
$taskResults = Join-Path $taskRepository 'artifacts/test-results/notification-messaging'
$taskSuffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$taskPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$taskContainers = @()
$taskPrevious = @{}
foreach ($taskKey in @('NOTIFICATION_TEST_MYSQL', 'NOTIFICATION_TEST_RABBITMQ_PORT', 'NOTIFICATION_TEST_RABBITMQ_USER', 'NOTIFICATION_TEST_RABBITMQ_PASSWORD')) {
    $taskPrevious[$taskKey] = [Environment]::GetEnvironmentVariable($taskKey, 'Process')
}

function Start-TestContainer([string[]]$Arguments) {
    $taskResult = & $DockerExecutable @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo iniciar un contenedor aislado de pruebas.' }
    $taskId = ($taskResult | Select-Object -Last 1).Trim()
    if ($taskId -notmatch '^[0-9a-f]{64}$') { throw 'Identificador de contenedor inesperado.' }
    return $taskId
}

try {
    & $DockerExecutable version --format '{{.Server.Version}}'
    if ($LASTEXITCODE -ne 0) { throw 'Inicia Docker Desktop.' }
    $taskMySql = Start-TestContainer @('run', '--detach', '--rm', '--name', "notification-mysql-test-$taskSuffix",
        '--label', 'rutasegura.purpose=notification-messaging-tests', '--publish', '127.0.0.1::3306', '--tmpfs', '/var/lib/mysql',
        '--env', "MYSQL_ROOT_PASSWORD=$taskPassword", '--env', 'MYSQL_ROOT_HOST=%',
        '--health-cmd', 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -h127.0.0.1 -uroot -e "SELECT 1" >/dev/null 2>&1',
        '--health-interval', '2s', '--health-timeout', '3s', '--health-retries', '60',
        'mysql:8.4', '--character-set-server=utf8mb4', '--collation-server=utf8mb4_bin')
    $taskContainers += $taskMySql
    $taskRabbit = Start-TestContainer @('run', '--detach', '--rm', '--name', "notification-rabbit-test-$taskSuffix",
        '--label', 'rutasegura.purpose=notification-messaging-tests', '--publish', '127.0.0.1::5672',
        '--env', 'RABBITMQ_DEFAULT_USER=notification_test', '--env', "RABBITMQ_DEFAULT_PASS=$taskPassword",
        '--health-cmd', 'rabbitmq-diagnostics -q ping', '--health-interval', '2s', '--health-timeout', '5s', '--health-retries', '60',
        'rabbitmq:4.1-management')
    $taskContainers += $taskRabbit
    foreach ($taskId in $taskContainers) {
        $taskDeadline = [DateTime]::UtcNow.AddSeconds(150)
        do {
            $taskHealth = & $DockerExecutable inspect --format '{{.State.Health.Status}}' $taskId
            if ($LASTEXITCODE -ne 0 -or $taskHealth -eq 'unhealthy') { throw 'Un contenedor de pruebas no esta saludable.' }
            if ($taskHealth -eq 'healthy') { break }
            Start-Sleep -Seconds 2
        } while ([DateTime]::UtcNow -lt $taskDeadline)
        if ($taskHealth -ne 'healthy') { throw 'Tiempo de inicializacion agotado.' }
    }
    $taskBinding = & $DockerExecutable port $taskMySql '3306/tcp'
    if ($LASTEXITCODE -ne 0 -or $taskBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Puerto MySQL inesperado.' }
    $env:NOTIFICATION_TEST_MYSQL = "Server=127.0.0.1;Port=$($Matches[1]);UserID=root;Password=$taskPassword;SslMode=Disabled;Pooling=False;Connection Timeout=10"
    $taskBinding = & $DockerExecutable port $taskRabbit '5672/tcp'
    if ($LASTEXITCODE -ne 0 -or $taskBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Puerto RabbitMQ inesperado.' }
    $env:NOTIFICATION_TEST_RABBITMQ_PORT = $Matches[1]
    $env:NOTIFICATION_TEST_RABBITMQ_USER = 'notification_test'
    $env:NOTIFICATION_TEST_RABBITMQ_PASSWORD = $taskPassword
    & dotnet test $taskProject -c Release -p:NuGetAudit=false --verbosity minimal --logger trx --results-directory $taskResults
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas de mensajeria fallaron; revisa los TRX.' }
}
finally {
    foreach ($taskKey in $taskPrevious.Keys) { [Environment]::SetEnvironmentVariable($taskKey, $taskPrevious[$taskKey], 'Process') }
    foreach ($taskId in $taskContainers) {
        if ($taskId -notmatch '^[0-9a-f]{64}$') { continue }
        & $DockerExecutable stop --time 10 $taskId | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "No se pudo detener el contenedor temporal $taskId." }
    }
    if ($taskContainers.Count -gt 0) { Write-Output 'Contenedores temporales retirados; datos de pruebas efimeros.' }
}
