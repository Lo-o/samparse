#!/usr/bin/env bash
# Build a Release publish artifact and push it to the VPS over tar+ssh.
#
# Usage:
#   VPS_HOST=deploy@samparse.example.com ./deploy/deploy.sh
#
# Or export VPS_HOST in your shell profile and just run ./deploy/deploy.sh.
#
# Uses tar+ssh rather than rsync so it works on any environment with bash,
# tar and ssh — including stock Git Bash on Windows. The /opt/displayparagraph
# dir is owned by the displayparagraph app user (set up by setup-vps.sh), so
# we briefly chown it to the deploy user during the upload, then restore.
#
# The SAM XML files are NOT shipped by this script — upload them out of band
# to /var/lib/samparse/ (see docs/deployment.md).

set -euo pipefail

VPS_HOST="${VPS_HOST:?Set VPS_HOST=user@host before running}"
APP_DIR="${APP_DIR:-/opt/displayparagraph}"
APP_USER="${APP_USER:-displayparagraph}"
SERVICE="${SERVICE:-displayparagraph}"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO_ROOT/DisplayParagraph/DisplayParagraph/DisplayParagraph.csproj"
PUBLISH_DIR="$REPO_ROOT/publish"

echo "==> Publishing $PROJECT (Release)…"
dotnet publish "$PROJECT" -c Release -o "$PUBLISH_DIR" --nologo

echo "==> Streaming publish to $VPS_HOST:$APP_DIR/…"
tar -czf - -C "$PUBLISH_DIR" . | ssh "$VPS_HOST" "
  set -e
  sudo chown -R \$(id -un):\$(id -gn) '$APP_DIR'
  find '$APP_DIR' -mindepth 1 -delete
  tar -xzf - -C '$APP_DIR'
  sudo chown -R '$APP_USER:$APP_USER' '$APP_DIR'
"

echo "==> Restarting $SERVICE…"
ssh "$VPS_HOST" "sudo systemctl restart '$SERVICE' && sudo systemctl status '$SERVICE' --no-pager | head -5"

echo "==> Done."
