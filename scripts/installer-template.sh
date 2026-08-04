#!/usr/bin/env bash
# =============================================================================
# Syxx self-contained server installer.
#
# This ONE file carries the entire Syxx server (Python package + website) as an
# embedded payload. Drop it on a fresh Linux box and run it as root — it installs
# dependencies, writes a systemd unit, starts the daemon, and (optionally) puts an
# nginx + Let's Encrypt reverse proxy in front. No Mac-side push, no rsync.
#
#   sudo DOMAIN=syxx.app LETSENCRYPT_EMAIL=you@example.com bash syxx-installer.sh
#
# Distro-aware: apt / dnf / yum / apk / zypper / pacman. On RHEL-family
# (AlmaLinux/Rocky) it also enables EPEL for certbot, flips the SELinux
# httpd_can_network_connect boolean, and opens firewalld for 80/443/53.
# =============================================================================
set -euo pipefail

REMOTE_DIR="${REMOTE_DIR:-/opt/SyxxServer}"
DOMAIN="${DOMAIN:-syxx.app}"
LETSENCRYPT_EMAIL="${LETSENCRYPT_EMAIL:-jjwarne@icloud.com}"
SETUP_TLS="${SETUP_TLS:-1}"
SYXX_SESSION_SECRET="${SYXX_SESSION_SECRET:-}"
SYXX_KEYGEN_ACCOUNT_ID="${SYXX_KEYGEN_ACCOUNT_ID:-4b50cc83-1664-4626-99b1-fbd4e76e35c3}"
SYXX_LICENSE_KEYS="${SYXX_LICENSE_KEYS:-}"
SYXX_ENFORCE_NATIVE="${SYXX_ENFORCE_NATIVE:-true}"
# Outbound email (optional). Leave SYXX_SMTP_HOST empty to disable all mail.
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

if [ "$(id -u)" != "0" ]; then
  echo "Please run as root:  sudo bash $0"
  exit 1
fi

echo "=================================================="
echo "          Syxx self-contained installer           "
echo "=================================================="
echo "Dir    : ${REMOTE_DIR}"
echo "Domain : ${DOMAIN}"
echo "TLS    : ${SETUP_TLS}"
echo "--------------------------------------------------"

pkg_install() {
  if command -v apt-get >/dev/null 2>&1; then
    export DEBIAN_FRONTEND=noninteractive; apt-get update -y && apt-get install -y "$@"
  elif command -v dnf >/dev/null 2>&1; then dnf install -y "$@"
  elif command -v yum >/dev/null 2>&1; then yum install -y "$@"
  elif command -v apk >/dev/null 2>&1; then apk add --no-cache "$@"
  elif command -v zypper >/dev/null 2>&1; then zypper --non-interactive install "$@"
  elif command -v pacman >/dev/null 2>&1; then pacman -Sy --noconfirm "$@"
  else echo "WARNING: no supported package manager; install manually: $*"; return 1; fi
}

# 1. Extract the embedded Server/ tree (everything after the __SYXX_PAYLOAD__ marker).
echo "Extracting server files to ${REMOTE_DIR}..."
mkdir -p "${REMOTE_DIR}"
PAYLOAD_LINE="$(awk '/^__SYXX_PAYLOAD__$/ { print NR + 1; exit }' "$0")"
tail -n +"${PAYLOAD_LINE}" "$0" | base64 -d | tar xzf - -C "${REMOTE_DIR}"
if [ -f "${REMOTE_DIR}/index.html" ] && [ -f "${REMOTE_DIR}/server.py" ]; then
  IMG_COUNT="$(find "${REMOTE_DIR}" -type f \
      \( -iname '*.svg' -o -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.gif' -o -iname '*.webp' -o -iname '*.ico' \) \
      | wc -l | tr -d ' ')"
  echo "  OK: server.py + index.html + ${IMG_COUNT} image asset(s) + package extracted."
else
  echo "  ERROR: extraction incomplete (index.html/server.py missing)." >&2
  exit 1
fi

# 2. Python + a session secret.
command -v python3 >/dev/null 2>&1 || pkg_install python3
if [ -z "${SYXX_SESSION_SECRET}" ]; then
  SYXX_SESSION_SECRET="$(python3 -c 'import secrets;print(secrets.token_hex(32))' 2>/dev/null || openssl rand -hex 32)"
  echo "Generated a random SYXX_SESSION_SECRET."
fi

