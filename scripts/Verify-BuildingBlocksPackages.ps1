[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$packageFolder = Join-Path $repoRoot 'artifacts/packages'
$resultsFolder = Join-Path $repoRoot 'artifacts/test-results/package-consumers'
$consumerCache = Join-Path $repoRoot "artifacts/package-consumers/cache/$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $packageFolder, $resultsFolder | Out-Null

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')"
    }
}

Push-Location $repoRoot
try {
    foreach ($component in @('Domain', 'Application', 'Messaging', 'Testing')) {
        $project = "src/BuildingBlocks/RutaSegura.BuildingBlocks.$component/RutaSegura.BuildingBlocks.$component.csproj"
        Invoke-DotNet -Arguments @('pack', $project, '-c', 'Release', '--output', $packageFolder, '--nologo')
    }

    # Consumers restore from a separate local cache and use no BuildingBlocks ProjectReference.
    Invoke-DotNet -Arguments @('restore', 'RutaSegura.slnx', '-p:UseBuildingBlockPackages=true', "-p:RestorePackagesPath=$consumerCache", '--force', '--nologo')
    Invoke-DotNet -Arguments @('build', 'RutaSegura.slnx', '-c', 'Release', '-p:UseBuildingBlockPackages=true',
        "-p:RestorePackagesPath=$consumerCache", '--no-restore', '--nologo')
    Invoke-DotNet -Arguments @('test', 'RutaSegura.slnx', '-c', 'Release', '-p:UseBuildingBlockPackages=true',
        "-p:RestorePackagesPath=$consumerCache", '--no-build', '--no-restore', '--nologo', '--logger', 'trx', '--results-directory', $resultsFolder)

    $consumers = @()
    foreach ($service in @('IAM', 'Administration', 'TripTracking', 'Notification')) {
        $projectName = "RutaSegura.$service.Api"
        $assetsPath = Join-Path $repoRoot "artifacts/package-consumers/obj/$projectName/project.assets.json"
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        $libraries = $assets.libraries
        foreach ($component in @('Domain', 'Application', 'Messaging')) {
            $key = "RutaSegura.BuildingBlocks.$component/1.0.0"
            $library = $libraries.PSObject.Properties[$key].Value
            if ($null -eq $library -or $library.type -ne 'package') {
                throw "$projectName did not consume $key as a package."
            }
            $metadataPath = Join-Path $consumerCache "$($library.path)/.nupkg.metadata"
            $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
            if (-not [string]::Equals([IO.Path]::GetFullPath($metadata.source),
                [IO.Path]::GetFullPath($packageFolder), [StringComparison]::OrdinalIgnoreCase)) {
                throw "$projectName consumed $key from an unexpected feed."
            }
        }
        foreach ($library in $libraries.PSObject.Properties) {
            if ($library.Name -like 'RutaSegura.BuildingBlocks.*' -and $library.Value.type -eq 'project') {
                throw "$projectName retained a BuildingBlocks project reference."
            }
        }
        $consumers += [pscustomobject]@{ Service = $service; Version = '1.0.0'; Source = 'local NuGet packages'; Assets = $assetsPath }
    }
    $testAssetsPath = Join-Path $repoRoot 'artifacts/package-consumers/obj/RutaSegura.Backend.Structure.Tests/project.assets.json'
    $testAssets = Get-Content -LiteralPath $testAssetsPath -Raw | ConvertFrom-Json
    if ($testAssets.libraries.PSObject.Properties['RutaSegura.BuildingBlocks.Testing/1.0.0'].Value.type -ne 'package') {
        throw 'Testing package was not consumed by tests.'
    }
    $consumers | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageFolder 'consumption-evidence.json') -Encoding utf8
    Write-Host 'Four services compiled and tests passed using BuildingBlocks 1.0.0 packages.'
}
finally {
    Pop-Location
}
