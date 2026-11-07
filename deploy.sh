#!/bin/bash
set -euo pipefail

docker compose down --remove-orphans
docker compose build
docker compose up -d

echo "Deploy complete"
