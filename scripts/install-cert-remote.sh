#!/bin/bash
# install-cert-remote.sh
# Upload and install SSL certificate on remote server
#
# Usage:
#   ./install-cert-remote.sh <cert_file> <key_file> [ca_bundle_file]
#   ./install-cert-remote.sh --dry-run <cert_file> <key_file> [ca_bundle_file]
#
# Examples:
#   ./install-cert-remote.sh domain.cert.pem private.key.pem
#   ./install-cert-remote.sh domain.cert.pem private.key.pem ca_bundle.pem
#   ./install-cert-remote.sh --dry-run domain.cert.pem private.key.pem

set -e

# Configuration
SERVER_USER="root"
SERVER_HOST="B6354A1"
SERVER_IP="198.71.60.193"
REMOTE_DIR="/opt/SyxxServer"
CERT_DIR="/etc/letsencrypt/live/syxx.app"

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

# Check arguments
DRY_RUN=false
if [ "$1" = "--dry-run" ]; then
    DRY_RUN=true
    shift
fi

if [ $# -lt 2 ]; then
    echo -e "${RED}Error: Missing certificate files${NC}"
    echo "Usage: $0 [--dry-run] <cert_file> <key_file> [ca_bundle_file]"
    echo ""
    echo "Examples:"
    echo "  $0 domain.cert.pem private.key.pem"
    echo "  $0 domain.cert.pem private.key.pem ca_bundle.pem"
    echo "  $0 --dry-run domain.cert.pem private.key.pem"
    exit 1
fi

CERT_FILE="$1"
KEY_FILE="$2"
CA_BUNDLE="${3:-}"

# Check if files exist
if [ ! -f "$CERT_FILE" ]; then
    echo -e "${RED}Error: Certificate file not found: $CERT_FILE${NC}"
    exit 1
fi

if [ ! -f "$KEY_FILE" ]; then
    echo -e "${RED}Error: Private key file not found: $KEY_FILE${NC}"
    exit 1
fi

if [ -n "$CA_BUNDLE" ] && [ ! -f "$CA_BUNDLE" ]; then
    echo -e "${RED}Error: CA bundle file not found: $CA_BUNDLE${NC}"
    exit 1
fi

echo -e "${GREEN}✓ All certificate files found${NC}"

# Validate certificate locally
echo ""
echo "Validating certificate..."
CERT_TEXT=$(openssl x509 -in "$CERT_FILE" -noout -text 2>/dev/null)
if [ -z "$CERT_TEXT" ]; then
    echo -e "${RED}Error: Invalid certificate file${NC}"
    exit 1
fi

echo "Certificate covers:"
echo "$CERT_TEXT" | grep -E "DNS:|Subject:" | head -10
echo ""

# Check if certificate covers required domains
REQUIRED_DOMAINS=("syxx.app" "www.syxx.app" "macmotd.syxx.app" "winmotd.syxx.app" "debmotd.syxx.app" "leaktest.syxx.app" "leak.syxx.app")
MISSING_DOMAINS=()

for domain in "${REQUIRED_DOMAINS[@]}"; do
    if ! echo "$CERT_TEXT" | grep -q "$domain"; then
        MISSING_DOMAINS+=("$domain")
    fi
done

if [ ${#MISSING_DOMAINS[@]} -gt 0 ]; then
    echo -e "${YELLOW}Warning: Certificate does not cover these domains:${NC}"
    printf '%s\n' "${MISSING_DOMAINS[@]}"
    echo ""
    if [ "$DRY_RUN" = false ]; then
        read -p "Continue anyway? (y/N): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo "Aborted."
            exit 1
        fi
    fi
else
    echo -e "${GREEN}✓ Certificate covers all required domains${NC}"
fi

# Validate private key matches certificate
echo ""
echo "Validating private key matches certificate..."
CERT_MOD=$(openssl x509 -in "$CERT_FILE" -noout -modulus 2>/dev/null | openssl md5)
KEY_MOD=$(openssl rsa -in "$KEY_FILE" -noout -modulus 2>/dev/null | openssl md5)

if [ "$CERT_MOD" != "$KEY_MOD" ]; then
    echo -e "${RED}Error: Private key does not match certificate${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Private key matches certificate${NC}"

# Build command chain
echo ""
echo "=========================================="
echo "Commands to execute:"
echo "=========================================="
echo ""

# Step 1: Upload files
echo "# Step 1: Upload certificate files to server"
UPLOAD_CMD="scp '$CERT_FILE' '$KEY_FILE'"
if [ -n "$CA_BUNDLE" ]; then
    UPLOAD_CMD="$UPLOAD_CMD '$CA_BUNDLE'"
fi
UPLOAD_CMD="$UPLOAD_CMD ${SERVER_USER}@${SERVER_IP}:${REMOTE_DIR}/"

if [ "$DRY_RUN" = true ]; then
    echo "$UPLOAD_CMD"
else
    echo "Uploading files..."
    eval "$UPLOAD_CMD"
    echo -e "${GREEN}✓ Files uploaded${NC}"
fi

echo ""

# Step 2: Install certificate
CERT_BASENAME=$(basename "$CERT_FILE")
KEY_BASENAME=$(basename "$KEY_FILE")

INSTALL_CMDS="sudo cp ${REMOTE_DIR}/${CERT_BASENAME} ${CERT_DIR}/fullchain.pem && "
INSTALL_CMDS+="sudo chmod 644 ${CERT_DIR}/fullchain.pem && "
INSTALL_CMDS+="sudo cp ${REMOTE_DIR}/${KEY_BASENAME} ${CERT_DIR}/privkey.pem && "
INSTALL_CMDS+="sudo chmod 600 ${CERT_DIR}/privkey.pem"

if [ -n "$CA_BUNDLE" ]; then
    CA_BASENAME=$(basename "$CA_BUNDLE")
    INSTALL_CMDS+=" && sudo cp ${REMOTE_DIR}/${CA_BASENAME} ${CERT_DIR}/chain.pem"
    INSTALL_CMDS+=" && sudo chmod 644 ${CERT_DIR}/chain.pem"
    INSTALL_CMDS+=" && sudo cat ${CERT_DIR}/fullchain.pem ${CERT_DIR}/chain.pem > ${CERT_DIR}/fullchain_with_ca.pem"
    INSTALL_CMDS+=" && sudo mv ${CERT_DIR}/fullchain_with_ca.pem ${CERT_DIR}/fullchain.pem"
fi

echo "# Step 2: Install certificate on server"
SSH_INSTALL="ssh ${SERVER_USER}@${SERVER_IP} '$INSTALL_CMDS'"

if [ "$DRY_RUN" = true ]; then
    echo "$SSH_INSTALL"
else
    echo "Installing certificate..."
    eval "$SSH_INSTALL"
    echo -e "${GREEN}✓ Certificate installed${NC}"
fi

echo ""

# Step 3: Restart nginx
echo "# Step 3: Restart nginx"
SSH_RESTART="ssh ${SERVER_USER}@${SERVER_IP} 'sudo systemctl restart nginx'"

if [ "$DRY_RUN" = true ]; then
    echo "$SSH_RESTART"
else
    echo "Restarting nginx..."
    eval "$SSH_RESTART"
    echo -e "${GREEN}✓ Nginx restarted${NC}"
fi

echo ""

# Step 4: Verify installation
echo "# Step 4: Verify certificate installation"
SSH_VERIFY="ssh ${SERVER_USER}@${SERVER_IP} 'sudo openssl x509 -in ${CERT_DIR}/fullchain.pem -noout -text | grep -A 1 \"Subject Alternative Name\"'"

if [ "$DRY_RUN" = true ]; then
    echo "$SSH_VERIFY"
else
    echo "Verifying certificate..."
    eval "$SSH_VERIFY"
fi

echo ""

# Step 5: Test HTTPS
echo "# Step 5: Test HTTPS connection"
SSH_TEST="ssh ${SERVER_USER}@${SERVER_IP} 'openssl s_client -servername leaktest.syxx.app -connect 127.0.0.1:443 </dev/null 2>&1 | grep -E \"subject=|issuer=\" | head -5'"

if [ "$DRY_RUN" = true ]; then
    echo "$SSH_TEST"
    echo ""
    echo "=========================================="
    echo "To execute all commands, run:"
    echo "=========================================="
    echo "$UPLOAD_CMD && $SSH_INSTALL && $SSH_RESTART && $SSH_VERIFY && $SSH_TEST"
else
    echo "Testing HTTPS..."
    eval "$SSH_TEST"
    echo ""
    echo -e "${GREEN}=========================================="
    echo "Certificate installation complete!"
    echo "==========================================${NC}"
    echo ""
    echo "Visit https://leaktest.syxx.app in your browser"
    echo "The certificate warning should be gone."
fi
