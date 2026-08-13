#!/usr/bin/env bash
# ============================================================================
# Syxx Server — Automated Deployment
# ============================================================================
# Phase 6 hardening: environment validation, DB migration, app startup.
# Source this file or run directly:  bash scripts/deploy.sh
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_DIR"

echo "[deploy] Starting Syxx server deployment at $(date -u +%Y-%m-%dT%H:%M:%SZ)"

# ---------------------------------------------------------------------------
# 1. Environment validation
# ---------------------------------------------------------------------------
REQUIRED_VARS=(
  STRIPE_SECRET_KEY
  SYXX_DB_PATH
  SYXX_COLLECTOR_PRIVATE_KEY
)
MISSING=0
for var in "${REQUIRED_VARS[@]}"; do
  if [ -z "${!var:-}" ]; then
    echo "[deploy]  FAIL  Missing required env var: $var"
    MISSING=$((MISSING + 1))
  fi
done

if [ "$MISSING" -gt 0 ]; then
  echo "[deploy] ERROR: $MISSING required environment variable(s) not set. Aborting."
  exit 1
fi
echo "[deploy]  PASS  Environment validation — $MISSING missing variables"

# Optional with defaults
export SYXX_HTTP_HOST="${SYXX_HTTP_HOST:-127.0.0.1}"
export SYXX_HTTP_PORT="${SYXX_HTTP_PORT:-8080}"
export SYXX_ENFORCE_WEB="${SYXX_ENFORCE_WEB:-true}"
export SYXX_ENFORCE_DOH="${SYXX_ENFORCE_DOH:-true}"
export SYXX_ENFORCE_NATIVE="${SYXX_ENFORCE_NATIVE:-true}"
export SYXX_METRICS_BEARER_TOKEN="${SYXX_METRICS_BEARER_TOKEN:-}"
export SYXX_LOG_LEVEL="${SYXX_LOG_LEVEL:-info}"
export SYXX_HTTP_REQUEST_TIMEOUT="${SYXX_HTTP_REQUEST_TIMEOUT:-30}"
export SYXX_DOH_UPSTREAM_TIMEOUT="${SYXX_DOH_UPSTREAM_TIMEOUT:-5}"

# ---------------------------------------------------------------------------
# 2. Python environment & dependency check
# ---------------------------------------------------------------------------
if ! command -v python3 &>/dev/null; then
  echo "[deploy] ERROR: python3 not found on PATH. Aborting."
  exit 1
fi

PYTHON="python3"
echo "[deploy] Using $(python3 --version 2>&1) at $(which python3)"

# Ensure pip deps are installed (can be skipped with SKIP_PIP_INSTALL=1)
if [ "${SKIP_PIP_INSTALL:-0}" != "1" ]; then
  if [ -f requirements.txt ]; then
    echo "[deploy] Installing Python dependencies from requirements.txt..."
    python3 -m pip install --quiet --upgrade -r requirements.txt
  fi
fi

# ---------------------------------------------------------------------------
# 3. Database migration
# ---------------------------------------------------------------------------
echo "[deploy] Running database migration..."

# Ensure DB directory exists
DB_DIR="$(dirname "${SYXX_DB_PATH:-./syxx.db}")"
if [ ! -d "$DB_DIR" ]; then
  mkdir -p "$DB_DIR"
  echo "[deploy] Created database directory: $DB_DIR"
fi

python3 -c "
import os, sys
os.environ.setdefault('SYXX_DB_PATH', '${SYXX_DB_PATH:-./syxx.db}')
from syxx_server import db
db.init()
print('[deploy]  PASS  Database initialized / migrated')
" 2>&1

if [ $? -ne 0 ]; then
  echo "[deploy] ERROR: Database migration failed. Aborting."
  exit 1
fi

# ---------------------------------------------------------------------------
# 4. Application startup
# ---------------------------------------------------------------------------
echo "[deploy] Starting Syxx server on ${SYXX_HTTP_HOST}:${SYXX_HTTP_PORT}..."

# Use exec to replace shell so signals reach the Python process
exec python3 -m syxx_server

