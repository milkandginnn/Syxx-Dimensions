# syntax=docker/dockerfile:1
# ──────────────────────────────────────────────────────────────────────────────
# Syxx Server — multi-stage container image
# Python 3.12 + uv, no pip. Non-root runtime. ~120 MB final image.
# ──────────────────────────────────────────────────────────────────────────────

# ── Stage 1: Builder ──────────────────────────────────────────────────────────
FROM python:3.12-slim AS builder

# Install uv into the builder stage (not carried to runtime).
COPY --from=ghcr.io/astral-sh/uv:latest /uv /uvx /bin/

WORKDIR /build

# Copy dependency manifests first for layer caching.
COPY pyproject.toml uv.lock ./

# Install all dependencies (including dev — we strip dev at copy time).
# --frozen: use uv.lock exactly as committed; never resolve fresh.
# --no-install-project: install deps only; the project source is copied later.
RUN uv sync --frozen --no-install-project --no-dev --no-cache

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM python:3.12-slim AS runtime

# Labels
LABEL maintainer="Syxx <feedback@milkandgin.com>"
LABEL org.opencontainers.image.title="Syxx Server"
LABEL org.opencontainers.image.description="DNS tampering detection — collector and API server"
LABEL org.opencontainers.image.source="https://github.com/milkandgin/Syxx"

# Runtime environment — unbuffered output for container log visibility.
ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONPATH=/app

WORKDIR /app

# Copy the installed virtualenv from builder (all deps, no dev tools).
COPY --from=builder /build/.venv /app/.venv

# Put the venv on PATH so `python` resolves to the venv interpreter.
ENV PATH="/app/.venv/bin:$PATH"

# Create non-root user before any file copies so ownership can be set inline.
RUN groupadd --system syxx && \
    useradd --system --gid syxx --home-dir /app --no-create-home syxx

# ── Copy application source (selective — no tests, docs, scripts, dev-keys) ──
# Python package
COPY syxx_server/ /app/syxx_server/

# Entry point
COPY server.py /app/server.py

# Static website files served by the HTTP router
COPY index.html /app/index.html
COPY leak-test.html /app/leak-test.html
COPY leaktest.html /app/leaktest.html
COPY privacy.html /app/privacy.html
COPY purchase-cancel.html /app/purchase-cancel.html
COPY purchase-success.html /app/purchase-success.html
COPY refund-policy.html /app/refund-policy.html
COPY terms.html /app/terms.html
COPY robots.txt /app/robots.txt
COPY sitemap.xml /app/sitemap.xml
COPY syxx-favicon.svg /app/syxx-favicon.svg

# Per-platform What's New / MOTD HTML
COPY whats-new-mac.html /app/whats-new-mac.html
COPY whats-new-win.html /app/whats-new-win.html
COPY whats-new-deb.html /app/whats-new-deb.html

# Asset directory (CSS, JS, images)
COPY assets/ /app/assets/

# Well-known files (security.txt, etc.)
COPY .well-known/ /app/.well-known/

# Broadcast template (server reads broadcast.json at runtime; this is the seed)
COPY broadcast.json.example /app/broadcast.json.example

# ── Data directories (SQLite DB, backups) — owned by syxx ────────────────────
RUN mkdir -p /app/data /app/data/backups && \
    chown -R syxx:syxx /app/data

# ── Runtime configuration ─────────────────────────────────────────────────────
# In Docker the server MUST bind 0.0.0.0 (the default 127.0.0.1 is unreachable
# from outside the container). Override via docker-compose for custom values.
ENV SYXX_HTTP_HOST=0.0.0.0 \
    SYXX_HTTP_PORT=8080 \
    SYXX_DNS_PORT=5353 \
    SYXX_DNS_FALLBACK_PORT=5053 \
    SYXX_DB_PATH=/app/data/syxx-server.db \
    SYXX_DB_BACKUP_DIR=/app/data/backups \
    SYXX_STATIC_DIR=/app \
    SYXX_WHATSNEW_DIR=/app \
    SYXX_ENV=production

# Expose HTTP and DNS ports.
EXPOSE 8080 5353/udp

# Volume for persistent SQLite database and backups.
VOLUME ["/app/data"]

# Health check — /healthz is process-liveness-only (no DB/DNS dependency).
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8080/healthz')" || exit 1

# Run as non-root.
USER syxx

# Launch the server.
CMD ["python", "server.py"]
