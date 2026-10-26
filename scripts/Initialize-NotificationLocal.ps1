[CmdletBinding()]
param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $taskRepository '.local/notification' }
$taskDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $taskDirectory) {
    if (Test-Path -LiteralPath (Join-Path $taskDirectory 'compose.env')) {
        Write-Output "Se conserva la configuracion existente en $taskDirectory (sin rotar claves)."
        return
    }
    if (Get-ChildItem -LiteralPath $taskDirectory -Force | Select-Object -First 1) { throw 'El directorio contiene archivos. No se sobrescribiran claves existentes.' }
}
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
function New-LocalSecret { [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant() }
$taskDatabasePassword = New-LocalSecret
$taskJwt = New-LocalSecret
$taskRabbitPassword = New-LocalSecret
$taskServiceToken = New-LocalSecret
$taskFiles = @{
    'mysql-password' = $taskDatabasePassword
    'mysql-root-password' = (New-LocalSecret)
    'ConnectionStrings__NotificationDatabase' = "Server=notification-mysql;Port=3306;Database=rutasegura_notification;UserID=notification_app;Password=$taskDatabasePassword;SslMode=Disabled;Connection Timeout=5"
    'NotificationPersistence__PushTokenEncryptionKey' = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    'Jwt__Secret' = $taskJwt
    'NotificationRabbitMq__Password' = $taskRabbitPassword
    'service-token' = $taskServiceToken
    'NotificationMonitoring__Token' = (New-LocalSecret)
}
foreach ($taskFile in $taskFiles.GetEnumerator()) {
    [IO.File]::WriteAllText((Join-Path $taskDirectory $taskFile.Key), $taskFile.Value, [Text.UTF8Encoding]::new($false))
}
$taskEnv = @(
    "NOTIFICATION_SECRETS_DIR=$($taskDirectory.Replace('\','/'))"
    "RUTASEGURA_JWT_SECRET=$taskJwt"
    "RUTASEGURA_RABBIT_PASSWORD=$taskRabbitPassword"
    "NOTIFICATION_SERVICE_TOKEN=$taskServiceToken"
) -join "`n"
[IO.File]::WriteAllText((Join-Path $taskDirectory 'compose.env'), $taskEnv + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Secretos locales generados en $taskDirectory. No se muestran sus valores ni se sobrescriben en otra ejecucion."
