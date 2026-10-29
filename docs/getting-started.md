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

On the VM, once the Synology is on your tailnet with rsync enabled:

```bash
chmod +x /srv/shelfie/scripts/backup.sh
export SHELFIE_BACKUP_TARGET=roman@synology:/volume1/backup/shelfie
/srv/shelfie/scripts/backup.sh          # test once by hand
crontab -e
# 15 3 * * * SHELFIE_BACKUP_TARGET=roman@synology:/volume1/backup/shelfie /srv/shelfie/scripts/backup.sh >> /srv/shelfie/backup.log 2>&1
```

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

Update after a new image: `docker compose pull capture-api && docker compose up -d capture-api`.

For local development (`dotnet run` in `capture-api/`), the secrets go into `dotnet user-secrets`: `Vision:BaseUrl`, `Vision:ApiKey`, `PartDb:ApiToken`.

| Endpoint | |
|---|---|
| `POST /api/identify` | multipart `images` (1–4) + optional `hint` → identification + matching parts in stock |
| `POST /api/parts` | multipart `part` (JSON) + optional `photos` → creates the part with stock lot, parameters, photos |
| `POST /api/parts/{id}/stock` | JSON `{quantity, storage_location_path?}` → adds stock to an existing part (its lot in that location, or a new lot: `new_lot: true`) |
| `POST /api/parts/{id}/label` | prints the QR label; optional JSON `{title, subtitle, copies}` |
| `GET /api/parts/{id}/label.png` | label preview |
| `GET /api/storage-locations` | all storage locations (id, name, full path) |
| `POST /api/storage-locations` | JSON `{path}`, e.g. `["Helmer", "D2", "D2-A1"]` → the location, created with missing parents if needed |
| `POST /api/storage-locations/{id}/label` | prints a bin label (QR to the parts in it + bin name); optional JSON as for parts |
| `GET /api/storage-locations/{id}/label.png` | bin label preview |

## Moving away from Azure

1. Run `scripts/backup.sh` one last time.
2. On the new host: install Docker, copy the repo to `/srv/shelfie`, restore `deploy/data/partdb/` from the backup (and `deploy/data/tailscale/` to keep the same tailnet names).
3. `docker compose up -d`.
4. Point `SHELFIE_AI_BASE_URL` / `SHELFIE_AI_API_KEY` / `SHELFIE_AI_MODEL` at any other OpenAI-compatible provider.
