[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$Port = 5500)
$ErrorActionPreference = 'Stop'
$taskNode = Get-Command node -ErrorAction SilentlyContinue
if (!$taskNode) { throw 'Necesitas Node.js para servir la pagina local. No requiere instalar paquetes npm.' }
$taskServer = Join-Path $PSScriptRoot 'notification-fcm-web/serve.mjs'
& $taskNode.Source $taskServer --port $Port
if ($LASTEXITCODE -ne 0) { throw 'No se pudo mantener el servidor de pruebas FCM.' }
