#!/usr/bin/env bash
# deploy-sentry.sh — Deploy Sentry self-hosted on a fresh Linux server
# Run as root or with sudo: sudo bash deploy-sentry.sh
#
# What this does:
#   1. Installs Docker + Docker Compose (if not present)
#   2. Clones Sentry self-hosted
#   3. Runs the install script (creates admin user, configures services)
#   4. Starts Sentry
#   5. Creates the 4 Syxx projects and outputs DSNs
#
# Requirements: Ubuntu 22.04+ or Debian 12+, 4GB+ RAM, 20GB+ disk

set -euo pipefail

echo "=== Syxx Sentry Self-Hosted Deployment ==="
echo ""

# --- OS check ---
if [ "$(uname)" = "Darwin" ]; then
    echo "ERROR: This script must run on a Linux server, not macOS."
    echo ""
    echo "Copy it to your server and run it there:"
    echo "  scp scripts/deploy-sentry.sh user@your-server:/tmp/"
    echo "  ssh user@your-server 'sudo bash /tmp/deploy-sentry.sh'"
    exit 1
fi

# --- Pre-flight checks ---
if [ "$(id -u)" -ne 0 ]; then
    echo "ERROR: Run as root or with sudo."
    echo "  sudo bash deploy-sentry.sh"
    exit 1
fi

FREE_MEM_KB=$(grep MemAvailable /proc/meminfo | awk '{print $2}')
FREE_MEM_GB=$((FREE_MEM_KB / 1024 / 1024))
if [ "$FREE_MEM_GB" -lt 3 ]; then
    echo "WARNING: Only ${FREE_MEM_GB}GB available RAM. Sentry recommends 4GB+."
    echo "It may work but could be slow. Continue? (y/N)"
    read -r yn
    [ "$yn" = "y" ] || exit 1
fi

FREE_DISK_KB=$(df / | tail -1 | awk '{print $4}')
FREE_DISK_GB=$((FREE_DISK_KB / 1024 / 1024))
if [ "$FREE_DISK_GB" -lt 15 ]; then
    echo "WARNING: Only ${FREE_DISK_GB}GB free disk. Sentry needs ~20GB."
    echo "Continue? (y/N)"
    read -r yn
    [ "$yn" = "y" ] || exit 1
fi

echo "Memory: ${FREE_MEM_GB}GB available"
echo "Disk: ${FREE_DISK_GB}GB available"
echo ""

# --- Step 1: Install Docker ---
echo "=== Step 1: Installing Docker ==="
if command -v docker &>/dev/null; then
    echo "Docker already installed: $(docker --version)"
else
    echo "Installing Docker..."
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg

    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
    chmod a+r /etc/apt/keyrings/docker.gpg

    echo \
      "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
      $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
      tee /etc/apt/sources.list.d/docker.list > /dev/null

    apt-get update -qq
    apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

    systemctl enable docker
    systemctl start docker
    echo "Docker installed: $(docker --version)"
fi

# Verify Docker Compose
if docker compose version &>/dev/null; then
    echo "Docker Compose: $(docker compose version --short)"
else
    echo "ERROR: Docker Compose not available. Install docker-compose-plugin."
    exit 1
fi

# --- Step 2: Clone Sentry self-hosted ---
echo ""
echo "=== Step 2: Cloning Sentry self-hosted ==="
SENTRY_DIR="/opt/sentry"
if [ -d "$SENTRY_DIR" ]; then
    echo "Sentry directory already exists at $SENTRY_DIR"
    echo "To reinstall from scratch, remove it first: rm -rf $SENTRY_DIR"
else
    git clone https://github.com/getsentry/self-hosted.git "$SENTRY_DIR"
    echo "Cloned to $SENTRY_DIR"
fi

cd "$SENTRY_DIR"

# Pin to a stable release (update this when upgrading)
LATEST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
if [ -n "$LATEST_TAG" ]; then
    git checkout "$LATEST_TAG"
    echo "Checked out release: $LATEST_TAG"
fi

# --- Step 3: Configure ---
echo ""
echo "=== Step 3: Configuration ==="

# Set the external URL — change this to your actual domain
SENTRY_DOMAIN="${SENTRY_DOMAIN:-sentry.syxx.app}"
echo "Sentry will be accessible at: https://$SENTRY_DOMAIN"
echo ""
echo "If this is wrong, set SENTRY_DOMAIN before running:"
echo "  SENTRY_DOMAIN=your-domain.com sudo bash deploy-sentry.sh"
echo ""

