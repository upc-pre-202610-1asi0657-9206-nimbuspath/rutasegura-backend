[CmdletBinding()]
param([switch]$SkipBuild, [string]$WebDirectory = '')
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskProject = 'sprint3-verify-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$taskArtifacts = Join-Path $taskRepository 'artifacts/test-results/sprint3/integration'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
@{ project=$taskProject; outcome='setup-in-progress'; startedAtUtc=[DateTimeOffset]::UtcNow } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskArtifacts 'report.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Initialize-NotificationLocal.ps1')
$taskCompose = @('--project-name', $taskProject, '--env-file', (Join-Path $taskRepository '.local/notification/compose.env'), '-f', (Join-Path $taskRepository 'docker-compose.yml'), '-f', (Join-Path $taskRepository 'docker/sprint3/verify.override.yml'))
function Invoke-Compose { & docker compose @taskCompose @args; if ($LASTEXITCODE -ne 0) { throw 'Isolated compose operation failed.' } }
function Service-Url([string]$Service) {
    $taskBinding = Invoke-Compose port $Service 8080
    if ($taskBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected isolated service binding.' }
    "http://127.0.0.1:$($Matches[1])"
}
$taskPrevious = @{}
foreach ($taskName in @('SPRINT3_ISOLATED_PROJECT','SPRINT3_GATEWAY_URL','SPRINT3_MONOLITH_URL','SPRINT3_ARTIFACTS','SPRINT3_WEB_DIRECTORY','SPRINT3_WEB_URL','SPRINT3_COMPOSE_ARGUMENTS')) { $taskPrevious[$taskName] = [Environment]::GetEnvironmentVariable($taskName) }
try {
    Push-Location $taskRepository
    if (!$SkipBuild) { Invoke-Compose build --pull monolith trip-tracking notification gateway }
    if ($WebDirectory) {
        $WebDirectory = (Resolve-Path -LiteralPath $WebDirectory).Path
        if (!(Test-Path -LiteralPath (Join-Path $WebDirectory 'package.json'))) { throw 'Web directory must contain the existing Vite project.' }
        # Compile a copy; npm ci must never replace the developer's active node_modules.
        $taskWebBuild = Join-Path $taskArtifacts "$taskProject-web-source"
        $taskWebBuildFull = [IO.Path]::GetFullPath($taskWebBuild)
        if (!$taskWebBuildFull.StartsWith([IO.Path]::GetFullPath($taskArtifacts) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected build workspace.' }
        New-Item -ItemType Directory -Path $taskWebBuildFull -Force | Out-Null
        foreach ($taskSource in @('src','public','package.json','package-lock.json','vite.config.js','index.html')) {
            Copy-Item -LiteralPath (Join-Path $WebDirectory $taskSource) -Destination $taskWebBuildFull -Recurse
        }
        $taskOldApi = $env:VITE_API_BASE_URL; $taskOldHub = $env:VITE_TRACKING_HUB_URL
        try {
            $env:VITE_API_BASE_URL='/api/v1'; $env:VITE_TRACKING_HUB_URL=''
            Push-Location $taskWebBuildFull
            try { & npm ci; if ($LASTEXITCODE -ne 0) { throw 'Web dependency restore failed.' }; & npm run build; if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' } } finally { Pop-Location }
        } finally { $env:VITE_API_BASE_URL=$taskOldApi; $env:VITE_TRACKING_HUB_URL=$taskOldHub }
        & docker build --build-context "web-dist=$(Join-Path $taskWebBuildFull 'dist')" -f (Join-Path $taskRepository 'docker/web/Dockerfile') -t "$taskProject-web" $taskRepository
        if ($LASTEXITCODE -ne 0) { throw 'Production web image build failed.' }
        $taskWebOverride = Join-Path $taskArtifacts "$taskProject-web.yml"
        @"
services:
  web:
    image: $taskProject-web
    ports: ["127.0.0.1::80"]
    depends_on:
      gateway: { condition: service_healthy }
"@ | Set-Content -LiteralPath $taskWebOverride -Encoding utf8
        $taskCompose += @('-f', $taskWebOverride)
    }
    Invoke-Compose up -d --wait --wait-timeout 240
    $env:SPRINT3_ISOLATED_PROJECT = $taskProject
    $env:SPRINT3_GATEWAY_URL = Service-Url gateway
    $env:SPRINT3_MONOLITH_URL = Service-Url monolith
    $env:SPRINT3_ARTIFACTS = $taskArtifacts
    $env:SPRINT3_WEB_DIRECTORY = if ($WebDirectory) { $taskWebBuildFull } else { '' }
    if ($WebDirectory) {
        $taskWebBinding = Invoke-Compose port web 80
        if ($taskWebBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected isolated web binding.' }
        $env:SPRINT3_WEB_URL = "http://127.0.0.1:$($Matches[1])"
    }
    $env:SPRINT3_COMPOSE_ARGUMENTS = ConvertTo-Json -InputObject $taskCompose -Compress
    & node (Join-Path $PSScriptRoot 'sprint3/e2e.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Sprint 3 integration failed. See sanitized report.json.' }
} catch {
    $taskReportFile=Join-Path $taskArtifacts 'report.json'
    $taskCurrentReport=Get-Content -LiteralPath $taskReportFile -Raw | ConvertFrom-Json
    if ($taskCurrentReport.outcome -eq 'setup-in-progress') {
        @{project=$taskProject;outcome='setup-failed';failure='Build or compose failed before the scenario started.'} | ConvertTo-Json | Set-Content -LiteralPath $taskReportFile -Encoding utf8
    }
    throw
} finally {
    # Only this generated project, its own volumes and network. Never the user's running rutasegura stack.
    if ($taskProject -match '^sprint3-verify-[0-9a-f]{12}$') { & docker compose @taskCompose down --volumes --remove-orphans }
    foreach ($taskName in $taskPrevious.Keys) { [Environment]::SetEnvironmentVariable($taskName, $taskPrevious[$taskName]) }
    if ($WebDirectory -and $taskWebBuildFull -and (Test-Path -LiteralPath $taskWebBuildFull)) {
        $taskCleanupTarget=[IO.Path]::GetFullPath($taskWebBuildFull)
        $taskAllowedRoot=[IO.Path]::GetFullPath($taskArtifacts)+[IO.Path]::DirectorySeparatorChar
        if ($taskCleanupTarget.StartsWith($taskAllowedRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $taskCleanupTarget) -eq "$taskProject-web-source") { Remove-Item -LiteralPath $taskCleanupTarget -Recurse -Force }
    }
    Pop-Location
}
