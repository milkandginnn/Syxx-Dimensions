#!/bin/bash
set -euo pipefail

# ==============================================================================
# Syxx Server deployment.
#
# Ships the full Server/ tree, installs a systemd unit, and (optionally) puts an
# nginx + Let's Encrypt reverse proxy in front so https://syxx.app/... reaches
# the plaintext daemon on :8080.
#
# Password prompts: this script opens ONE shared SSH connection (ControlMaster)
# and reuses it for every step, so you are asked for the server password at most
# once per run. For zero prompts, install a key first:  ssh-copy-id root@IP
#
# Config via env before running, e.g.:
#   SERVER_IP=1.2.3.4 DOMAIN=syxx.app LETSENCRYPT_EMAIL=you@example.com ./deploy.sh
# ==============================================================================

# CI-020: no hardcoded default — require SERVER_IP to be explicitly set via environment
# to prevent accidental deployments to stale/wrong hosts.
# Dedicated server as of 2026-07-16 (was the IONOS VPS 74.208.237.162).
if [ -z "${SERVER_IP:-}" ]; then
  echo "❌ SERVER_IP is not set. Set it via environment: SERVER_IP=<ip> ./deploy.sh" >&2
  exit 1
fi
SERVER_IP="${SERVER_IP}"
REMOTE_USER="${REMOTE_USER:-root}"
REMOTE_DIR="${REMOTE_DIR:-/opt/SyxxServer}"
DOMAIN="${DOMAIN:-syxx.app}"
LETSENCRYPT_EMAIL="${LETSENCRYPT_EMAIL:-jjwarne@icloud.com}"
SETUP_TLS="${SETUP_TLS:-1}"

# F079: Rollback configuration.
# SYXX_BACKUP_DIR: where pre-deploy tarballs are stored (default: alongside REMOTE_DIR).
# SYXX_BACKUP_KEEP: number of recent backups to retain (older ones are pruned).
SYXX_BACKUP_DIR="${SYXX_BACKUP_DIR:-${REMOTE_DIR}.backups}"
SYXX_BACKUP_KEEP="${SYXX_BACKUP_KEEP:-3}"

# Secrets / entitlement config pushed into the systemd unit environment.
SYXX_SESSION_SECRET="${SYXX_SESSION_SECRET:-}"
SYXX_COLLECTOR_PRIVATE_KEY="${SYXX_COLLECTOR_PRIVATE_KEY:-}"
SYXX_KEYGEN_ACCOUNT_ID="${SYXX_KEYGEN_ACCOUNT_ID:-4b50cc83-1664-4626-99b1-fbd4e76e35c3}"
SYXX_LICENSE_KEYS="${SYXX_LICENSE_KEYS:-}"
SYXX_ENFORCE_NATIVE="${SYXX_ENFORCE_NATIVE:-true}"
# Outbound email (optional). Leave SYXX_SMTP_HOST empty to disable all mail;
# set SYXX_BETA_DIGEST_TO empty to disable only the weekly signup digest.
SYXX_SMTP_HOST="${SYXX_SMTP_HOST:-}"
SYXX_SMTP_PORT="${SYXX_SMTP_PORT:-587}"
# STARTTLS=true → STARTTLS on 587; false → implicit SSL/TLS on 465 (e.g. IONOS).
SYXX_SMTP_STARTTLS="${SYXX_SMTP_STARTTLS:-true}"
SYXX_SMTP_USER="${SYXX_SMTP_USER:-}"
SYXX_SMTP_PASSWORD="${SYXX_SMTP_PASSWORD:-}"
SYXX_SMTP_TIMEOUT="${SYXX_SMTP_TIMEOUT:-15}"
SYXX_SMTP_INSECURE_CERTS="${SYXX_SMTP_INSECURE_CERTS:-false}"
SYXX_MAIL_FROM="${SYXX_MAIL_FROM:-Syxx <noreply@syxx.app>}"
SYXX_BETA_DIGEST_TO="${SYXX_BETA_DIGEST_TO:-jjwarne@icloud.com}"

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( dirname "$SCRIPT_DIR" )"

echo "=================================================="
echo "          Syxx Server Deployment Script           "
echo "=================================================="
echo "Target : ${REMOTE_USER}@${SERVER_IP}"
echo "Domain : ${DOMAIN}"
echo "Dir    : ${REMOTE_DIR}"
echo "TLS    : ${SETUP_TLS}"
echo "Backup : ${SYXX_BACKUP_DIR} (keep ${SYXX_BACKUP_KEEP})"
echo "--------------------------------------------------"

# ---------------------------------------------------------------------------
# One shared, authenticated SSH connection for the whole run. Every ssh/rsync
# below rides over this socket, so the password is entered at most once.
# ---------------------------------------------------------------------------
SSH_CTL="$(mktemp -u "${TMPDIR:-/tmp}/syxx-ssh-XXXXXX")"
SSH_OPTS=(-o ControlMaster=auto -o "ControlPath=${SSH_CTL}" -o ControlPersist=600 -o ConnectTimeout=15)

cleanup() {
  ssh "${SSH_OPTS[@]}" -O exit "${REMOTE_USER}@${SERVER_IP}" 2>/dev/null || true
  rm -f "${SSH_CTL}" 2>/dev/null || true
}
trap cleanup EXIT

remote() { ssh "${SSH_OPTS[@]}" "${REMOTE_USER}@${SERVER_IP}" "$@"; }

echo "Opening SSH connection (you may be asked for the password once)…"
remote true

# 1. Ship the WHOLE Server/ tree — the package AND the website (index.html,
#    favicon, README). server.py serves index.html from this directory, so the
#    static files must land alongside it or the site has nothing to serve.
#
#    Uses tar-over-ssh, not rsync: rsync must be installed on BOTH ends, and this
#    minimal box (no apt-get) very likely has no rsync — which is why only
#    server.py appeared to make it. tar is universal and rides the shared socket.
# Guard: never deploy an empty site. The landing page and its favicon/image assets
# must exist locally before we ship, or the box comes up serving nothing.
if [ ! -f "${PROJECT_ROOT}/Server/index.html" ]; then
  echo "❌ ${PROJECT_ROOT}/Server/index.html not found — refusing to deploy a site with no landing page." >&2
  exit 1
fi
LOCAL_IMG_COUNT="$(find "${PROJECT_ROOT}/Server" -type f \
    \( -iname '*.svg' -o -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.gif' -o -iname '*.webp' -o -iname '*.ico' \) \
    | wc -l | tr -d ' ')"
echo "Local website assets: index.html + ${LOCAL_IMG_COUNT} image file(s)."