# E071: actually apply SENTRY_DOMAIN to sentry.conf.py so system.url-prefix reflects it.
# Previously this block was a no-op that read a config file and printed "Using default"
# while writing nothing — the operator was told configuration happened when it did not.
SENTRY_CONF="$SENTRY_DIR/sentry/sentry.conf.py"
if [ -f "$SENTRY_DIR/sentry/sentry.conf.example.py" ] && [ ! -f "$SENTRY_CONF" ]; then
    cp "$SENTRY_DIR/sentry/sentry.conf.example.py" "$SENTRY_CONF"
fi
if [ -f "$SENTRY_CONF" ]; then
    if grep -q "SENTRY_BIND" "$SENTRY_CONF"; then
        # Update system.url-prefix if present, otherwise append it.
        if grep -q "system.url-prefix" "$SENTRY_CONF"; then
            sed -i "s|^SENTRY_OPTIONS\[\"system.url-prefix\"\].*|SENTRY_OPTIONS[\"system.url-prefix\"] = \"https://$SENTRY_DOMAIN\"|" "$SENTRY_CONF"
        else
            echo "SENTRY_OPTIONS[\"system.url-prefix\"] = \"https://$SENTRY_DOMAIN\"" >> "$SENTRY_CONF"
        fi
        echo "Applied SENTRY_DOMAIN to $SENTRY_CONF"
    fi
else
    echo "⚠️  sentry.conf.py not found — SENTRY_DOMAIN not applied. Set it manually after install."
fi

# --- Step 4: Run install ---
echo ""
echo "=== Step 4: Running Sentry install ==="
echo "This will:"
echo "  - Pull ~20 Docker images (~5GB download)"
echo "  - Start PostgreSQL, ClickHouse, Kafka, Redis, etc."
echo "  - Prompt you to create an admin user"
echo ""
echo "This takes 5-15 minutes depending on server speed."
echo ""

# Run the install script non-interactively (the admin user is created below).
# The flag name has changed across getsentry/self-hosted releases, so detect it from
# --help rather than hardcoding: an unknown flag aborts install.sh at argument parsing,
# before it builds and tags sentry-self-hosted-local, and the failure then resurfaces as
# a misleading "pull access denied ... sentry-self-hosted-local" from docker compose.
echo "Starting install (this will take a while)..."
SENTRY_USER_FLAG=""
for flag in --no-user-prompt --skip-user-prompt --skip-user; do
    if ./install.sh --help 2>&1 | grep -q -- "$flag"; then
        SENTRY_USER_FLAG="$flag"; break
    fi
done
[ -n "$SENTRY_USER_FLAG" ] && echo "Using install.sh $SENTRY_USER_FLAG"
# `set -euo pipefail` (line 14) already aborts here on failure — which is correct:
# continuing to `docker compose up -d` against a half-built tree buries the real cause.
./install.sh $SENTRY_USER_FLAG

echo ""
echo "=== Step 5: Starting Sentry ==="
docker compose up -d

echo ""
echo "Waiting for services to be ready..."
sleep 30

# Check if Sentry is running
if curl -sf "http://localhost:9000/_health/" >/dev/null 2>&1; then
    echo "✅ Sentry is running!"
else
    echo "⚠️  Sentry may still be starting up. Check: docker compose logs"
fi

# --- Step 5: Create admin user ---
echo ""
echo "=== Step 5: Create admin user ==="
# E066: generate the password once, store it 0600, and print it so the operator can record it.
ADMIN_PASSWORD="$(openssl rand -base64 16)"
echo "Creating admin user..."
if docker compose exec web sentry createuser \
    --email admin@syxx.app \
    --password "$ADMIN_PASSWORD" \
    --superuser \
    --no-input; then
    echo ""
    echo "⚠️  RECORD THIS NOW — it will not be shown again:"
    echo "  Email:    admin@syxx.app"
    echo "  Password: $ADMIN_PASSWORD"
    echo ""
    # Also persist to a root-only file so it survives terminal scrollback loss.
    ( umask 077; echo "$ADMIN_PASSWORD" > /root/.syxx-sentry-admin )
    echo "  (Also saved to /root/.syxx-sentry-admin, mode 0600.)"
    echo ""
else
    echo ""
    echo "❌ User creation failed. Create one manually:"
    echo "  docker compose exec web sentry createuser --superuser"
    echo ""
fi

# --- Step 6: Create projects and collect DSNs ---
echo ""
echo "=== Step 6: Creating Syxx projects ==="

