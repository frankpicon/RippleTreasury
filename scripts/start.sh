#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
docker compose up --build -d
echo "EventFlow is starting. Open http://localhost:3000"
echo "RabbitMQ: http://localhost:15672 | Seq: http://localhost:5341 | Jaeger: http://localhost:16686"
