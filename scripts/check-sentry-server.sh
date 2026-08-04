#!/bin/bash
# check-sentry-server.sh — Diagnose why sentry-sdk isn't importable by the server
# Run as root: sudo bash check-sentry-server.sh

echo "=== 1. Which python3 is the server using? ==="
SERVER_PID=$(pgrep -f server.py | head -1)
if [ -z "$SERVER_PID" ]; then
    echo "❌ No server.py process found"
    exit 1
fi

echo "Server PID: $SERVER_PID"
echo "Process command:"
cat /proc/$SERVER_PID/cmdline | tr '\0' ' '
echo ""
echo ""

# Extract the python binary from the cmdline
PYTHON_BIN=$(cat /proc/$SERVER_PID/cmdline | tr '\0' '\n' | head -1)
echo "Python binary: $PYTHON_BIN"

echo ""
echo "=== 2. Is sentry-sdk installed for THIS python? ==="
$PYTHON_BIN -c 'import sentry_sdk; print("✅ sentry-sdk", sentry_sdk.VERSION, "installed")' 2>&1 || echo "❌ NOT installed for $PYTHON_BIN"

echo ""
echo "=== 3. Where is pip for this python? ==="
$PYTHON_BIN -m pip --version 2>&1 || echo "No pip module"

echo ""
echo "=== 4. All python3 binaries on the system ==="
which -a python3 2>/dev/null
ls -la /usr/bin/python3* 2>/dev/null
ls -la /usr/local/bin/python3* 2>/dev/null

echo ""
echo "=== 5. sentry-sdk installed under which python? ==="
for py in $(which -a python3 2>/dev/null) /usr/bin/python3 /usr/local/bin/python3; do
    if [ -x "$py" ]; then
        RESULT=$($py -c 'import sentry_sdk; print("✅", sentry_sdk.VERSION)' 2>&1)
        if echo "$RESULT" | grep -q '✅'; then
            echo "  $py: $RESULT"
        fi
    fi
done 2>/dev/null

echo ""
echo "=== 6. pipx list (if pipx installed) ==="
pipx list 2>/dev/null | grep -i sentry || echo "  No sentry in pipx list (or pipx not installed)"

echo ""
echo "=== 7. pip list for each python ==="
for py in $(which -a python3 2>/dev/null); do
    if [ -x "$py" ]; then
        echo "--- $py ---"
        $py -m pip list 2>/dev/null | grep -i sentry || echo "  (no sentry-sdk)"
    fi
done

echo ""
echo "=== 8. Server process environment ==="
cat /proc/$SERVER_PID/environ 2>/dev/null | tr '\0' '\n' | grep -E 'SENTRY|PYTHON|PATH' || echo "  No matching env vars"