# F079: Create backup before deploy for rollback capability.
# Backups live in SYXX_BACKUP_DIR (configurable, defaults to ${REMOTE_DIR}.backups).
# Only the SYXX_BACKUP_KEEP most recent backups are retained.
BACKUP_TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_NAME="syxx-deploy-${BACKUP_TIMESTAMP}.tar.gz"
BACKUP_PATH="${SYXX_BACKUP_DIR}/${BACKUP_NAME}"
echo "📦 F079: Creating pre-deploy backup…"
remote "bash -s" <<BACKUP_EOF
set -eu
mkdir -p "${SYXX_BACKUP_DIR}"
if [ -d "${REMOTE_DIR}" ] && [ "\$(ls -A "${REMOTE_DIR}" 2>/dev/null | head -1)" ]; then
  tar czf "${BACKUP_PATH}" -C "${REMOTE_DIR}" .
  echo "✅ Backup created: ${BACKUP_PATH} (\$(du -h "${BACKUP_PATH}" | cut -f1))"
  # Rotate: keep only the ${SYXX_BACKUP_KEEP} most recent backups.
  cd "${SYXX_BACKUP_DIR}"
  ls -1t syxx-deploy-*.tar.gz 2>/dev/null | tail -n +$(( SYXX_BACKUP_KEEP + 1 )) | while IFS= read -r old; do
    rm -f -- "\$old"
    echo "🗑️  Pruned old backup: \$old"
  done
  REMAINING=\$(ls -1 syxx-deploy-*.tar.gz 2>/dev/null | wc -l | tr -d ' ')
  echo "   Backups on disk: \${REMAINING} (keeping last ${SYXX_BACKUP_KEEP})"
else
  echo "ℹ️  No existing deployment to back up (first deploy)."
fi
BACKUP_EOF

echo "Uploading server files (full tree: package + website)…"
remote "mkdir -p ${REMOTE_DIR}"
# Ship EVERYTHING in Server/ (so any image dropped in here rides along); the excludes
# below are caches/databases/OS cruft only — no web or image asset matches them.
# --no-mac-metadata: macOS bsdtar otherwise embeds Apple xattrs (provenance/
# quarantine/macl) that the server's GNU tar can't read and warns "Ignoring …"
# on every file. Harmless, but noisy — strip them so the transfer is quiet.
# venv/.venv: the LOCAL one is a macOS virtualenv (47 MB of Mach-O binaries and absolute
# /Users/... paths). Shipping it to a Linux box is pure waste at best; at worst an operator
# sources it and gets an interpreter that cannot run. The server builds its own — see below.
tar --no-mac-metadata -czf - -C "${PROJECT_ROOT}/Server" \
    --exclude='__pycache__' --exclude='*.pyc' \
    --exclude='*.db' --exclude='*.db-wal' --exclude='*.db-shm' \
    --exclude='.DS_Store' \
    --exclude='venv' --exclude='.venv' --exclude='*.egg-info' \
    . | remote "tar xzf - -C ${REMOTE_DIR}"

echo "Verifying the website landed at ${REMOTE_DIR}:"
remote "cd ${REMOTE_DIR} && ls -1 && \
  printf '  index.html       : %s\n' \"\$(test -f index.html && echo present || echo MISSING)\" && \
  printf '  syxx-favicon.svg : %s\n' \"\$(test -f syxx-favicon.svg && echo present || echo MISSING)\" && \
  printf '  image assets     : %s file(s)\n' \"\$(find . -type f \\( -iname '*.svg' -o -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.gif' -o -iname '*.webp' -o -iname '*.ico' \\) | wc -l | tr -d ' ')\""
if ! remote "test -f ${REMOTE_DIR}/index.html"; then
  echo "❌ index.html did not land on the server — deploy incomplete." >&2
  exit 1
fi
echo "✅ Upload complete (index.html + image assets verified)."

# 1b. Install the single third-party runtime dependency (`cryptography`, for Ed25519 signing).
#
# THIS STEP FAILED SILENTLY ON THE 2026-07-16 DEPLOY AND IT MATTERS:
#   1. `pip3 install ... 2>/dev/null` suppressed the REASON it failed. Ubuntu 24.04 marks the
#      system Python externally-managed (PEP 668), so a bare `pip3 install` is refused with a
#      clear explanation — which /dev/null ate.
#   2. It then fell through to bare `pip`, which does not exist on this box at all, so the only
#      thing the operator saw was "pip: command not found" — an error about the FALLBACK, not
#      about the real failure.
#   3. Without `cryptography`, leak.py raises at import of the signing path: /api/pubkey 500s on
#      every call and every leak attestation is unverifiable. That is precisely the two-day
#      silent outage this deploy was supposed to end.
# So: never suppress stderr here, try the real strategies in order, and VERIFY by import rather
# than trusting an installer's exit code (presence is not function).
echo "Installing Python runtime dependencies…"
# A120: propagate dependency install failure — do NOT mask exit 1 with || echo.
remote "bash -s" <<'PIPEOF'
set -u
REQ=/opt/SyxxServer/requirements.txt
if python3 -c 'import cryptography' 2>/dev/null; then
  echo "✅ cryptography already present."
else
  # In order: normal pip; PEP668 override (this box has no other Python consumer); distro package.
  python3 -m pip install -r "$REQ" \
    || python3 -m pip install --break-system-packages -r "$REQ" \
    || (command -v apt-get >/dev/null 2>&1 && apt-get install -y python3-cryptography) \
    || echo "⚠️  every install strategy failed."
fi
# The bar is that the interpreter the DAEMON uses can import it — not that an installer exited 0.
if python3 -c 'import cryptography; print("✅ cryptography " + cryptography.__version__ + " importable by python3.")'; then :; else
  echo "❌ cryptography is NOT importable. /api/pubkey WILL 500 and every leak attestation is unverifiable."
  echo "   Fix on the box, then: systemctl restart syxx-server"
  exit 1
fi
PIPEOF

# 2. Generate a session secret if the operator did not supply one.
# E071: defer the "generated" message to the env-file creation branch — on a redeploy the
# env file already exists and is preserved, so the freshly generated secret is discarded.
# The message must only print on the branch that actually writes it.
if [ -z "${SYXX_SESSION_SECRET}" ]; then
  SYXX_SESSION_SECRET="$(openssl rand -hex 32)"
fi

# Portable package installer, sourced into every remote shell. Works across
# apt/dnf/yum/apk/zypper/pacman — the box at ${SERVER_IP} is not Debian, which is
# why the old hard-coded 'apt-get' failed with "command not found".
read -r -d '' PKG_HELPER <<'HELPER' || true
pkg_install() {
  if command -v apt-get >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive; apt-get update -y && apt-get install -y "$@"
  elif command -v dnf >/dev/null 2>&1; then dnf install -y "$@"
  elif command -v yum >/dev/null 2>&1; then yum install -y "$@"
  elif command -v apk >/dev/null 2>&1; then apk add --no-cache "$@"
  elif command -v zypper >/dev/null 2>&1; then zypper --non-interactive install "$@"
  elif command -v pacman >/dev/null 2>&1; then pacman -Sy --noconfirm "$@"
  else echo "⚠️  No supported package manager found; please install manually: $*"; return 1; fi
}
HELPER

# 3. Install Python (if needed) + systemd unit.
echo "Configuring systemd service…"
remote "bash -s" <<EOF
set -e
${PKG_HELPER}
command -v python3 >/dev/null 2>&1 || pkg_install python3

