$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "..")
try {
    docker compose down
} finally {
    Pop-Location
}
