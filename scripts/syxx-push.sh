#!/usr/bin/env bash
# Push staged Windows source into the VM over SSH (key auth, no password).
# The VM sits on UTM's shared subnet 192.168.64.x, reachable directly from the Mac.
# Usage: syxx-push [vm-ip]
#   No arg -> auto-finds the VM (a 192.168.64.x host with port 22 open).
set -euo pipefail
USER_NAME="${SYXX_VM_USER:-Syxx}"
SRC="${SRC:-/Users/masterrabbit/Desktop/Shared/Syxx-Windows}"
DEST="${DEST:-C:/Users/${USER_NAME}/Syxx-Windows}"
IP="${1:-${SYXX_VM_HOST:-}}"

if [ -z "$IP" ]; then
  echo ">>> auto-detecting VM on 192.168.64.x ..."
  for i in $(seq 2 40); do ping -c1 -W 300 "192.168.64.$i" >/dev/null 2>&1 & done; wait
  for cand in $(arp -an | grep -oE "192\.168\.64\.[0-9]+" | grep -vE "192\.168\.64\.1$" | sort -u); do
    if nc -z -G 2 "$cand" 22 2>/dev/null; then IP="$cand"; echo "    found ssh at $cand"; break; fi
  done
fi
if [ -z "$IP" ]; then
  echo "No VM with SSH open on 192.168.64.x. In the VM: start sshd + allow inbound TCP 22,"
  echo "then retry. Or pass the IP explicitly: syxx-push <ip>  (ipconfig in the VM shows it)."
  exit 2
fi
[ -d "$SRC" ] || { echo "source $SRC missing -- run syxx-stage first"; exit 1; }
echo ">>> pushing $SRC  ->  ${USER_NAME}@${IP}:${DEST}"
scp -r -o StrictHostKeyChecking=accept-new "$SRC" "${USER_NAME}@${IP}:${DEST}"
echo ">>> done. Open ${DEST}\\Windows\\Syxx.sln in Visual Studio."
