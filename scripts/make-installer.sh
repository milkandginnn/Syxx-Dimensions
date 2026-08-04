#!/usr/bin/env bash
# Builds dist/syxx-installer.sh: the installer template with the whole Server/
# tree appended as a base64 payload. Run from anywhere in the repo.
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT="$( dirname "$SCRIPT_DIR" )"
OUT="${ROOT}/dist/syxx-installer.sh"

mkdir -p "${ROOT}/dist"
cp "${SCRIPT_DIR}/installer-template.sh" "${OUT}"

tar czf - -C "${ROOT}/Server" \
    --exclude='__pycache__' --exclude='*.pyc' \
    --exclude='*.db' --exclude='*.db-wal' --exclude='*.db-shm' \
    --exclude='.DS_Store' \
    . | base64 >> "${OUT}"

chmod +x "${OUT}"
echo "Built ${OUT} ($(wc -c < "${OUT}" | tr -d ' ') bytes)"
echo "Deploy:  scp ${OUT} root@HOST:  &&  ssh root@HOST 'sudo DOMAIN=syxx.app LETSENCRYPT_EMAIL=you@x bash syxx-installer.sh'"
