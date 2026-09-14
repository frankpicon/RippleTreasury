$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "..")
try {
    docker compose up --build -d
    Write-Host "EventFlow is starting. Open http://localhost:3000" -ForegroundColor Green
    Write-Host "RabbitMQ: http://localhost:15672 | Seq: http://localhost:5341 | Jaeger: http://localhost:16686"
} finally {
    Pop-Location
}
