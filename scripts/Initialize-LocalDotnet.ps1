[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRepository=Split-Path -Parent $PSScriptRoot
$taskVersion=(Get-Content -LiteralPath (Join-Path $taskRepository 'global.json') -Raw | ConvertFrom-Json).sdk.version
$taskTooling=Join-Path $taskRepository '.local/tooling'
$taskInstall=Join-Path $taskTooling 'dotnet'
$taskExecutable=Join-Path $taskInstall 'dotnet.exe'
if (!(Test-Path -LiteralPath $taskExecutable)) {
    if (!$IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'Install the SDK from global.json using the official installer for your operating system.' }
    New-Item -ItemType Directory -Path $taskTooling -Force | Out-Null
    $taskMetadata=Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
    $taskRelease=$taskMetadata.releases | Where-Object { $_.sdk.version -eq $taskVersion } | Select-Object -First 1
    $taskFile=$taskRelease.sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.url.EndsWith('.zip') } | Select-Object -First 1
    if (!$taskFile) { throw 'Pinned SDK not present in official release metadata.' }
    $taskArchive=Join-Path $taskTooling 'sdk.zip'
    Invoke-WebRequest -Uri $taskFile.url -OutFile $taskArchive
    if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA512).Hash -ne $taskFile.hash) { throw 'Official SDK checksum mismatch.' }
    Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskInstall -Force
}
$taskInstalled=& $taskExecutable --version
if ($taskInstalled -ne $taskVersion) { throw 'Local SDK does not match global.json; use the official installer for the pinned SDK.' }
$env:DOTNET_ROOT=$taskInstall
$env:PATH=$taskInstall+[IO.Path]::PathSeparator+$env:PATH
Write-Output "Local SDK $taskInstalled activated for this PowerShell session."
