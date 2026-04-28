#!/usr/bin/env bash
# Build a Release publish artifact and push it to the VPS.
#
# Usage:
#   VPS_HOST=deploy@samparse.example.com ./deploy/deploy.sh
#
# Or set VPS_HOST in your shell profile and just run ./deploy/deploy.sh.
# The SAM XML files are NOT shipped by this script — upload them once with
# rsync to /var/lib/samparse/ (see docs/deployment.md).

set -euo pipefail

VPS_HOST="${VPS_HOST:?Set VPS_HOST=user@host before running}"
APP_DIR="${APP_DIR:-/opt/displayparagraph}"
SERVICE="${SERVICE:-displayparagraph}"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO_ROOT/DisplayParagraph/DisplayParagraph/DisplayParagraph.csproj"
PUBLISH_DIR="$REPO_ROOT/publish"

echo "==> Publishing $PROJECT (Release)…"
dotnet publish "$PROJECT" -c Release -o "$PUBLISH_DIR" --nologo

echo "==> Syncing to $VPS_HOST:$APP_DIR/…"
rsync -avz --delete "$PUBLISH_DIR/" "$VPS_HOST:$APP_DIR/"

echo "==> Restarting $SERVICE…"
ssh "$VPS_HOST" "sudo systemctl restart $SERVICE && sudo systemctl status $SERVICE --no-pager"

echo "==> Done."
