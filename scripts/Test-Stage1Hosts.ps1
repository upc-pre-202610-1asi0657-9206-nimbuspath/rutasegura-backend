[CmdletBinding()]
param(
    [switch]$UsePackages
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$evidenceFolder = Join-Path $repoRoot 'artifacts/host-startup'
New-Item -ItemType Directory -Force -Path $evidenceFolder | Out-Null
$dotnetPath = (Get-Command dotnet).Source
$services = @(
    @{ Name = 'IAM'; Folder = 'src/Services/IAM/src/RutaSegura.IAM.Api'; Assembly = 'RutaSegura.IAM.Api' },
    @{ Name = 'Administration'; Folder = 'src/Services/Administration/src/RutaSegura.Administration.Api'; Assembly = 'RutaSegura.Administration.Api' },
    @{ Name = 'TripTracking'; Folder = 'src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api'; Assembly = 'RutaSegura.TripTracking.Api' },
    @{ Name = 'Notification'; Folder = 'src/Services/Notification/src/RutaSegura.Notification.Api'; Assembly = 'RutaSegura.Notification.Api' },
    @{ Name = 'Gateway'; Folder = 'src/ApiGateway'; Assembly = 'RutaSegura.ApiGateway' }
)
$startedProcesses = @()
$results = @()
$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromSeconds(2)
try {
    foreach ($service in $services) {
        $dllRelative = if ($UsePackages) {
            "artifacts/package-consumers/bin/$($service.Assembly)/Release/net10.0/$($service.Assembly).dll"
        } else {
            "$($service.Folder)/bin/Release/net10.0/$($service.Assembly).dll"
        }
        $dll = Join-Path $repoRoot $dllRelative
        if (-not (Test-Path -LiteralPath $dll)) { throw "Build Release first: $dll" }
        $outputPath = Join-Path $evidenceFolder "$($service.Name).stdout.log"
        $errorPath = Join-Path $evidenceFolder "$($service.Name).stderr.log"
        $startOptions = @{
            FilePath = $dotnetPath
            ArgumentList = @("`"$dll`"", '--urls', 'http://127.0.0.1:0')
            WorkingDirectory = (Join-Path $repoRoot $service.Folder)
            PassThru = $true
            RedirectStandardOutput = $outputPath
            RedirectStandardError = $errorPath
        }
        if ($IsWindows -or $env:OS -eq 'Windows_NT') { $startOptions.WindowStyle = 'Hidden' }
        $process = Start-Process @startOptions
        $startedProcesses += $process

        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        $url = $null
        do {
            $process.Refresh()
            if ($process.HasExited) { throw "$($service.Name) exited; see $errorPath" }
            if (Test-Path -LiteralPath $outputPath) {
                $log = Get-Content -LiteralPath $outputPath -Raw
                if ($log -match 'Now listening on: (http://127\.0\.0\.1:\d+)') {
                    $url = $Matches[1]
                    break
                }
            }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)
        if (-not $url) { throw "$($service.Name) did not start within 20 seconds." }

        $response = $httpClient.GetAsync("$url/health/live").GetAwaiter().GetResult()
        try {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ([int]$response.StatusCode -ne 200 -or $body -ne 'Healthy') {
                throw "$($service.Name) liveness failed."
            }
        } finally { $response.Dispose() }
        $results += [pscustomobject]@{ Service = $service.Name; StatusCode = 200; Response = $body; Packages = [bool]$UsePackages }
        Write-Host "$($service.Name): started, /health/live = 200 Healthy"
    }
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceFolder 'results.json') -Encoding utf8
} finally {
    $httpClient.Dispose()
    foreach ($process in $startedProcesses) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}
