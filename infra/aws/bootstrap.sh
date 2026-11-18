#!/usr/bin/env bash
set -euo pipefail
cd /opt/luma
chmod 700 /opt/luma/runtime
mkdir -p status keys runtime/dev runtime/production
chmod 700 runtime/dev runtime/production
for environment in dev production; do
  docker network inspect "luma-${environment}-front" >/dev/null 2>&1 || docker network create "luma-${environment}-front"
done
aws secretsmanager get-secret-value --secret-id /luma/edge/runtime --region us-east-2 --query SecretString --output text |
  python3 -c 'import json,sys,pathlib; p=pathlib.Path("/opt/luma/cloudflare.token");p.write_text(json.load(sys.stdin)["token"]);p.chmod(0o600)'
docker compose -p luma-edge -f infra/aws/gateway.json up -d
cat >/etc/systemd/system/luma-deploy.service <<'UNIT'
[Unit]
Description=Verify and install signed Luma releases
After=docker.service network-online.target
Requires=docker.service
[Service]
Type=oneshot
ExecStart=/usr/bin/python3 /opt/luma/infra/aws/deploy.py
User=root
UMask=0077
UNIT
cat >/etc/systemd/system/luma-deploy.timer <<'UNIT'
[Unit]
Description=Check Luma deployment channels
[Timer]
OnBootSec=30
OnUnitInactiveSec=60
Persistent=true
[Install]
WantedBy=timers.target
UNIT
cat >/etc/systemd/system/luma-backup.service <<'UNIT'
[Unit]
Description=Back up Luma databases to private encrypted storage
After=docker.service network-online.target
[Service]
Type=oneshot
ExecStart=/usr/bin/python3 /opt/luma/infra/aws/deploy.py backup
User=root
UMask=0077
UNIT
cat >/etc/systemd/system/luma-backup.timer <<'UNIT'
[Unit]
Description=Daily Luma backup
[Timer]
OnCalendar=*-*-* 06:00:00 UTC
Persistent=true
[Install]
WantedBy=timers.target
UNIT
systemctl daemon-reload
systemctl enable --now luma-deploy.timer luma-backup.timer