# Get or create the internal API token
API_TOKEN=$(docker compose exec -T web sentry django shell -c "
from sentry.models.apitoken import ApiToken
from sentry.models.organization import Organization
org = Organization.objects.first()
if org:
    # E067: grant real scopes so subsequent API calls succeed (project:read, project:write, project:admin).
    # scopes=0 produced a token rejected with 403 on every project-creation call.
    token = ApiToken.objects.create(
        user=org.get_default_owner(),
        scopes=ApiToken.get_default_scopes(),
    )
    print(token.token)
" 2>/dev/null || echo "")

if [ -z "$API_TOKEN" ]; then
    echo "Could not auto-create API token. Create one manually:"
    echo "  1. Log into Sentry at http://localhost:9000"
    echo "  2. Go to Settings → API → Auth Tokens → Create New Token"
    echo "  3. Grant project:read, project:write scopes"
    echo "  4. Copy the token and run:"
    echo ""
    echo "  export SENTRY_TOKEN=<your-token>"
    echo "  bash create-projects.sh"
    echo ""
else
    echo "API token created. Creating projects..."

    # Get the organization slug
    ORG_SLUG=$(docker compose exec -T web sentry django shell -c "
from sentry.models.organization import Organization
print(Organization.objects.first().slug)
" 2>/dev/null || echo "sentry")

    for PROJECT in syxx-macos syxx-windows syxx-linux syxx-server; do
        PLATFORM="python"
        case "$PROJECT" in
            syxx-macos) PLATFORM="cocoa" ;;
            syxx-windows) PLATFORM="csharp" ;;
            syxx-linux) PLATFORM="csharp" ;;
            syxx-server) PLATFORM="python" ;;
        esac

        echo "Creating project: $PROJECT (platform: $PLATFORM)"
        # E067: capture HTTP status to distinguish 409 (already exists) from auth/other errors.
        HTTP_CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST \
            "http://localhost:9000/api/0/projects/${ORG_SLUG}/?platform=${PLATFORM}" \
            -H "Authorization: Bearer ${API_TOKEN}" \
            -H "Content-Type: application/json" \
            -d "{\"name\": \"${PROJECT}\"}")
        if [ "$HTTP_CODE" = "409" ]; then
            echo "  (already exists)"
        elif [ "$HTTP_CODE" != "201" ] && [ "$HTTP_CODE" != "200" ]; then
            echo "  ❌ failed (HTTP $HTTP_CODE) — check token scopes"
        fi
    done

    echo ""
    echo "=== DSNs ==="
    echo "Collecting DSNs for each project..."
    echo ""

    for PROJECT in syxx-macos syxx-windows syxx-linux syxx-server; do
        DSN=$(curl -sf \
            "http://localhost:9000/api/0/projects/${ORG_SLUG}/${PROJECT}/keys/" \
            -H "Authorization: Bearer ${API_TOKEN}" 2>/dev/null | \
            python3 -c "import sys,json; keys=json.load(sys.stdin); print(keys[0]['dsn']['public'] if keys else 'NOT FOUND')" 2>/dev/null || echo "ERROR")
        echo "${PROJECT}: ${DSN}"
    done

    echo ""
    echo "=== Save these DSNs ==="
    echo "Set them as environment variables on each client:"
    echo ""
    echo "  # macOS (UserDefaults or env var)"
    echo "  export SENTRY_DSN=<syxx-macos DSN>"
    echo ""
    echo "  # Windows (env var)"
    echo "  \$env:SENTRY_DSN = '<syxx-windows DSN>'"
    echo ""
    echo "  # Linux (env var)"
    echo "  export SENTRY_DSN='<syxx-linux DSN>'"
    echo ""
    echo "  # Server (env var)"
    echo "  export SENTRY_DSN='<syxx-server DSN>'"
fi

# --- Step 7: Reverse proxy hint ---
echo ""
echo "=== Step 7: Reverse proxy (optional) ==="
echo "Sentry is accessible at http://localhost:9000"
echo ""
echo "To expose it at https://${SENTRY_DOMAIN}, set up a reverse proxy."
echo "Example Caddy config:"
echo ""
echo "  ${SENTRY_DOMAIN} {"
echo "      reverse_proxy localhost:9000"
echo "  }"
echo ""
echo "Or nginx:"
echo ""
echo "  server {"
echo "      listen 443 ssl;"
echo "      server_name ${SENTRY_DOMAIN};"
echo "      location / {"
echo "          proxy_pass http://localhost:9000;"
echo "          proxy_set_header Host \$host;"
echo "          proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;"
echo "      }"
echo "  }"

echo ""
echo "=== Deployment complete ==="
echo "Sentry is running at http://localhost:9000"
echo "Log in with the admin credentials created in Step 5."
