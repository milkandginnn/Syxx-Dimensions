#!/bin/bash
# fix-sentry-server.sh — Install sentry-sdk and wire SENTRY_DSN into the syxx-server service
# Run as root on the production server: sudo bash fix-sentry-server.sh

set -e

echo "=== Step 1: Install sentry-sdk ==="
if python3 -c 'import sentry_sdk' 2>/dev/null; then
    echo "✅ sentry-sdk already installed"
else
    echo "Installing sentry-sdk..."
    REQ=/opt/SyxxServer/requirements.txt
    if [ -f "$REQ" ] && grep -q sentry-sdk "$REQ"; then
        python3 -m pip install -r "$REQ" 2>/dev/null \
            || python3 -m pip install --break-system-packages -r "$REQ" 2>/dev/null
    else
        python3 -m pip install --break-system-packages sentry-sdk 2>/dev/null \
            || python3 -m pip install sentry-sdk 2>/dev/null
    fi
    if python3 -c 'import sentry_sdk; print("✅ sentry-sdk " + sentry_sdk.VERSION + " installed")' 2>/dev/null; then
        echo "✅ sentry-sdk installed successfully"
    else
        echo "❌ sentry-sdk still not importable. Check pip output above."
        exit 1
    fi
fi

echo ""
echo "=== Step 2: Check SENTRY_DSN configuration ==="

# Check if DSN is already in the env file
ENV_FILE=/etc/syxx/syxx.env
mkdir -p /etc/syxx

if [ -f "$ENV_FILE" ] && grep -q '^SENTRY_DSN=.\\+' "$ENV_FILE" 2>/dev/null; then
    echo "✅ SENTRY_DSN found in $ENV_FILE"
    grep '^SENTRY_DSN=' "$ENV_FILE" | sed 's/=.*/=***REDACTED***/' 
else
    echo "⚠️  SENTRY_DSN not configured in $ENV_FILE"
    echo ""
    echo "You need to get your DSN from Sentry.io:"
    echo "  1. Go to https://sentry.io"
    echo "  2. Navigate to Settings → Projects → syxx-server → Client Keys (DSN)"
    echo "  3. Copy the DSN (looks like: https://abc123...@o123456.ingest.sentry.io/789012)"
    echo ""
    echo "Then run:"
    echo "  echo 'SENTRY_DSN=<your-dsn-here>' >> $ENV_FILE"
    echo ""
    # Create the file with a placeholder if it doesn't exist
    if [ ! -f "$ENV_FILE" ]; then
        touch "$ENV_FILE"
        echo "Created $ENV_FILE (empty)"
    fi
fi

echo ""
echo "=== Step 3: Wire env file into systemd unit ==="
UNIT_FILE=/etc/systemd/system/syxx-server.service

if [ -f "$UNIT_FILE" ]; then
    if grep -q 'EnvironmentFile=' "$UNIT_FILE"; then
        echo "✅ EnvironmentFile already declared in unit"
    else
        echo "Adding EnvironmentFile to unit..."
        # Add EnvironmentFile after the [Service] line
        sed -i '/^\[Service\]/a EnvironmentFile=/etc/syxx/syxx.env' "$UNIT_FILE"
        echo "✅ EnvironmentFile added"
    fi
else
    echo "❌ Unit file $UNIT_FILE not found. The server may have been started manually."
    echo "Current server process:"
    ps aux | grep server.py | grep -v grep | head -1
    echo ""
    echo "You'll need to create the unit file or add SENTRY_DSN to the existing startup method."
fi

echo ""
echo "=== Step 4: Reload and restart ==="
if systemctl list-unit-files | grep -q syxx-server; then
    systemctl daemon-reload
    systemctl restart syxx-server.service
    echo "✅ syxx-server restarted"
    sleep 2
    if systemctl is-active --quiet syxx-server.service; then
        echo "✅ Service is running"
    else
        echo "⚠️  Service failed to start. Check: journalctl -u syxx-server -n 20 --no-pager"
    fi
else
    echo "⚠️  syxx-server is not a systemd unit. Server was likely started manually."
    echo "To restart manually after setting SENTRY_DSN:"
    echo "  1. Kill the old process: kill \$(pgrep -f server.py)"
    echo "  2. Start: cd /opt/SyxxServer && SENTRY_DSN='<your-dsn>' python3 server.py &"
fi

echo ""
echo "=== Step 5: Verify ==="
sleep 2
SERVER_PID=$(pgrep -f server.py | head -1)
if [ -n "$SERVER_PID" ]; then
    echo "Server PID: $SERVER_PID"
    DSN_IN_ENV=$(cat /proc/$SERVER_PID/environ 2>/dev/null | tr '\0' '\n' | grep SENTRY_DSN || echo "NOT FOUND in process env")
    if echo "$DSN_IN_ENV" | grep -q '^SENTRY_DSN=https'; then
        echo "✅ SENTRY_DSN is loaded in the server process environment"
    else
        echo "⚠️  SENTRY_DSN not found in server process environment"
        echo "   Process env: $DSN_IN_ENV"
    fi
else
    echo "❌ Server process not found"
fi

echo ""
echo "=== Done ==="
echo "Check Sentry.io dashboard for incoming events to confirm the connection is live."
