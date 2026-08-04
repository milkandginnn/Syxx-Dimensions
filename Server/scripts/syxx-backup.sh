#!/bin/sh
# Online, rotating backup of the Syxx SQLite DB (SD-56 / SHIP-v1 S7-1).
#
# Run by the syxx-backup.timer (see scripts/deploy.sh). Uses SQLite's online backup API via the
# package, which is SAFE on the live WAL database — no daemon stop, no `cp` (a cp of a WAL DB misses
# the -wal and yields a corrupt restore point). Reads SYXX_DB_PATH / SYXX_DB_BACKUP_DIR /
# SYXX_DB_BACKUP_KEEP from the same env the daemon uses.
#
# THIS IS LOCAL step one. A host loss still loses these backups with the DB — wire an off-host copy
# (rsync to another box / S3) of the backup dir as the follow-up; that target is an operator choice.
set -eu

DIR="${SYXX_SERVER_DIR:-/opt/SyxxServer}"
cd "$DIR"

# Load the daemon's secrets/paths if the env file exists (0600, root-only).
# E070: parse with grep/cut instead of shell-sourcing. The env file is in systemd
# EnvironmentFile format, where values containing $, backtick, <, >, ;, | or spaces are
# legal for systemd but executed/corrupted by shell `.` — a Stripe or Keygen secret with
# a shell metacharacter silently corrupted every backup run under the old sourcing approach.
if [ -r /etc/syxx/syxx.env ]; then
    SYXX_DB_PATH="${SYXX_DB_PATH:-$(grep '^SYXX_DB_PATH=' /etc/syxx/syxx.env | head -1 | cut -d= -f2-)}"
    SYXX_DB_BACKUP_DIR="${SYXX_DB_BACKUP_DIR:-$(grep '^SYXX_DB_BACKUP_DIR=' /etc/syxx/syxx.env | head -1 | cut -d= -f2-)}"
    SYXX_DB_BACKUP_KEEP="${SYXX_DB_BACKUP_KEEP:-$(grep '^SYXX_DB_BACKUP_KEEP=' /etc/syxx/syxx.env | head -1 | cut -d= -f2-)}"
fi

OUT="$(python3 -c 'from syxx_server import db; print(db.backup())')"
echo "syxx-backup: wrote $OUT"
