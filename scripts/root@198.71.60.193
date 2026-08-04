#!/bin/bash
# install-registrar-cert.sh
# Install SSL certificate from registrar for syxx.app and all subdomains
#
# Usage:
#   ./install-registrar-cert.sh <cert_file> <key_file> [ca_bundle_file]
#
# Example:
#   ./install-registrar-cert.sh syxx_app.crt syxx_app.key syxx_app.ca-bundle
#
# This script will:
# 1. Validate the certificate files
# 2. Install them to /etc/letsencrypt/live/syxx.app/
# 3. Update nginx configuration
# 4. Restart nginx
# 5. Verify the installation

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo -e "${RED}Error: This script must be run as root (use sudo)${NC}"
    exit 1
fi

# Check arguments
if [ $# -lt 2 ]; then
    echo -e "${RED}Error: Missing required arguments${NC}"
    echo "Usage: $0 <cert_file> <key_file> [ca_bundle_file]"
    echo ""
    echo "Example:"
    echo "  $0 syxx_app.crt syxx_app.key syxx_app.ca-bundle"
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

# Validate certificate
echo ""
echo "Validating certificate..."
CERT_DOMAIN=$(openssl x509 -in "$CERT_FILE" -noout -text 2>/dev/null | grep -E "DNS:|Subject:" | head -5)
if [ -z "$CERT_DOMAIN" ]; then
    echo -e "${RED}Error: Invalid certificate file${NC}"
    exit 1
fi

echo "Certificate covers:"
echo "$CERT_DOMAIN"
echo ""

# Check if certificate covers required domains
REQUIRED_DOMAINS=("syxx.app" "www.syxx.app" "macmotd.syxx.app" "winmotd.syxx.app" "debmotd.syxx.app" "leaktest.syxx.app" "leak.syxx.app")
CERT_TEXT=$(openssl x509 -in "$CERT_FILE" -noout -text 2>/dev/null)

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
    read -p "Continue anyway? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 1
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

# Create backup of current certificate
CERT_DIR="/etc/letsencrypt/live/syxx.app"
if [ -d "$CERT_DIR" ]; then
    echo ""
    echo "Creating backup of current certificate..."
    BACKUP_DIR="${CERT_DIR}.backup.$(date +%Y%m%d_%H%M%S)"
    cp -r "$CERT_DIR" "$BACKUP_DIR"
    echo -e "${GREEN}✓ Backup created: $BACKUP_DIR${NC}"
fi

# Install new certificate
echo ""
echo "Installing new certificate..."

# Create directory if it doesn't exist
mkdir -p "$CERT_DIR"

# Copy certificate files
cp "$CERT_FILE" "$CERT_DIR/fullchain.pem"
chmod 644 "$CERT_DIR/fullchain.pem"

cp "$KEY_FILE" "$CERT_DIR/privkey.pem"
chmod 600 "$CERT_DIR/privkey.pem"

# If CA bundle provided, concatenate it with the certificate
if [ -n "$CA_BUNDLE" ]; then
    cat "$CERT_FILE" "$CA_BUNDLE" > "$CERT_DIR/fullchain.pem"
    chmod 644 "$CERT_DIR/fullchain.pem"
    echo -e "${GREEN}✓ CA bundle concatenated with certificate${NC}"
fi

# Create symlinks (Let's Encrypt standard structure)
cd "$CERT_DIR"
ln -sf fullchain.pem cert.pem
ln -sf fullchain.pem chain.pem

echo -e "${GREEN}✓ Certificate files installed${NC}"

# Update nginx configuration
echo ""
echo "Updating nginx configuration..."
NGINX_CONF="/etc/nginx/sites-available/syxx.app"

if [ ! -f "$NGINX_CONF" ]; then
    echo -e "${RED}Error: Nginx configuration not found: $NGINX_CONF${NC}"
    exit 1
fi

# Create backup of nginx config
cp "$NGINX_CONF" "${NGINX_CONF}.backup.$(date +%Y%m%d_%H%M%S)"
echo -e "${GREEN}✓ Nginx configuration backed up${NC}"

# Update certificate paths in nginx config
sed -i "s|ssl_certificate .*|ssl_certificate $CERT_DIR/fullchain.pem;|" "$NGINX_CONF"
sed -i "s|ssl_certificate_key .*|ssl_certificate_key $CERT_DIR/privkey.pem;|" "$NGINX_CONF"

echo -e "${GREEN}✓ Nginx configuration updated${NC}"

# Test nginx configuration
echo ""
echo "Testing nginx configuration..."
if nginx -t; then
    echo -e "${GREEN}✓ Nginx configuration is valid${NC}"
else
    echo -e "${RED}Error: Nginx configuration test failed${NC}"
    echo "Restoring backup..."
    BACKUP=$(ls -t ${NGINX_CONF}.backup.* | head -1)
    cp "$BACKUP" "$NGINX_CONF"
    echo "Backup restored. Please check your certificate files and try again."
    exit 1
fi

# Restart nginx
echo ""
echo "Restarting nginx..."
systemctl restart nginx
echo -e "${GREEN}✓ Nginx restarted${NC}"

# Verify installation
echo ""
echo "Verifying certificate installation..."
sleep 2

INSTALLED_CERT=$(openssl s_client -servername syxx.app -connect syxx.app:443 2>/dev/null | openssl x509 -noout -subject -issuer 2>/dev/null)
if [ -z "$INSTALLED_CERT" ]; then
    echo -e "${YELLOW}Warning: Could not verify certificate (server may still be starting)${NC}"
    echo "Please check manually: openssl s_client -servername syxx.app -connect syxx.app:443"
else
    echo "Installed certificate:"
    echo "$INSTALLED_CERT"
    echo ""
    
    # Check if it covers leaktest.syxx.app
    if openssl s_client -servername leaktest.syxx.app -connect syxx.app:443 2>/dev/null | openssl x509 -noout -text 2>/dev/null | grep -q "leaktest.syxx.app"; then
        echo -e "${GREEN}✓ Certificate covers leaktest.syxx.app${NC}"
    else
        echo -e "${YELLOW}Warning: Certificate may not cover leaktest.syxx.app${NC}"
    fi
fi

echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Certificate installation complete!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo "Next steps:"
echo "1. Visit https://leaktest.syxx.app in your browser"
echo "2. Verify the certificate is trusted (no warning)"
echo "3. If you still see a warning, clear your browser cache and try again"
echo ""
echo "To verify manually:"
echo "  openssl s_client -servername leaktest.syxx.app -connect syxx.app:443 | openssl x509 -noout -text"
echo ""
