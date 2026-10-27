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

From the Mac, copy the repo to the VM (the repo is private, so this avoids setting up GitHub credentials there):

```bash
rsync -a --exclude '.git' --exclude 'deploy/data' --exclude '.env' \
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

## Moving away from Azure

1. Run `scripts/backup.sh` one last time.
2. On the new host: install Docker, copy the repo to `/srv/shelfie`, restore `deploy/data/partdb/` from the backup (and `deploy/data/tailscale/` to keep the same tailnet names).
3. `docker compose up -d`.
4. Point `SHELFIE_AI_BASE_URL` / `SHELFIE_AI_API_KEY` / `SHELFIE_AI_MODEL` at any other OpenAI-compatible provider.