# Secrets live in a root-only env file the unit reads — created ONCE and never overwritten by a
# redeploy, so you fill in the Stripe/Keygen values a single time and they persist across restarts,
# reboots, and future deploys. (Shell 'export's do NOT persist — this file is why.)
# A110: dedicated unprivileged service account. The network-facing DNS/HTTP parsers
# must not run as root; an RCE in a hostile-packet parser then lands unprivileged.
# E059: never silently fall back to root. The network-facing DNS packet parser
# must not run as root; a useradd failure that degrades to root and still reports
# success is the deploy-script analogue of a fabricated result.
if ! id -u syxx >/dev/null 2>&1; then
  if command -v useradd >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin syxx
  elif command -v adduser >/dev/null 2>&1; then
    adduser -S -D -H -s /sbin/nologin syxx 2>/dev/null \
      || adduser --system --no-create-home --disabled-login syxx
  else
    echo "❌ Neither useradd nor adduser found — cannot create the 'syxx' service user." >&2
    exit 1
  fi
fi
if id -u syxx >/dev/null 2>&1; then
  SVC_USER=syxx
else
  echo "❌ Failed to create the 'syxx' service user. The network-facing DNS parser must not run as root." >&2
  echo "   Create an unprivileged user manually, then re-run." >&2
  exit 1
fi

mkdir -p /etc/syxx
if [ ! -f /etc/syxx/syxx.env ]; then
  # A109: ALL secrets live ONLY in this root-only 0600 file (never in inline
  # Environment= directives, which leak via 'systemctl show' and the readable unit).
  # Created once (umask 077 closes the pre-chmod window); a redeploy never overwrites
  # it, so operator-filled Stripe/Keygen values persist. Secrets known at deploy time
  # (session, collector key, SMTP creds, offline license keys) are seeded here.
  ( umask 077
    cat > /etc/syxx/syxx.env <<'ENVF'
# Syxx server secrets. One NAME=value per line. No 'export', no spaces around '='.
# Fill in the blanks, then:  systemctl restart syxx-server
SYXX_SESSION_SECRET=${SYXX_SESSION_SECRET}
SYXX_COLLECTOR_PRIVATE_KEY=${SYXX_COLLECTOR_PRIVATE_KEY}
SYXX_LICENSE_KEYS=${SYXX_LICENSE_KEYS}
SYXX_SMTP_USER=${SYXX_SMTP_USER}
SYXX_SMTP_PASSWORD=${SYXX_SMTP_PASSWORD}
STRIPE_SECRET_KEY=
STRIPE_WEBHOOK_SECRET=
#
# --- Keygen ------------------------------------------------------------------
# SYXX_KEYGEN_PUBLIC_KEY is REQUIRED IN PRODUCTION. Licensing fails CLOSED (D040/A021):
# with this blank, EVERY live key validation is denied (402) — by design, because without
# the public key a forged Keygen response cannot be told from a genuine one. This is the
# single most likely reason a fresh deploy "rejects all valid keys".
# Value: your Keygen account's Ed25519 verify key, hex-encoded (Keygen Dashboard ->
# Settings -> Public Key). NOT the admin token.
SYXX_KEYGEN_PUBLIC_KEY=
# Bearer token that MINTS licenses on a paid webhook. Despite the variable name, this must be a
# KEYGEN **PRODUCT TOKEN** (Dashboard -> Products -> Syxx -> Tokens), value looks like prod-...v3.
#
# NOT an admin token, for two reasons straight from Keygen's docs:
#   1. "Admin tokens SHOULD NOT be utilized for an API integration. These tokens are only meant to
#      be used by the Dashboard, not for API integrations."
#   2. Admin tokens are AUTO-REVOKED when the admin's password, 2FA, or permissions change. Change
#      your password in six months and minting silently stops: the customer pays, gets no key, and
#      the webhook still 200s so Stripe never retries. A product token does not expire.
# All twelve policies live under the single "Syxx" product, so ONE product token covers them all.
# The name says ADMIN because it was written from a wrong assumption (2026-07-16); renaming it is a
# code change across config/preflight/tests and is tracked separately. The VALUE must be a product token.
SYXX_KEYGEN_ADMIN_TOKEN=
# One policy id per SKU. A BLANK policy is not fatal: that product's sale is recorded
# pending rather than dropped, so a missing id delays a key — it never loses a payment.
# A policy id here is INERT unless config.STRIPE_SKUS names it: the webhook resolves the
# policy BY SKU, never by variable name. Adding a variable alone does nothing — the TRIAL
# vars on the 2026-07 production host were dead for exactly this reason, sitting in the env
# file referenced by no code. tests/test_payments.py pins this list against config.
SYXX_KEYGEN_POLICY_MAC_MONTHLY=
SYXX_KEYGEN_POLICY_MAC_YEARLY=
SYXX_KEYGEN_POLICY_WIN_MONTHLY=
SYXX_KEYGEN_POLICY_WIN_YEARLY=
SYXX_KEYGEN_POLICY_DEB_MONTHLY=
SYXX_KEYGEN_POLICY_DEB_YEARLY=
# Trials are NOT sold through Stripe — no payment gates them, so machine identity is the ONLY
# limit on one-per-customer. A host that cannot be node-locked (a VM, or any Linux box without
# the DMI capture helper) can mint a fresh trial per reinstall.
SYXX_KEYGEN_POLICY_MAC_TRIAL=
SYXX_KEYGEN_POLICY_WIN_TRIAL=
SYXX_KEYGEN_POLICY_DEB_TRIAL=
# Perpetual licenses are NOT listed on Stripe: selling one promises this server runs forever.
# The policies exist so a perpetual key can be minted BY HAND (support, comps, early backers).
SYXX_KEYGEN_POLICY_MAC_PERM=
SYXX_KEYGEN_POLICY_WIN_PERM=
SYXX_KEYGEN_POLICY_DEB_PERM=
#
# --- Stripe catalog ----------------------------------------------------------
# The live product ids are compiled in as defaults; these override them (test mode).
# A platform's monthly and yearly ids are THE SAME PRODUCT -- two prices on one product.
SYXX_STRIPE_PRODID_MAC_MONTHLY=
SYXX_STRIPE_PRODID_MAC_YEARLY=
SYXX_STRIPE_PRODID_WIN_MONTHLY=
SYXX_STRIPE_PRODID_WIN_YEARLY=
SYXX_STRIPE_PRODID_DEB_MONTHLY=
SYXX_STRIPE_PRODID_DEB_YEARLY=
# REQUIRED TO SELL. The price id is what says WHICH TIER to charge -- the product id cannot,
# because both tiers live on one product. Blank => that buy button 503s (checkout refuses
# rather than falling back to the product's default price and charging the wrong tier).
SYXX_STRIPE_PRICEID_MAC_MONTHLY=
SYXX_STRIPE_PRICEID_MAC_YEARLY=
SYXX_STRIPE_PRICEID_WIN_MONTHLY=
SYXX_STRIPE_PRICEID_WIN_YEARLY=
SYXX_STRIPE_PRICEID_DEB_MONTHLY=
SYXX_STRIPE_PRICEID_DEB_YEARLY=
#
# SYXX_DEV_MODE is deliberately ABSENT. Setting it to 1 disables fail-closed licensing
# and would grant entitlement on unsigned responses. It is a dev/test escape ONLY and
# must never appear on a production host.
#
# --- DNS Leak Collector ------------------------------------------------------
# Sink IPs returned by the authoritative DNS for in-zone queries. The defaults
# (127.0.0.1 / ::1) trigger DNS-rebind guards in home routers and PNA blocks
# in browsers. Override on production to RFC 5737 TEST-NET-2 addresses, which
# are non-routable on the public internet but won't trigger rebind/PNA filters.
SYXX_LEAK_SINK_A=
SYXX_LEAK_SINK_AAAA=
ENVF
  )
  chmod 600 /etc/syxx/syxx.env
  echo "Created /etc/syxx/syxx.env (chmod 600). Deploy-time secrets were seeded here; fill in the Stripe/Keygen values, then restart."
  # E071: print the session-secret message only on the branch that actually writes it.
  echo "ℹ️  Generated a random SYXX_SESSION_SECRET for this deploy."
