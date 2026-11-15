[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CredentialsFile,
    [Parameter(Mandatory=$true)][string]$RegistrationTokenFile,
    [string]$ProjectId = 'rutasegura-f2703'
)
$ErrorActionPreference = 'Stop'
foreach ($taskFile in @($CredentialsFile,$RegistrationTokenFile)) {
    if (!(Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw 'No existe uno de los archivos locales indicados.' }
}
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRepository 'src/Services/Notification/src/RutaSegura.Notification.Api/RutaSegura.Notification.Api.csproj'
$taskPrevious = @{}
foreach ($taskName in @('GOOGLE_APPLICATION_CREDENTIALS','NotificationFcm__ProjectId','NotificationFcmTest__TokenFile')) {
    $taskPrevious[$taskName] = [Environment]::GetEnvironmentVariable($taskName,'Process')
}
try {
    $env:GOOGLE_APPLICATION_CREDENTIALS = [IO.Path]::GetFullPath($CredentialsFile)
    $env:NotificationFcm__ProjectId = $ProjectId
    $env:NotificationFcmTest__TokenFile = [IO.Path]::GetFullPath($RegistrationTokenFile)
    & dotnet run --project $taskProject --configuration Release --no-launch-profile -- --test-fcm
    if ($LASTEXITCODE -ne 0) { throw 'Firebase no acepto el envio de prueba; revisa el codigo estable mostrado.' }
}
finally {
    foreach ($taskName in $taskPrevious.Keys) { [Environment]::SetEnvironmentVariable($taskName,$taskPrevious[$taskName],'Process') }
}
