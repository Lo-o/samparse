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
- `setup-vps.sh` — first-time hardening + base-package install (run once as root)
- `displayparagraph.service` — systemd unit
- `Caddyfile` — reverse-proxy config
- `deploy.sh` — local push-to-VPS script

---

## One-time server setup

### 1–4. Run `setup-vps.sh`

Add your SSH public key to root in the Hetzner Console (Server → SSH Keys),
then from your local machine:

```bash
scp deploy/setup-vps.sh root@<vps>:/tmp/
ssh root@<vps> "bash /tmp/setup-vps.sh"
```

That script handles, idempotently:

- `apt update && upgrade`
- Base packages (`rsync`, `curl`, `ufw`, `fail2ban`, `unattended-upgrades`, …)
- UFW firewall — default deny inbound, allow 22 / 80 / 443
- Fail2ban for SSH brute-force protection
- Unattended security upgrades
- Creating the **`deploy`** user with passwordless sudo and your SSH key
  (copied from root)
- Disabling root SSH and password auth (`/etc/ssh/sshd_config.d/99-hardening.conf`)
- Installing the **ASP.NET Core 10** runtime via Microsoft's apt repo
- Installing **Caddy**
- Creating the **`displayparagraph`** app user and the directories
  `/opt/displayparagraph`, `/var/lib/samparse`, `/var/log/caddy`

When it finishes, **log out, log back in as `deploy`** to verify SSH works
before continuing — if it doesn't, you can still recover via the Hetzner web
console (Server → Console).

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