else
  echo "Kept existing /etc/syxx/syxx.env (your secrets are preserved across this deploy)."
fi

# The daemon runs as \${SVC_USER}; it must own its working dir + SQLite DB, since
# ProtectSystem=strict makes everything outside ReadWritePaths read-only. Idempotent.
chown -R "\${SVC_USER}" "${REMOTE_DIR}" 2>/dev/null || true

cat > /etc/systemd/system/syxx-server.service <<UNIT
[Unit]
Description=Syxx DNS + Web API Daemon
After=network.target

[Service]
Type=simple
User=\${SVC_USER}
# A121: the service user must be in syxx-cert to read the Let's Encrypt private key
# for app-terminated DoT/DoQ. Without this, the :853 listener fails to load TLS.
SupplementaryGroups=syxx-cert
WorkingDirectory=${REMOTE_DIR}
ExecStart=/usr/bin/env python3 ${REMOTE_DIR}/server.py
Restart=always
RestartSec=5
# Non-secret operational settings only. Ports, DB path, the (public) Keygen account
# id, native-enforcement toggle, and non-secret SMTP transport settings.
Environment=SYXX_HTTP_PORT=8080
Environment=SYXX_DNS_PORT=53
Environment=SYXX_DB_PATH=${REMOTE_DIR}/syxx-server.db
Environment=SYXX_KEYGEN_ACCOUNT_ID=${SYXX_KEYGEN_ACCOUNT_ID}
Environment=SYXX_ENFORCE_NATIVE=${SYXX_ENFORCE_NATIVE}
Environment=SYXX_SMTP_HOST=${SYXX_SMTP_HOST}
Environment=SYXX_SMTP_PORT=${SYXX_SMTP_PORT}
Environment=SYXX_SMTP_STARTTLS=${SYXX_SMTP_STARTTLS}
Environment=SYXX_SMTP_TIMEOUT=${SYXX_SMTP_TIMEOUT}
Environment=SYXX_SMTP_INSECURE_CERTS=${SYXX_SMTP_INSECURE_CERTS}
Environment="SYXX_MAIL_FROM=${SYXX_MAIL_FROM}"
Environment=SYXX_BETA_DIGEST_TO=${SYXX_BETA_DIGEST_TO}
# D-2 DoT: app-terminated TLS on :853 uses the SAME Let's Encrypt cert as nginx. Off by default
# (SYXX_ENABLE_DOT lives in the env file); these just point the listener at the cert if enabled.
Environment=SYXX_DOT_TLS_CERT=/etc/letsencrypt/live/${DOMAIN}/fullchain.pem
Environment=SYXX_DOT_TLS_KEY=/etc/letsencrypt/live/${DOMAIN}/privkey.pem
# A109: EVERY secret (session secret, collector Ed25519 key, offline license keys,
# SMTP user/password, Stripe + Keygen tokens) comes ONLY from this root-only 0600
# file - NEVER an inline Environment= line, which systemd exposes via 'systemctl
# show' and the readable unit. The leading '-' keeps a missing file non-fatal.
EnvironmentFile=-/etc/syxx/syxx.env

# A110 hardening: run unprivileged, keeping only CAP_NET_BIND_SERVICE so :53 (and
# :8080) still bind. ProtectSystem=strict makes the whole filesystem read-only except
# the working dir (which holds the SQLite DB). An RCE in a hostile-packet parser then
# lands as an unprivileged, sandboxed user rather than root.
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=${REMOTE_DIR}
ProtectHome=true
PrivateDevices=true
PrivateTmp=true
# E065: additional hardening for a daemon parsing hostile DNS packets on :53.
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
SystemCallFilter=@system-service
SystemCallArchitectures=native
MemoryDenyWriteExecute=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectKernelLogs=true
ProtectControlGroups=true
ProtectClock=true
ProtectProc=invisible
RestrictNamespaces=true
RestrictSUIDSGID=true
RestrictRealtime=true
LockPersonality=true
PrivateUsers=true
UMask=0077
# E065: prevent infinite crash-loop on config error (SD-56).
StartLimitIntervalSec=300
StartLimitBurst=5

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable syxx-server.service
systemctl restart syxx-server.service

# ---------------------------------------------------------------------------
# F079: rollback function — restore from backup and restart the service.
# Called when post-deploy health checks detect a broken deployment.
# ---------------------------------------------------------------------------
ROLLBACK_BACKUP="${BACKUP_PATH}"
rollback() {
  local reason="\$1"
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo "🔄 F079 ROLLBACK: \${reason}"
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  if [ ! -f "\${ROLLBACK_BACKUP}" ]; then
    echo "❌ No backup found at \${ROLLBACK_BACKUP} — cannot roll back."
    echo "   Manual intervention required: ssh ${REMOTE_USER}@${SERVER_IP}"
    return 1
  fi
  echo "  1/4 Stopping syxx-server.service…"
  systemctl stop syxx-server.service 2>/dev/null || true
  echo "  2/4 Clearing deployment directory ${REMOTE_DIR}…"
  # Use find to remove ALL files including dotfiles (rm -rf * misses .hidden).
  find "${REMOTE_DIR}" -mindepth 1 -delete 2>/dev/null || true
  echo "  3/4 Restoring from backup \${ROLLBACK_BACKUP}…"
  tar xzf "\${ROLLBACK_BACKUP}" -C "${REMOTE_DIR}"
  chown -R "\${SVC_USER:-syxx}" "${REMOTE_DIR}" 2>/dev/null || true
  echo "  4/4 Restarting syxx-server.service…"
  systemctl restart syxx-server.service
  sleep 3
  if systemctl is-active --quiet syxx-server.service \
     && curl -fsS http://127.0.0.1:8080/api/pubkey >/dev/null 2>&1; then
    echo "✅ F079 Rollback SUCCEEDED — previous version restored and serving."
    return 0
  else
    echo "❌ F079 Rollback FAILED — service did not recover."
    echo "   Manual intervention required: ssh ${REMOTE_USER}@${SERVER_IP}"
    echo "   Backup used: \${ROLLBACK_BACKUP}"
    journalctl -u syxx-server -n 30 --no-pager
    return 1
  fi
}

# E063: Type=simple returns 0 as soon as systemd forks the process — before Python
# has imported a single module. Verify the daemon is actually alive and serving.
sleep 3
if systemctl is-active --quiet syxx-server.service; then
  echo "✅ systemd unit is active."
else
  echo "❌ syxx-server.service is not active after restart." >&2
  journalctl -u syxx-server -n 50 --no-pager
  rollback "systemd unit failed to start" || true
  exit 1
fi
if curl -fsS http://127.0.0.1:8080/api/pubkey >/dev/null 2>&1; then
  echo "✅ Daemon running — /api/pubkey responds."