# 3. systemd unit.
echo "Installing systemd service..."
cat > /etc/systemd/system/syxx-server.service <<UNIT
[Unit]
Description=Syxx DNS + Web API Daemon
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=${REMOTE_DIR}
ExecStart=/usr/bin/env python3 ${REMOTE_DIR}/server.py
Restart=always
RestartSec=5
Environment=SYXX_HTTP_PORT=8080
Environment=SYXX_DNS_PORT=53
Environment=SYXX_DB_PATH=${REMOTE_DIR}/syxx-server.db
Environment=SYXX_SESSION_SECRET=${SYXX_SESSION_SECRET}
Environment=SYXX_KEYGEN_ACCOUNT_ID=${SYXX_KEYGEN_ACCOUNT_ID}
Environment=SYXX_LICENSE_KEYS=${SYXX_LICENSE_KEYS}
Environment=SYXX_ENFORCE_NATIVE=${SYXX_ENFORCE_NATIVE}
Environment=SYXX_SMTP_HOST=${SYXX_SMTP_HOST}
Environment=SYXX_SMTP_PORT=${SYXX_SMTP_PORT}
Environment=SYXX_SMTP_STARTTLS=${SYXX_SMTP_STARTTLS}
Environment=SYXX_SMTP_USER=${SYXX_SMTP_USER}
Environment=SYXX_SMTP_PASSWORD=${SYXX_SMTP_PASSWORD}
Environment=SYXX_SMTP_TIMEOUT=${SYXX_SMTP_TIMEOUT}
Environment=SYXX_SMTP_INSECURE_CERTS=${SYXX_SMTP_INSECURE_CERTS}
Environment="SYXX_MAIL_FROM=${SYXX_MAIL_FROM}"
Environment=SYXX_BETA_DIGEST_TO=${SYXX_BETA_DIGEST_TO}
AmbientCapabilities=CAP_NET_BIND_SERVICE
NoNewPrivileges=true
ProtectSystem=full
PrivateTmp=true

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable syxx-server.service
systemctl restart syxx-server.service
sleep 1
echo "Daemon status: $(systemctl is-active syxx-server.service)"

# 4. nginx + TLS reverse proxy (non-fatal; the daemon is already up on :8080).
if [ "${SETUP_TLS}" = "1" ]; then
  echo "Configuring nginx + TLS for ${DOMAIN}..."

  # RHEL-family: certbot ships in EPEL.
  if command -v dnf >/dev/null 2>&1; then dnf install -y epel-release 2>/dev/null || true; fi
  command -v nginx >/dev/null 2>&1 || pkg_install nginx || true
  command -v certbot >/dev/null 2>&1 || pkg_install certbot python3-certbot-nginx || true

  # SELinux (Alma/RHEL enforce by default) blocks nginx -> local :8080 (502 / blank).
  if command -v setsebool >/dev/null 2>&1; then setsebool -P httpd_can_network_connect 1 2>/dev/null || true; fi

  # firewalld (Alma/RHEL) blocks 80/443/53 until opened.
  if command -v firewall-cmd >/dev/null 2>&1 && systemctl is-active --quiet firewalld 2>/dev/null; then
    firewall-cmd --permanent --add-service=http --add-service=https 2>/dev/null || true
    firewall-cmd --permanent --add-port=53/udp --add-port=53/tcp 2>/dev/null || true
    firewall-cmd --reload 2>/dev/null || true
  fi

  mkdir -p /etc/nginx/conf.d
  cat > /etc/nginx/conf.d/syxx.conf <<NGINX
server {
    listen 80;
    server_name ${DOMAIN};
    client_max_body_size 1m;
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_read_timeout 30s;
    }
}
NGINX
  rm -f /etc/nginx/sites-enabled/default 2>/dev/null || true
  systemctl enable nginx 2>/dev/null || true
  if command -v nginx >/dev/null 2>&1 && nginx -t 2>/dev/null; then
    systemctl restart nginx || systemctl start nginx || true
    echo "nginx: proxying :80 -> daemon :8080"
  else
    echo "WARNING: nginx not configured; the daemon is still reachable on :8080."
  fi

  if [ -n "${LETSENCRYPT_EMAIL}" ] && command -v certbot >/dev/null 2>&1; then
    certbot --nginx -d "${DOMAIN}" --non-interactive --agree-tos -m "${LETSENCRYPT_EMAIL}" --redirect \
      || echo "certbot deferred: point ${DOMAIN}'s A record at this box, then: certbot --nginx -d ${DOMAIN}"
  else
    echo "TLS cert skipped (set LETSENCRYPT_EMAIL and ensure ${DOMAIN} resolves here, then re-run)."
  fi
fi

echo "=================================================="
echo "Done. Quick check:  curl -s http://localhost:8080/healthz"
echo "Logs:  journalctl -u syxx-server -f"
echo "DNS leak zones need NS records delegating to this box."
echo "=================================================="
exit 0
__SYXX_PAYLOAD__
