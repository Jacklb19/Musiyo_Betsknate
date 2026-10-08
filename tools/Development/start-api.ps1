param(
    [string]$ApiRoot,
    [string]$DatabasePath,
    [string]$StorageRoot,
    [string]$WebOrigin = 'http://localhost:5174',
    [int]$Port = 8000
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $ApiRoot) { $ApiRoot = Join-Path (Split-Path $workspaceRoot -Parent) 'Musiyo_Api' }
if (-not $DatabasePath) { $DatabasePath = Join-Path $workspaceRoot 'TestResults/development.db' }
if (-not $StorageRoot) { $StorageRoot = Join-Path $workspaceRoot 'TestResults/private' }
$ApiRoot = (Resolve-Path -LiteralPath $ApiRoot).Path
$DatabasePath = (Resolve-Path -LiteralPath $DatabasePath).Path
$StorageRoot = (Resolve-Path -LiteralPath $StorageRoot).Path
$pythonPath = Join-Path $ApiRoot '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
    throw 'The API virtual environment is missing. Install its development dependencies first.'
}
if ($Port -lt 1 -or $Port -gt 65535) { throw 'Invalid API port.' }
$originUri = [Uri]$WebOrigin
if (-not $originUri.IsAbsoluteUri -or $originUri.Scheme -notin 'http', 'https') {
    throw 'WebOrigin must be an absolute HTTP or HTTPS origin.'
}

$env:MUSIYO_DATABASE_URL = 'sqlite:///' + $DatabasePath.Replace('\', '/')
$env:MUSIYO_STORAGE_ROOT = $StorageRoot
$env:MUSIYO_WEB_ORIGIN = $WebOrigin
Push-Location -LiteralPath $ApiRoot
try {
    Write-Output "Starting the local API on port $Port with the selected database and private storage."
    & $pythonPath -m uvicorn app.main:app --host 127.0.0.1 --port $Port
    if ($LASTEXITCODE -ne 0) { throw "The API exited with code $LASTEXITCODE." }
} finally {
    Pop-Location
}