else
  echo "❌ Daemon is active but /api/pubkey does not respond." >&2
  journalctl -u syxx-server -n 50 --no-pager
  rollback "/api/pubkey not responding (daemon alive but broken)" || true
  exit 1
fi
EOF

# 4. nginx + certbot reverse proxy so https://${DOMAIN} -> 127.0.0.1:8080.
#    Non-fatal: the daemon is already up; a proxy/TLS hiccup must not abort.
if [ "${SETUP_TLS}" = "1" ]; then
  echo "Configuring nginx + TLS for ${DOMAIN}…"
  if remote "DOMAIN='${DOMAIN}' EMAIL='${LETSENCRYPT_EMAIL}' bash -s" <<EOF
set -e
${PKG_HELPER}
command -v nginx >/dev/null 2>&1 || pkg_install nginx
command -v certbot >/dev/null 2>&1 || pkg_install certbot python3-certbot-nginx || true

# conf.d is the portable include dir (works on Debian, RHEL, Alpine); avoid the
# Debian-only sites-available/sites-enabled layout.
mkdir -p /etc/nginx/conf.d
# One server block for ALL the client-facing hosts: app.py routes the MOTD purely on the
# HOST HEADER (app.py:487-491 — macmotd/winmotd serve whats-new-mac/win at "/"), so every name
# proxies to the same backend and the app decides. The proxy_set_header Host directive below is
# therefore load-bearing: strip it and macmotd gets the marketing site instead of the MOTD.
#
# The MOTD hosts were MISSING here until 2026-07-16, which is why the app silently showed no
# What's-New: nginx had no server_name for macmotd.\${DOMAIN}, so it fell through to the default
# vhost and presented the \${DOMAIN} certificate — a name mismatch the client rejects before any
# HTTP happens. The symptom read as "the server is down"; the daemon was healthy the whole time.
cat > /etc/nginx/conf.d/syxx.conf <<NGINX
server {
    listen 80;
    server_name \${DOMAIN} www.\${DOMAIN} macmotd.\${DOMAIN} winmotd.\${DOMAIN} debmotd.\${DOMAIN} leaktest.\${DOMAIN};
    client_max_body_size 1m;
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host \\\$host;
        proxy_set_header X-Real-IP \\\$remote_addr;
        proxy_set_header X-Forwarded-Proto \\\$scheme;
        proxy_read_timeout 30s;
    }
}
NGINX
rm -f /etc/nginx/sites-enabled/default 2>/dev/null || true
nginx -t && (systemctl reload nginx || systemctl restart nginx)

if [ -n "\${EMAIL}" ]; then
  # --expand so re-running adds the MOTD names to the EXISTING cert rather than erroring. Every
  # name must already resolve to this box (HTTP-01 validation), which the A records do.
  # E069: certbot failure must be fatal — a site left on plaintext HTTP while the deploy
  # prints "✅ Reverse proxy configured." is an honesty violation. Every name must resolve
  # here before certbot can validate via HTTP-01.
  if ! certbot --nginx --expand -d "\${DOMAIN}" -d "www.\${DOMAIN}" \
    -d "macmotd.\${DOMAIN}" -d "winmotd.\${DOMAIN}" -d "debmotd.\${DOMAIN}" \
    -d "leaktest.\${DOMAIN}" \
    --non-interactive --agree-tos -m "\${EMAIL}" --redirect; then
    echo "❌ certbot failed. The site is on plaintext HTTP — licensing and leak-attestation APIs are exposed."
    echo "   Every name must resolve to this host first. Fix DNS and re-run with SETUP_TLS=1."
    exit 1
  fi

  # A146: HSTS must be added by the terminating proxy (nginx) since the Python process
  # does not see TLS in the shipped topology. certbot --nginx creates the HTTPS server
  # block but does not add HSTS. Inject it here so the header is present from first deploy.
  if [ -f /etc/nginx/conf.d/syxx.conf ]; then
    # Add HSTS to the HTTPS server block (the one listening on :443 with ssl).
    # Only add if not already present (idempotent).
    if ! grep -q "Strict-Transport-Security" /etc/nginx/conf.d/syxx.conf 2>/dev/null; then
      sed -i '/listen 443/,/^}/ {
        /location \/ {/i\        add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
      }' /etc/nginx/conf.d/syxx.conf
      nginx -t && systemctl reload nginx && echo "✅ HSTS header added to nginx config."
    else
      echo "✅ HSTS header already present."
    fi
  fi

  # ---- Verify RENEWAL, not just issuance (SD-57) --------------------------------------
  # Issuing a cert is a 90-day loan, not a solution. This step used to end at the certbot
  # call above and simply hope the distro shipped certbot.timer enabled. When that hope is
  # wrong the failure is SILENT FOR 90 DAYS and then presents as "the server is down" —
  # every client hard-fails on TLS before any HTTP happens, which is exactly how the
  # 2026-07-16 MOTD outage read. Verified on the 198.71.60.193 build: the timer was active
  # and --dry-run passed, but that was the distro's default, not this script's doing.
  #
  # The SAN check exists because a cert that covers only \${DOMAIN} looks completely healthy
  # and still breaks macmotd/winmotd with a name mismatch — the original outage.
  echo "Verifying certificate coverage and renewal…"
  CERT=/etc/letsencrypt/live/\${DOMAIN}/fullchain.pem
  if [ -r "\$CERT" ]; then
    SAN="\$(openssl x509 -in "\$CERT" -noout -ext subjectAltName 2>/dev/null || true)"
    for n in "\${DOMAIN}" "www.\${DOMAIN}" "macmotd.\${DOMAIN}" "winmotd.\${DOMAIN}" "debmotd.\${DOMAIN}" "leaktest.\${DOMAIN}"; do
      case "\$SAN" in
        *"DNS:\$n"*) : ;;
        *) echo "⚠️  \$n is NOT in the certificate — that name will fail TLS before any HTTP. Re-run certbot once it resolves here." ;;
      esac
    done
    echo "   Expires: \$(openssl x509 -in "\$CERT" -noout -enddate 2>/dev/null | cut -d= -f2)"
  else
    echo "⚠️  no certificate at \$CERT — HTTPS is not live."
  fi

  # systemd timer OR a cron drop-in; distros ship one or the other. Neither => renewal never runs.
  if systemctl list-timers 'certbot*' --all --no-pager 2>/dev/null | grep -q certbot; then
    systemctl enable --now certbot.timer >/dev/null 2>&1 || true
    echo "✅ certbot.timer present."
  elif [ -f /etc/cron.d/certbot ]; then
    echo "✅ certbot cron drop-in present."
  else
    echo "❌ NO RENEWAL MECHANISM. The certificate expires in ~90 days and TLS dies silently."
    echo "   Fix: systemctl enable --now certbot.timer   (or install a cron drop-in)"
  fi
  # The real bar: prove renewal WORKS rather than that a timer exists. Presence is not function.
  if certbot renew --dry-run >/dev/null 2>&1; then
    echo "✅ certbot renew --dry-run passed — renewal is proven, not assumed."
  else
    echo "⚠️  certbot renew --dry-run FAILED. The cert is live now and will expire in ~90 days."
    echo "   Diagnose: certbot renew --dry-run"
  fi

  # D-2 forward-compat: let a non-root service read the LE cert for app-terminated DoT/DoQ. The
  # daemon runs as root today so this is not strictly required now — but the moment SD-53 drops it
  # to a dedicated/DynamicUser service, DoT would silently break without this. An ACL survives
  # certbot renewals better than chmod on archive/. Idempotent; failures are non-fatal (DoT is off
  # by default, and root can read the key regardless).
  if command -v setfacl >/dev/null 2>&1; then
    getent group syxx-cert >/dev/null 2>&1 || groupadd syxx-cert 2>/dev/null || true
    # A121: add the service user to syxx-cert so the unprivileged daemon can read the TLS
    # key for app-terminated DoT/DoQ. Without this, setfacl grants the group read but the
    # service user is not a member — certificate loading fails and the listener stays down.
    usermod -aG syxx-cert "\${SVC_USER}" 2>/dev/null || true
    setfacl -R -m g:syxx-cert:rX /etc/letsencrypt/live /etc/letsencrypt/archive 2>/dev/null \
      && echo "✅ syxx-cert group granted read on the LE cert (for app-terminated DoT/DoQ)." \
      || echo "ℹ️  could not set cert ACL (fine while the service runs as root; revisit under SD-53)."
    # certbot's renewal can reset perms; re-apply the ACL on every renewal via a deploy-hook.
    HOOK=/etc/letsencrypt/renewal-hooks/deploy/syxx-cert-acl.sh
    mkdir -p "\$(dirname "\$HOOK")"
    printf '#!/bin/sh\nsetfacl -R -m g:syxx-cert:rX /etc/letsencrypt/live /etc/letsencrypt/archive 2>/dev/null || true\n' > "\$HOOK"
    chmod +x "\$HOOK"
  else
    echo "ℹ️  setfacl not present; skipping cert-group ACL (service runs as root, so DoT can still read the key)."
  fi
