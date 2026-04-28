# Deploying DisplayParagraph to a VPS

Reference setup: **Hetzner CX22** (€3.79/mo, 2 vCPU, 4 GB RAM, EU) running
**Ubuntu 24.04 LTS**, with **Caddy** as auto-TLS reverse proxy and **systemd**
managing the app process. The app's working set is ~500 MB so 2 GB of RAM is
already comfortable; CX22's 4 GB gives plenty of margin for the OS, Caddy,
and page cache.

The SAM XML files (~200 MB for CHAPTERIV + REF, ~2.4 GB for the full dump)
live on the VPS in `/var/lib/samparse/`, **not** in the deploy artifact —
they update on a different cadence than the code.

All the deploy artifacts referenced below live in `deploy/`:
- `displayparagraph.service` — systemd unit
- `Caddyfile` — reverse-proxy config
- `deploy.sh` — local push-to-VPS script

---

## One-time server setup

Run the following on the VPS as root (or with `sudo`).

### 1. Base packages

```bash
apt update && apt upgrade -y
apt install -y rsync curl ufw
```

Open the firewall:
```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw enable
```

### 2. Install the ASP.NET Core 10 runtime

Microsoft's apt repo:
```bash
curl -sSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
apt update
apt install -y aspnetcore-runtime-10.0
```

Verify: `dotnet --list-runtimes` should show `Microsoft.AspNetCore.App 10.0.*`.

### 3. Install Caddy

```bash
apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
apt update
apt install -y caddy
```

### 4. Create the app user and directories

```bash
useradd --system --no-create-home --shell /usr/sbin/nologin displayparagraph
mkdir -p /opt/displayparagraph /var/lib/samparse /var/log/caddy
chown displayparagraph:displayparagraph /opt/displayparagraph
chown displayparagraph:displayparagraph /var/lib/samparse
```

### 5. Upload the SAM XML files

From your **local machine**:
```bash
rsync -avz --progress ./trial/SAM/ deploy@samparse.example.com:/var/lib/samparse/
```

(Replace `deploy@samparse.example.com` with your SSH user@host.) The app
currently only reads `CHAPTERIV-*.xml` and `REF-*.xml`, so you can save
bandwidth by uploading just those two if you want — the loader skips
missing files when their flag isn't requested.

### 6. Install the systemd unit

From your local machine, copy the unit file:
```bash
scp deploy/displayparagraph.service deploy@samparse.example.com:/tmp/
```

On the VPS:
```bash
mv /tmp/displayparagraph.service /etc/systemd/system/displayparagraph.service
systemctl daemon-reload
systemctl enable displayparagraph
```

(Don't `start` yet — the app isn't deployed.)

### 7. Configure Caddy

Edit `deploy/Caddyfile` locally, replacing `samparse.example.com` with your
real domain. Make sure the domain's DNS A record points at the VPS's public
IPv4 (and AAAA for IPv6 if applicable). Then:

```bash
scp deploy/Caddyfile deploy@samparse.example.com:/tmp/Caddyfile
```

On the VPS:
```bash
mv /tmp/Caddyfile /etc/caddy/Caddyfile
systemctl reload caddy
```

Caddy will provision a Let's Encrypt cert on the next request to your
domain. Watch the logs (`journalctl -u caddy -f`) the first time to
confirm.

### 8. First deploy

From your local machine:
```bash
export VPS_HOST=deploy@samparse.example.com
./deploy/deploy.sh
```

Then visit `https://samparse.example.com/paragraph-viewer`.

---

## Routine operations

### Deploying a new app version

```bash
./deploy/deploy.sh
```

The script publishes Release, rsyncs `publish/` to `/opt/displayparagraph/`,
and restarts the systemd service. Downtime is the time it takes to load
CHAPTERIV + REF (a handful of seconds).

### Updating the SAM dataset

When SAM publishes a new export:

```bash
rsync -avz --delete --progress ./trial/SAM/CHAPTERIV-*.xml ./trial/SAM/REF-*.xml \
    deploy@samparse.example.com:/var/lib/samparse/
ssh deploy@samparse.example.com "sudo systemctl restart displayparagraph"
```

The `--delete` removes any older `CHAPTERIV-*.xml` files from the previous
export so the loader picks the right one.

### Logs

- App logs: `journalctl -u displayparagraph -f`
- Caddy logs: `journalctl -u caddy -f` (or `/var/log/caddy/displayparagraph.log`)
- Service status: `systemctl status displayparagraph`

### Health check

The app eagerly loads SAM data during startup (`Program.cs` calls
`GetRequiredService<SamDataService>()` before `app.Run()`), so if
`systemctl status displayparagraph` reports `active (running)`, the data
loaded successfully. A failed load shows up immediately as a crash with
the exception in `journalctl`.

---

## Notes

- **Self-contained alternative.** If you'd rather not install the .NET
  runtime on the VPS, change `deploy.sh` to publish with
  `-r linux-x64 --self-contained` and adjust the systemd unit's
  `ExecStart` to `/opt/displayparagraph/DisplayParagraph` (no `dotnet`
  prefix). Artifact grows to ~80 MB.
- **Per-environment config.** `appsettings.Production.json` sets
  `SamDataPath` to `/var/lib/samparse`. Override it on the VPS via the
  systemd unit's `Environment=SamDataPath=...` if you need a different
  path without redeploying.
- **Scaling up.** If the app ever needs the full SAM dataset (AMP, RMB,
  etc.), expect the working set to grow to 8–12 GB and resize the VPS
  accordingly — or first consider a SAM XML → SQLite import so the data
  no longer needs to live in process memory.
