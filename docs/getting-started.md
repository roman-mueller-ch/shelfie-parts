# Getting started

From an empty Azure subscription to Part-DB on your tailnet and a working vision test, in about an hour.

## 0. Prerequisites (on the Mac)

```bash
brew install terraform azure-cli uv
az login
az account list -o table          # note the ID of the Visual Studio subscription
```

You also need a Tailscale account (the same tailnet as your other devices).

## 1. Prepare Tailscale

In the [Tailscale admin console](https://login.tailscale.com/admin):

1. **DNS → enable MagicDNS and HTTPS certificates.** The sidecars need both to serve `https://parts.<tailnet>.ts.net`.
2. **Settings → Keys → Generate auth key** twice, each *not reusable* and *pre-approved*:
   - one for the VM host (`shelfie-vm`), used by Terraform,
   - one for the `parts` sidecar, used by docker compose.
3. After the nodes appear, open each machine's menu and choose **Disable key expiry**, so they don't drop off the tailnet after 180 days.

## 2. Create the Azure resources

```bash
cd infra
cp terraform.tfvars.example terraform.tfvars   # fill in subscription ID, SSH key, VM auth key
terraform init
terraform apply
```

This creates a resource group with a small Ubuntu 24.04 VM, a network security group with no inbound rules, and an Azure OpenAI account with a `gpt-5-mini` deployment.

> **If the deployment fails with a quota error:** Visual Studio subscriptions sometimes start with low or zero quota for some models. Request quota in the Azure AI Foundry portal, or set `openai_model_name` / `openai_model_version` in `terraform.tfvars` to a model that has quota. Note that `gpt-5-mini` in Switzerland North is a *Global Standard* deployment, so requests may be processed in any Azure region.

cloud-init needs a few minutes after `apply`. The VM shows up as `shelfie-vm` in `tailscale status` when it's done:

```bash
ssh roman@shelfie-vm            # Tailscale SSH; port 22 is not reachable from the internet
cloud-init status --wait        # on the VM: should end with "status: done"
docker version
```

## 3. Deploy the stack

From the Mac, copy the repo to the VM (keeps local changes and needs no git on the VM; secrets and Terraform state stay behind):

```bash
rsync -a --exclude '.git' --exclude 'deploy/data' --exclude '.env' --exclude 'infra/.terraform' \
  --exclude 'terraform.tfvars' --exclude '*.tfstate*' --exclude 'bin' --exclude 'obj' --exclude 'vision/results' \
  ~/Projects/shelfie-parts/ roman@shelfie-vm:/srv/shelfie/
```

On the VM:

```bash
cd /srv/shelfie/deploy
cp .env.example .env
nano .env                        # TS_AUTHKEY_PARTS + PARTDB_APP_SECRET (openssl rand -hex 32)
docker compose up -d
docker compose ps                # ts-parts should be "healthy", partdb "running"

# First start only: create the database. Note the admin password it prints.
docker compose exec --user www-data partdb php bin/console doctrine:migrations:migrate
```

Open **https://parts.&lt;your-tailnet&gt;.ts.net** on any of your devices and log in as `admin`.

Before adding parts, check **Base currency** in `.env` (default CHF). It can't be changed once prices are stored.

## 4. Test part identification

On the Mac:

```bash
cd ~/Projects/shelfie-parts
cp vision/.env.example vision/.env
terraform -chdir=infra output openai_base_url        # → SHELFIE_AI_BASE_URL
terraform -chdir=infra output -raw openai_api_key    # → SHELFIE_AI_API_KEY

uv run vision/identify.py ~/Desktop/part.heic
uv run vision/identify.py bag.jpg closeup.jpg --hint "from my last AliExpress order" --save vision/results
```

`uv` installs the script's dependencies on first run. iPhone HEIC photos work directly. Try a mix: a dev board, a bag with a label, a strip of resistors, an AliExpress screenshot. If results are off, tune `vision/prompt.md`; the schema in `vision/schema.json` is what the capture API will rely on.

## 5. Backups

`scripts/backup.sh` writes a consistent SQLite copy plus uploads to `deploy/backups/` and copies it to the Synology over Tailscale. It runs nightly by cron and before every `scripts/update.sh`. The target comes from `SHELFIE_BACKUP_TARGET` in `deploy/.env` (or the environment).

The copy goes to the Synology's **rsync service with a dedicated DSM user**, not over SSH: DSM only allows SSH for administrators, and an admin SSH key on a cloud VM would give that VM full control of the NAS. The backup user can write to one shared folder and nothing else.

On the Synology (DSM, on the tailnet as `diskstation218`):

1. **Control Panel → File Services → rsync:** *Enable rsync service* **and** *Enable rsync account* on. Without rsync accounts, a login with a user name fails with `@ERROR: account system disabled`.
2. **Control Panel → User & Group → Create:** user `shelfie-backup`, a long random password, only in the group `users`. Permissions: **Read/Write on `homelab-backup`** (the backup shared folder), *No access* on all other shared folders. Applications: deny everything except **rsync** (allow).
3. **Edit rsync Account → Create:** `shelfie-backup` with the same password.
4. In the backup shared folder, create the folder `shelfie` (File Station). Check the restriction from the VM: an rsync to another shared folder (e.g. `rsync://shelfie-backup@…/home/`) must fail.

On the VM:

```bash
mkdir -p ~/.config/shelfie
read -rs PW && printf '%s\n' "$PW" > ~/.config/shelfie/rsync.pass && unset PW   # type the password, Enter
chmod 600 ~/.config/shelfie/rsync.pass
echo 'SHELFIE_BACKUP_TARGET=rsync://shelfie-backup@diskstation218.tailf98c37.ts.net/homelab-backup/shelfie' >> /srv/shelfie/deploy/.env
/srv/shelfie/scripts/backup.sh       # must end with "synced to …"
( crontab -l 2>/dev/null; echo '15 3 * * * /srv/shelfie/scripts/backup.sh >> /srv/shelfie/backup.log 2>&1' ) | crontab -
```

In the Tailscale admin console, *Disable key expiry* for the Synology, or it drops off the tailnet after about 6 months and the backups stop.

The VM keeps the newest `SHELFIE_BACKUP_KEEP` backups (default 7). The rsync adds to the Synology without deleting there; the NAS prunes on its own schedule (10 newest plus one per week for 6 months), shared with the homelab backups. A plain `user@host:/path` target (rsync over SSH) still works with a host that allows it.

## 6. Label printer (Raspberry Pi)

The print server runs as its own small compose stack on the Pi the Brother P-touch is plugged into, with a Tailscale sidecar at `https://labels.<tailnet>.ts.net`.

1. Plug the P700 in via USB and switch it on. The **P-Lite LED must be off** (hold the P-Lite button ~2 s); otherwise it shows up as a USB drive instead of a printer. `lsusb` should list `04f9:2061`.
2. Generate another Tailscale auth key (not reusable, pre-approved) for `labels`.
3. From the Mac:

   ```bash
   rsync -a --exclude data --exclude .env ~/Projects/shelfie-parts/print-server/ homelab:apps/shelfie-print/
   ```

4. On the Pi:

   ```bash
   cd ~/apps/shelfie-print
   cp .env.example .env && nano .env     # TS_AUTHKEY_LABELS
   docker compose up -d --build          # first build compiles ptouch-print, ~5–10 min on a Pi 3
   ```

5. Test from any tailnet device:

   ```bash
   curl https://labels.<tailnet>.ts.net/health
   curl -X POST https://labels.<tailnet>.ts.net/labels/preview -H 'content-type: application/json' \
     -d '{"title": "ESP32-C3 SuperMini", "subtitle": "Box 4", "url": "https://parts.<tailnet>.ts.net/"}' -o label.png
   curl -X POST https://labels.<tailnet>.ts.net/labels -H 'content-type: application/json' \
     -d '{"title": "Hello Shelfie", "url": "https://parts.<tailnet>.ts.net/"}'
   ```

   Interactive API docs: `https://labels.<tailnet>.ts.net/docs`.

## 7. Capture API

The capture API runs in the VM stack (`capture-api` + `ts-capture` sidecar) at `https://capture.<tailnet>.ts.net`. Its image is built by GitHub Actions on every push to `capture-api/` or the vision prompt/schema and published to `ghcr.io/roman-mueller-ch/shelfie-capture-api` (public, like the repo).

1. Part-DB: **System → Users → admin → Permissions → API**: set *Access API* and *Manage API tokens* to Allow (off by default). Then **User settings → API tokens**: create `capture-api` with access level *Edit*, no expiry.
2. Generate a Tailscale auth key for `capture` (not reusable, pre-approved).
3. Fill in the `# --- Capture API` block of `deploy/.env` on the VM (see `.env.example`), then `docker compose up -d`.
4. Test:

   ```bash
   curl https://capture.<tailnet>.ts.net/api/health
   curl -X POST https://capture.<tailnet>.ts.net/api/identify -F images=@part.jpg
   ```

Update after a new image: `docker compose pull capture-api && docker compose up -d capture-api` (in `/srv/shelfie/deploy`; everything else: §9).

For local development (`dotnet run` in `capture-api/`), the secrets go into `dotnet user-secrets`: `Vision:BaseUrl`, `Vision:ApiKey`, `PartDb:ApiToken`.

| Endpoint | |
|---|---|
| `POST /api/identify` | multipart `images` (1–4) + optional `hint` → identification + matching parts in stock (`same: true` = the same part, else only similar) |
| `POST /api/parts` | multipart `part` (JSON) + optional `photos` → creates the part with stock lot, parameters, photos (`new_bin: true` if nothing was in that location before) |
| `POST /api/parts/{id}/stock` | JSON `{quantity, storage_location_path?}` → adds stock to an existing part (its lot in that location, or a new lot: `new_lot: true`) |
| `POST /api/parts/{id}/label` | prints the QR label; optional JSON `{title, subtitle, copies}` |
| `GET /api/parts/{id}/label.png` | label preview |
| `GET /api/bins/suggest?category=…&category=…&bagged=true` | where a new part goes: drawer(s) from the category, the bin being filled (or first empty one), the empty bins |
| `GET /api/storage-locations` | all storage locations (id, name, full path) |
| `POST /api/storage-locations` | JSON `{path}`, e.g. `["Helmer", "D2", "D2-A1"]` → the location, created with missing parents if needed |
| `POST /api/storage-locations/{id}/label` | prints a bin label (QR to the parts in it + bin name); optional JSON as for parts |
| `GET /api/storage-locations/{id}/label.png` | bin label preview |

## 8. Public access (Cloudflare Tunnel + Access)

For use without Tailscale (e.g. on the iPhone with ProtonVPN on; iOS runs only one VPN), two services are also public on the domain `rmlabs.ch`:

| Address | Service | Tunnel | Reaches |
|---|---|---|---|
| **`https://parts.rmlabs.ch`** | capture app (Shelfie) | `shelfie`, container `cloudflared` | `127.0.0.1:8080` in `ts-capture`'s namespace |
| **`https://db.rmlabs.ch`** | Part-DB | `shelfie-partdb`, container `cloudflared-partdb` | `127.0.0.1:80` in `ts-parts`' namespace |

The print server stays tailnet-only; the capture API reaches it and Part-DB over the tailnet as before. Two tunnels because each `cloudflared` container shares one service's network namespace (like the Tailscale sidecars), so one container can't reach both cleanly.

No port opens on the VM: `cloudflared` connects out to Cloudflare. **Cloudflare Access** (GitHub login, only Roman) sits in front of both: the capture API has no login of its own (it holds the Part-DB edit token and the Azure OpenAI key), and Part-DB's own login page shouldn't face the internet directly. Part-DB still asks for its own login after Access. As a safety net the capture API answers 403 to any tunnel request that didn't go through Access.

**QR codes and links:** with `PARTDB_PUBLIC_URL=https://db.rmlabs.ch` in `deploy/.env`, new labels (part and bin) and the app's "open in Part-DB" links point to `db.rmlabs.ch`, so scanning works without Tailscale. Labels printed before keep the tailnet address. Part-DB answers to both names (`TRUSTED_HOSTS=^(parts|db)\.`).

In the Cloudflare dashboard (free plan; `rmlabs.ch` added as a site with Cloudflare's nameservers; Zero Trust team `roman-mueller`):

1. **GitHub login** (once). GitHub → Settings → Developer settings → OAuth Apps → *New OAuth App*: homepage `https://<team>.cloudflareaccess.com`, callback `https://<team>.cloudflareaccess.com/cdn-cgi/access/callback`. Copy client ID and a new client secret into Zero Trust → **Settings → Authentication → Login methods → Add → GitHub**. *Test* it and note the email it shows.
2. **Access applications first** (so a hostname is never open), one per service: Zero Trust → **Access → Applications → Add → Self-hosted**:
   - `Shelfie`: hostname `parts.rmlabs.ch`
   - `Part-DB`: hostname `db.rmlabs.ch`

   Both: session duration 1 month, login methods GitHub only with *Instant Auth*, policy `Roman`: action *Allow*, include *Emails* = the email from step 1 (the policy can be reused).
3. **Tunnels.** Zero Trust → **Networks → Tunnels → Create → Cloudflared**, one per service. Copy each token (only the `eyJ…` string after `--token`, no quotes) into `deploy/.env` on the VM:
   - `shelfie` → `CLOUDFLARE_TUNNEL_TOKEN`; public hostname `parts` . `rmlabs.ch`, service `HTTP` → `127.0.0.1:8080`
   - `shelfie-partdb` → `CLOUDFLARE_TUNNEL_TOKEN_PARTDB`; public hostname `db` . `rmlabs.ch`, service `HTTP` → `127.0.0.1:80`

   Plus `COMPOSE_PROFILES=public` and `PARTDB_PUBLIC_URL=https://db.rmlabs.ch`. The public hostnames create the DNS records. The install page's *Continue* stays disabled until the container is running.
4. Copy the repo to the VM (rsync from §3, excluding `deploy/data` and `deploy/.env`), then on the VM:

   ```bash
   cd /srv/shelfie/deploy
   docker compose pull && docker compose up -d
   docker compose logs cloudflared cloudflared-partdb --tail 5   # "Registered tunnel connection"
   ```

   "Provided Tunnel token is not valid" means extra characters around the token in `.env`.
5. Test without Tailscale: `https://parts.rmlabs.ch` → GitHub login → the app; `https://db.rmlabs.ch` → GitHub login → Part-DB login. A private window must not get past the GitHub login. Add the app to the iPhone home screen from Safari (delete the old icon first). If the login inside the home-screen app is fiddly, log in once in Safari first. If Part-DB's login redirects to `http://`, turn on *SSL/TLS → Edge Certificates → Always Use HTTPS* for `rmlabs.ch`.

To turn it off: delete the public hostname (or the tunnel) in the dashboard, or remove `COMPOSE_PROFILES=public` from `.env` and `docker compose up -d --remove-orphans`.

## 9. Updates

Part-DB is **pinned** (`jbtronics/part-db1:${PARTDB_VERSION:-2.20.5}`), so a `docker compose pull` for the capture API never updates it by accident. The capture API, Tailscale and cloudflared follow `:latest`.

On the VM (`scripts/update.sh`):

```bash
/srv/shelfie/scripts/update.sh --check     # running vs newest Part-DB version, changes nothing
/srv/shelfie/scripts/update.sh             # backup, pull, restart, check (Part-DB stays on its version)
/srv/shelfie/scripts/update.sh 2.21.0      # the same, moving Part-DB to 2.21.0
/srv/shelfie/scripts/update.sh --auto      # for cron: see below
```

Database migrations run when Part-DB starts (`DB_AUTOMIGRATE=true`, which also writes its own backup to `uploads/.automigration-backup`); the script makes a full backup first (`scripts/backup.sh`, off-site to the Synology) and checks afterwards that Part-DB and the capture API answer over the tailnet. Going back after a migration means restoring the database from `deploy/backups/`, not just the old image.

**Automatic, with ntfy** (like openGym on the homelab): `--auto` moves Part-DB to the newest release **within its major version** and reports to the homelab's ntfy server (topic `homelab`):

| Case | Message | Priority |
|---|---|---|
| Already newest | ✔️ *Part-DB is up to date* | min (silent) |
| Updated | ✅ *Part-DB updated to 2.21.0*, link to the release notes | default |
| New major version | 📦 *Part-DB 3.0.0 available*: not installed; read the notes, then `update.sh 3.0.0` by hand | default |
| Anything failed | ⚠️ *Part-DB update failed* with the step | high |

If the image for a new release isn't published yet, it stays on the old version and the next run retries. The nightly backup reports failures the same way.

Setup, once:

1. **On the homelab Pi** (ntfy's directory, e.g. `~/apps/ntfy`), a write-only user for the VM:

   ```bash
   docker compose exec ntfy ntfy user add shelfie-vm            # asks for a password (not needed later)
   docker compose exec ntfy ntfy access shelfie-vm homelab write-only
   docker compose exec ntfy ntfy token add shelfie-vm           # prints tk_…
   ```

2. **On the VM:**

   ```bash
   mkdir -p ~/.config/shelfie
   read -rs T && printf '%s\n' "$T" > ~/.config/shelfie/ntfy-token && unset T   # paste tk_…, Enter
   chmod 600 ~/.config/shelfie/ntfy-token
   printf 'NTFY_URL=https://ntfy.roman-mueller.ch\nNTFY_TOPIC=homelab\n' >> /srv/shelfie/deploy/.env
   /srv/shelfie/scripts/notify.sh "Hello from shelfie-vm" "ntfy works" default tada
   crontab -e
   ```

   Replace the backup line and add the update line (times in UTC: 03:15 and 09:05, i.e. 04:15 and 10:05 in Swiss winter time):

   ```
   15 3 * * * /srv/shelfie/scripts/backup.sh >> /srv/shelfie/backup.log 2>&1 || /srv/shelfie/scripts/notify.sh "Shelfie backup failed" "Nightly backup on shelfie-vm failed, see /srv/shelfie/backup.log" high warning
   5 9 * * *  /srv/shelfie/scripts/update.sh --auto >> /srv/shelfie/update.log 2>&1
   ```

To also hear about releases without the VM: GitHub → `Part-DB/Part-DB-server` → **Watch → Custom → Releases**.

Only the capture API: `cd /srv/shelfie/deploy && docker compose pull capture-api && docker compose up -d capture-api`.

## Moving away from Azure

1. Run `scripts/backup.sh` one last time.
2. On the new host: install Docker, copy the repo to `/srv/shelfie`, restore `deploy/data/partdb/` from the backup (and `deploy/data/tailscale/` to keep the same tailnet names).
3. `docker compose up -d`.
4. Point `SHELFIE_AI_BASE_URL` / `SHELFIE_AI_API_KEY` / `SHELFIE_AI_MODEL` at any other OpenAI-compatible provider.