else
  echo "⚠️  LETSENCRYPT_EMAIL not set — skipping cert issuance. Set it and re-run to enable HTTPS."
fi
EOF
  then echo "✅ Reverse proxy configured."
  else echo "⚠️  nginx/TLS step did not complete — the daemon on :8080 is still running. Fix the proxy and re-run with SETUP_TLS=1."
  fi
fi

# ---- Rotating DB backup timer (SD-56 / SHIP-v1 S7-1) -------------------------------------------
# The single most dangerous gap: the DB holds every license, trial anchor, and customer email on
# local disk with nothing copying it off. This installs a systemd timer that runs the online
# backup daily. LOCAL step one — an off-host copy of /opt/SyxxServer/backups is the operator's
# follow-up (rsync/S3), and until it exists a host loss still loses these. Say so, don't imply safe.
remote "bash -s" <<REMOTE_BK
set -e
# E064: copy the backup script to a root-owned location outside the syxx-writable tree,
# so a compromise of the daemon user cannot rewrite it to gain root at the next timer fire.
cp ${REMOTE_DIR}/scripts/syxx-backup.sh /usr/local/libexec/syxx-backup.sh
chown root:root /usr/local/libexec/syxx-backup.sh
chmod 0755 /usr/local/libexec/syxx-backup.sh

cat > /etc/systemd/system/syxx-backup.service <<UNIT
[Unit]
Description=Syxx DB online backup (rotating)
After=syxx-server.service
[Service]
Type=oneshot
User=\${SVC_USER}
Environment=SYXX_SERVER_DIR=${REMOTE_DIR}
ExecStart=/bin/sh /usr/local/libexec/syxx-backup.sh
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=${REMOTE_DIR}/backups
PrivateTmp=true
ProtectHome=true
CapabilityBoundingSet=
UNIT
cat > /etc/systemd/system/syxx-backup.timer <<UNIT
[Unit]
Description=Run the Syxx DB backup daily
[Timer]
OnCalendar=daily
Persistent=true
[Install]
WantedBy=timers.target
UNIT
chmod +x ${REMOTE_DIR}/scripts/syxx-backup.sh 2>/dev/null || true
systemctl daemon-reload
systemctl enable --now syxx-backup.timer
# Take one backup right now so a restore point exists immediately, not only after the first daily fire.
# A120: gate success message on actual backup artifact existence.
if systemctl start syxx-backup.service; then
  if ls ${REMOTE_DIR}/backups/syxx-server-*.db >/dev/null 2>&1; then
    echo "✅ DB backup timer installed (daily). First backup taken now."
  else
    echo "⚠️  backup service ran but no backup artifact found — check: journalctl -u syxx-backup"
  fi
else
  echo "⚠️  initial backup run failed — check: journalctl -u syxx-backup"
  echo "   DB backup timer installed (daily), but NO restore point exists yet."
fi
echo "   ⚠️  LOCAL only: add an OFF-HOST copy of ${REMOTE_DIR}/backups or a host loss still loses everything."
REMOTE_BK

# ---- Sentry telemetry deployment (Phase 1 of OBSERVABILITY-PLAN.md) ----------------------------
# Detects whether Sentry self-hosted is running and healthy. Deploys or redeploys as needed.
# Checks performed:
#   1. Docker + Docker Compose installed?
#   2. Sentry self-hosted cloned at /opt/sentry?
#   3. Sentry containers running and healthy?
#   4. sentry-sdk importable by the daemon's python3?
#   5. SENTRY_DSN configured in /etc/syxx/syxx.env?
#
# The bar is: daemon can import sentry_sdk AND a DSN is present AND the Sentry
# health endpoint responds. If any leg fails, this section deploys/repairs.
#
# Resource guard: Sentry self-hosted needs 4GB+ RAM and ~20GB disk. If the box
# cannot support it, this section warns and installs sentry-sdk only.

echo "=================================================="
echo "        Sentry Telemetry Health Check             "
echo "=================================================="

remote "bash -s" <<'SENTRY_CHECK'
set -u

SENTRY_DIR="/opt/sentry"
NEED_DEPLOY=0
NEED_REPAIR=0
SKIP_SELFHOSTED=0
REASONS=""

# --- Check 1: Docker installed? ---
if ! command -v docker >/dev/null 2>&1; then
  echo "⚠️  Docker not installed."
  NEED_DEPLOY=1
  REASONS="${REASONS}  - Docker not installed\n"
elif ! docker compose version >/dev/null 2>&1; then
  echo "⚠️  Docker Compose not available."
  NEED_DEPLOY=1
  REASONS="${REASONS}  - Docker Compose not available\n"
else
  echo "✅ Docker $(docker --version | awk '{print $3}' | tr -d ',')"
  echo "✅ Docker Compose $(docker compose version --short 2>/dev/null || echo 'available')"
fi

# --- Check 2: Sentry self-hosted cloned? ---
if [ "$NEED_DEPLOY" -eq 0 ] && [ ! -d "${SENTRY_DIR}/.git" ]; then
  echo "⚠️  Sentry self-hosted not found at ${SENTRY_DIR}."
  NEED_DEPLOY=1
  REASONS="${REASONS}  - Sentry self-hosted not cloned\n"
elif [ -d "${SENTRY_DIR}/.git" ]; then
  echo "✅ Sentry self-hosted present at ${SENTRY_DIR}"
fi

