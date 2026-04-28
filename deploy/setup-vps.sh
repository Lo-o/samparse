#!/usr/bin/env bash
# Idempotent first-time VPS setup for DisplayParagraph.
# Run once as root on a fresh Ubuntu 24.04 Hetzner VPS:
#
#   scp deploy/setup-vps.sh root@<vps>:/tmp/
#   ssh root@<vps> "bash /tmp/setup-vps.sh"
#
# Prerequisite: your SSH public key must already be in /root/.ssh/authorized_keys
# (Hetzner does this automatically if you select an SSH key at provision time).
# The script refuses to proceed otherwise — disabling root SSH without a working
# key for the deploy user would lock you out.
#
# After this completes:
#   - A 'deploy' user with passwordless sudo and your SSH key
#   - Root SSH and password auth disabled
#   - UFW firewall (22 / 80 / 443), fail2ban for SSH, unattended security upgrades
#   - ASP.NET Core 10 runtime + Caddy installed
#   - App user 'displayparagraph' and directories /opt/displayparagraph,
#     /var/lib/samparse, /var/log/caddy
#
# Then log out, log in as the deploy user, and continue with docs/deployment.md
# from step 5 (upload SAM data).
#
# Re-running is safe: every step is guarded or overwrite-safe.

set -euo pipefail

DEPLOY_USER="${DEPLOY_USER:-deploy}"
APP_USER="displayparagraph"
APP_DIR="/opt/displayparagraph"
DATA_DIR="/var/lib/samparse"
LOG_DIR="/var/log/caddy"

if [[ "$EUID" -ne 0 ]]; then
    echo "ERROR: run as root (sudo bash $0)." >&2
    exit 1
fi

if [[ ! -s /root/.ssh/authorized_keys ]]; then
    echo "ERROR: /root/.ssh/authorized_keys is empty or missing." >&2
    echo "  Add your SSH public key to root first (Hetzner Console → server → SSH keys)" >&2
    echo "  or this script will lock you out when it disables root SSH." >&2
    exit 1
fi

export DEBIAN_FRONTEND=noninteractive

echo "==> Updating packages…"
apt-get update -q
apt-get upgrade -yq

echo "==> Installing base packages…"
apt-get install -yq \
    rsync curl ufw fail2ban unattended-upgrades \
    debian-keyring debian-archive-keyring apt-transport-https gpg ca-certificates

echo "==> Enabling unattended security upgrades…"
cat >/etc/apt/apt.conf.d/20auto-upgrades <<EOF
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF

echo "==> Configuring UFW (allow SSH / 80 / 443, default deny inbound)…"
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

echo "==> Configuring fail2ban for SSH…"
cat >/etc/fail2ban/jail.d/sshd.local <<EOF
[sshd]
enabled  = true
maxretry = 5
findtime = 10m
bantime  = 1h
EOF
systemctl enable --now fail2ban
systemctl restart fail2ban

echo "==> Creating deploy user '$DEPLOY_USER'…"
if ! id -u "$DEPLOY_USER" >/dev/null 2>&1; then
    adduser --disabled-password --gecos "" "$DEPLOY_USER"
fi
usermod -aG sudo "$DEPLOY_USER"

install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" /home/"$DEPLOY_USER"/.ssh
install -m 600 -o "$DEPLOY_USER" -g "$DEPLOY_USER" \
    /root/.ssh/authorized_keys /home/"$DEPLOY_USER"/.ssh/authorized_keys

# Passwordless sudo so deploy.sh can `sudo systemctl restart` without a TTY.
# The deploy user has --disabled-password, so this is the only way they can sudo
# at all. Threat model: SSH key compromise already grants full VPS access, so
# a sudo password adds no meaningful protection here.
cat >/etc/sudoers.d/"$DEPLOY_USER" <<EOF
$DEPLOY_USER ALL=(ALL) NOPASSWD:ALL
EOF
chmod 440 /etc/sudoers.d/"$DEPLOY_USER"

echo "==> Hardening SSH (disable root login + password auth)…"
cat >/etc/ssh/sshd_config.d/99-hardening.conf <<EOF
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
EOF
sshd -t                            # validate before reload
systemctl reload ssh

echo "==> Installing ASP.NET Core 10 runtime…"
if [[ ! -f /etc/apt/sources.list.d/microsoft-prod.list ]]; then
    curl -sSL "https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb" -o /tmp/ms-prod.deb
    dpkg -i /tmp/ms-prod.deb
    rm -f /tmp/ms-prod.deb
    apt-get update -q
fi
apt-get install -yq aspnetcore-runtime-10.0

echo "==> Installing Caddy…"
if [[ ! -f /etc/apt/sources.list.d/caddy-stable.list ]]; then
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
        | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
        > /etc/apt/sources.list.d/caddy-stable.list
    apt-get update -q
fi
apt-get install -yq caddy

echo "==> Creating app user and directories…"
if ! id -u "$APP_USER" >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "$APP_USER"
fi
install -d -m 755 -o "$APP_USER" -g "$APP_USER" "$APP_DIR" "$DATA_DIR"
install -d -m 755 -o caddy -g caddy "$LOG_DIR"

cat <<EOF

============================================================
VPS setup complete.

Next steps (from your local machine):

  1. Verify SSH as the deploy user works:
       ssh $DEPLOY_USER@<this-vps>

  2. (Only after #1 succeeds) Continue with docs/deployment.md
     starting from step 5 — upload SAM data, install the systemd
     unit, configure Caddy, and run deploy.sh.
============================================================
EOF