# --- Check 3: Sentry containers running and healthy? ---
if [ "$NEED_DEPLOY" -eq 0 ]; then
  if cd "${SENTRY_DIR}" 2>/dev/null; then
    RUNNING=$(docker compose ps --format '{{.Service}}' 2>/dev/null | wc -l | tr -d ' ')
    if [ "$RUNNING" -lt 5 ]; then
      echo "⚠️  Only ${RUNNING} Sentry containers running (expected 10+)."
      NEED_REPAIR=1
      REASONS="${REASONS}  - Only ${RUNNING}/10+ Sentry containers running\n"
    else
      echo "✅ ${RUNNING} Sentry containers running"
    fi
    if curl -sf "http://localhost:9000/_health/" >/dev/null 2>&1; then
      echo "✅ Sentry health endpoint responding"
    else
      echo "⚠️  Sentry health endpoint not responding at localhost:9000"
      NEED_REPAIR=1
      REASONS="${REASONS}  - Sentry health endpoint not responding\n"
    fi
  else
    NEED_DEPLOY=1
    REASONS="${REASONS}  - Cannot cd to ${SENTRY_DIR}\n"
  fi
fi

# --- Check 4: sentry-sdk importable? ---
if python3 -c 'import sentry_sdk' 2>/dev/null; then
  SENTRY_VER=$(python3 -c 'import sentry_sdk; print(sentry_sdk.VERSION)' 2>/dev/null || echo '?')
  echo "✅ sentry-sdk ${SENTRY_VER} importable"
else
  echo "⚠️  sentry-sdk not importable by python3."
  NEED_REPAIR=1
  REASONS="${REASONS}  - sentry-sdk not importable\n"
fi

# --- Check 5: SENTRY_DSN configured? ---
if grep -q '^SENTRY_DSN=.' /etc/syxx/syxx.env 2>/dev/null; then
  DSN_VAL=$(grep '^SENTRY_DSN=' /etc/syxx/syxx.env | head -1 | cut -d= -f2-)
  echo "✅ SENTRY_DSN configured (${DSN_VAL:0:30}...)"
else
  echo "⚠️  SENTRY_DSN not configured in /etc/syxx/syxx.env"
  NEED_REPAIR=1
  REASONS="${REASONS}  - SENTRY_DSN not in env file\n"
fi

# --- Decision ---
if [ "$NEED_DEPLOY" -eq 0 ] && [ "$NEED_REPAIR" -eq 0 ]; then
  echo ""
  echo "✅ Sentry telemetry is fully operational. No action needed."
  exit 0
fi

echo ""
if [ "$NEED_DEPLOY" -eq 1 ]; then
  echo "🔧 Sentry needs deployment. Reasons:"
else
  echo "🔧 Sentry needs repair. Reasons:"
fi
printf "${REASONS}"

# --- Resource guard (only for full self-hosted deploy) ---
FREE_MEM_KB=$(grep MemAvailable /proc/meminfo 2>/dev/null | awk '{print $2}' || echo 0)
FREE_MEM_MB=$((FREE_MEM_KB / 1024))
FREE_DISK_KB=$(df / 2>/dev/null | tail -1 | awk '{print $4}' || echo 0)
FREE_DISK_GB=$((FREE_DISK_KB / 1024 / 1024))
echo ""
echo "Resources: ${FREE_MEM_MB}MB RAM available, ${FREE_DISK_GB}GB disk available"

if [ "$NEED_DEPLOY" -eq 1 ] && [ "$FREE_MEM_MB" -lt 3500 ]; then
  echo "⚠️  Insufficient RAM for Sentry self-hosted (need 4GB+, have ${FREE_MEM_MB}MB). SDK-only install."
  SKIP_SELFHOSTED=1
fi
if [ "$NEED_DEPLOY" -eq 1 ] && [ "$FREE_DISK_GB" -lt 15 ]; then
  echo "⚠️  Insufficient disk for Sentry self-hosted (need 20GB+, have ${FREE_DISK_GB}GB). SDK-only install."
  SKIP_SELFHOSTED=1
fi

# --- Deploy: Docker ---
if [ "$NEED_DEPLOY" -eq 1 ] && [ "$SKIP_SELFHOSTED" -eq 0 ]; then
  echo ""
  echo "=== Installing Docker ==="
  if command -v apt-get >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -y -qq
    apt-get install -y -qq ca-certificates curl gnupg
    install -m 0755 -d /etc/apt/keyrings
    DISTRO_ID=$(. /etc/os-release && echo "$ID")
    DISTRO_CODE=$(. /etc/os-release && echo "$VERSION_CODENAME")
    curl -fsSL "https://download.docker.com/linux/${DISTRO_ID}/gpg" \
      | gpg --dearmor -o /etc/apt/keyrings/docker.gpg 2>/dev/null
    chmod a+r /etc/apt/keyrings/docker.gpg
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/${DISTRO_ID} ${DISTRO_CODE} stable" \
      > /etc/apt/sources.list.d/docker.list
    apt-get update -y -qq
    apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-compose-plugin
    systemctl enable docker && systemctl start docker
    echo "✅ Docker installed"
  else
    echo "⚠️  Cannot auto-install Docker (no apt-get). SDK-only install."
    SKIP_SELFHOSTED=1
  fi
fi

# --- Deploy: Sentry self-hosted ---
if [ "$NEED_DEPLOY" -eq 1 ] && [ "$SKIP_SELFHOSTED" -eq 0 ]; then
  echo ""
  echo "=== Cloning Sentry self-hosted ==="
  if [ ! -d "${SENTRY_DIR}" ]; then
    git clone --depth 1 https://github.com/getsentry/self-hosted.git "${SENTRY_DIR}"
  fi
  cd "${SENTRY_DIR}"
  LATEST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
  [ -n "$LATEST_TAG" ] && git checkout "$LATEST_TAG" 2>/dev/null

  echo "=== Running Sentry install (5-15 minutes) ==="
  # Flag detection, not a hardcoded guess: getsentry/self-hosted has renamed the
  # non-interactive user flag across releases (--skip-user / --skip-user-prompt /
  # --no-user-prompt). Passing an unknown flag makes install.sh abort at argument
  # parsing BEFORE it builds and tags sentry-self-hosted-local, and the later
  # `docker compose` then fails with a misleading "pull access denied ...
  # sentry-self-hosted-local" — an error about the wrong thing entirely.
  SENTRY_USER_FLAG=""
  for flag in --no-user-prompt --skip-user-prompt --skip-user; do
    if ./install.sh --help 2>&1 | grep -q -- "$flag"; then
      SENTRY_USER_FLAG="$flag"; break
    fi
  done
  if [ -z "$SENTRY_USER_FLAG" ]; then
    echo "ℹ️  No known non-interactive user flag found in install.sh --help; running without one."
  else
    echo "Using install.sh $SENTRY_USER_FLAG"
  fi
  # FAIL CLOSED. This previously ended in `|| echo "install.sh reported errors"`, which
  # swallowed a hard failure and ran `docker compose up -d` against a half-built tree,
  # burying the real cause under a cascade. A failed install is a failed install.
  if ! ./install.sh $SENTRY_USER_FLAG 2>&1; then
    echo "❌ Sentry install.sh FAILED. Not starting containers — a partial install would"
    echo "   report misleading image-resolution errors instead of this one."
    echo "   Diagnose on the host: cd ${SENTRY_DIR} && ./install.sh --help && ./install.sh"
    exit 1
  fi
  docker compose up -d
  echo "Waiting 60s for services to initialize..."
  sleep 60
  if curl -sf "http://localhost:9000/_health/" >/dev/null 2>&1; then
    echo "✅ Sentry self-hosted is running"
  else
    echo "⚠️  Sentry may still be starting. Check: cd /opt/sentry && docker compose logs"
  fi
fi

# --- Repair: restart containers ---
if [ "$NEED_REPAIR" -eq 1 ] && [ "$SKIP_SELFHOSTED" -eq 0 ] && [ -d "${SENTRY_DIR}" ]; then
  echo ""
  echo "=== Restarting Sentry containers ==="
  cd "${SENTRY_DIR}"
  docker compose down 2>/dev/null || true
  docker compose up -d
  sleep 30
  if curl -sf "http://localhost:9000/_health/" >/dev/null 2>&1; then
    echo "✅ Sentry restarted and healthy"
  else
    echo "⚠️  Sentry restart did not reach healthy state. Check: docker compose logs"
  fi
fi

# --- Always: ensure sentry-sdk ---
echo ""
echo "=== Ensuring sentry-sdk is installed ==="
if ! python3 -c 'import sentry_sdk' 2>/dev/null; then
  REQ=/opt/SyxxServer/requirements.txt
  # E068: drop 2>/dev/null so pip errors are visible, and make the failure fatal (exit 1)
  # the same way the cryptography block does — a first-ship deploy with no crash reporting
  # and a success banner is a dishonest deploy.
  python3 -m pip install -r "$REQ" \
    || python3 -m pip install --break-system-packages sentry-sdk \
    || { echo "❌ Could not install sentry-sdk"; exit 1; }
  if python3 -c 'import sentry_sdk; print("✅ sentry-sdk " + sentry_sdk.VERSION + " installed")' 2>/dev/null; then
    systemctl restart syxx-server.service 2>/dev/null \
      && echo "✅ Daemon restarted to pick up sentry-sdk"
  else
    echo "❌ sentry-sdk still not importable. Manual fix required."
    exit 1
  fi
else
  echo "✅ sentry-sdk already importable"
fi

# --- DSN guidance ---
if ! grep -q '^SENTRY_DSN=.' /etc/syxx/syxx.env 2>/dev/null; then
  echo ""
  echo "=== SENTRY_DSN not yet configured ==="
  if [ "$SKIP_SELFHOSTED" -eq 0 ] && [ -d "${SENTRY_DIR}" ]; then
    echo "  1. Create admin: cd /opt/sentry && docker compose exec web sentry createuser --superuser"
    echo "  2. Log in at http://localhost:9000"
    echo "  3. Create projects: syxx-macos, syxx-windows, syxx-linux, syxx-server"
    echo "  4. Copy DSN from Settings → Projects → [project] → Client Keys"
    echo "  5. echo 'SENTRY_DSN=<dsn>' >> /etc/syxx/syxx.env && systemctl restart syxx-server"
  else
    echo "  Self-hosted Sentry was skipped. Configure an external DSN:"
    echo "    echo 'SENTRY_DSN=<your-dsn>' >> /etc/syxx/syxx.env"
    echo "    systemctl restart syxx-server"
  fi
fi

echo ""
echo "=== Sentry telemetry check complete ==="
SENTRY_CHECK

# CI-023: post-deployment health check — verify the server is responding before declaring success.
echo ""
echo "=== Post-deployment health check ==="
HEALTH_URL="https://${DOMAIN}/api/health"
HEALTH_RETRIES=5
HEALTH_OK=0
for i in $(seq 1 $HEALTH_RETRIES); do
  if curl -sf --max-time 10 "$HEALTH_URL" >/dev/null 2>&1; then
    echo "✅ Health check passed: $HEALTH_URL returned 200"
    HEALTH_OK=1
    break
  fi
  echo "  Attempt $i/$HEALTH_RETRIES: $HEALTH_URL not yet responding…"
  sleep 3
done
if [ "$HEALTH_OK" -eq 0 ]; then
  echo "❌ CI-023: Health check failed after $HEALTH_RETRIES attempts."
  echo "   Check: ssh ${REMOTE_USER}@${SERVER_IP} 'journalctl -u syxx-server --no-pager -n 50'"
  # F079: External health check failed — attempt rollback via SSH.
  echo "🔄 F079: Attempting rollback from post-deploy health check…"
  remote "bash -s" <<ROLLBACK_EOF
set -eu
BACKUP_FILE="${BACKUP_PATH}"
if [ -f "\${BACKUP_FILE}" ]; then
  echo "  1/4 Stopping syxx-server.service…"
  systemctl stop syxx-server.service 2>/dev/null || true
  echo "  2/4 Clearing deployment directory ${REMOTE_DIR}…"
  find "${REMOTE_DIR}" -mindepth 1 -delete 2>/dev/null || true
  echo "  3/4 Restoring from backup \${BACKUP_FILE}…"
  tar xzf "\${BACKUP_FILE}" -C "${REMOTE_DIR}"
  SVC_USER="\$(stat -c '%U' "${REMOTE_DIR}" 2>/dev/null || echo syxx)"
  chown -R "\${SVC_USER}" "${REMOTE_DIR}" 2>/dev/null || true
  echo "  4/4 Restarting syxx-server.service…"
  systemctl restart syxx-server.service
  sleep 3
  if systemctl is-active --quiet syxx-server.service \
     && curl -fsS http://127.0.0.1:8080/api/pubkey >/dev/null 2>&1; then
    echo "✅ F079 Rollback SUCCEEDED — previous version restored."
  else
    echo "❌ F079 Rollback FAILED — manual intervention required."
    echo "   ssh ${REMOTE_USER}@${SERVER_IP}"
    journalctl -u syxx-server -n 30 --no-pager
  fi
else
  echo "❌ No backup at \${BACKUP_FILE} — cannot roll back."
  echo "   Manual intervention required: ssh ${REMOTE_USER}@${SERVER_IP}"
fi
ROLLBACK_EOF
fi

# CI-024: verify firewall allows only necessary ports (22, 80, 443).
echo ""
echo "=== Firewall verification ==="
remote "if command -v ufw >/dev/null 2>&1; then \
  ufw status | head -5; \
  echo 'Ports 22,80,443 should be allowed; all others denied.'; \
else \
  echo 'ufw not installed — verify firewall rules manually (iptables -L -n)'; \
fi"

# CI-025: nginx rate limiting — ensure the reverse proxy limits API request rates.
echo ""
echo "=== Nginx rate limiting ==="
remote "if [ -f /etc/nginx/conf.d/syxx.conf ]; then \
  if grep -q 'limit_req_zone' /etc/nginx/nginx.conf /etc/nginx/conf.d/syxx.conf 2>/dev/null; then \
    echo '✅ Rate limiting configured in nginx'; \
  else \
    echo '⚠️  No rate limiting found in nginx config. Add limit_req_zone to prevent API abuse.'; \
  fi; \
else \
  echo 'ℹ️  No nginx config found (nginx may not be set up yet).'; \
fi"

echo "=================================================="
echo "🎉 Deployed. Monitor: ssh ${REMOTE_USER}@${SERVER_IP} 'journalctl -u syxx-server -f'"
echo "   DNS leak zones must have NS records delegating to ${SERVER_IP}."
echo "=================================================="
