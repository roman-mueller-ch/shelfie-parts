# CLAUDE.md

Context for AI coding assistants working on this repo. Keep it current: update **Status** and **Decisions** when they change.

## What this is

**Shelfie** is a self-hosted inventory for electronic parts (mostly AliExpress orders). Take a photo of a part, its bag or a product page; a vision model identifies it, Shelfie checks whether it's already in stock, adds it to the inventory and prints a QR label.

The problem it solves: ordering a part, waiting weeks, then finding the same part in a box at home.

See `README.md` for architecture and `docs/getting-started.md` for setup.

## Architecture (decided)

- **Inventory:** Part-DB (official `jbtronics/part-db1` image), SQLite, in docker compose.
- **Host:** small Azure VM (Ubuntu 24.04, `Standard_B1ms`, Switzerland North), paid from Roman's $50/month Visual Studio Professional credits.
- **Vision:** Azure OpenAI `gpt-5-mini` (version `2025-08-07`, GlobalStandard; retires 2027-02-09), called through the OpenAI-compatible v1 API so the provider is swappable.
- **Capture app (not built yet):** ASP.NET Core minimal API (`capture-api/`) + React/Vite PWA (`capture-web/`).
- **Networking:** Tailscale everywhere. Host has Tailscale SSH; each service has its own Tailscale sidecar (`parts.<tailnet>.ts.net`). The Azure NSG has no inbound rules.
- **Labels:** Brother P-touch P700 on a Raspberry Pi 3 at home (`homelab`), driven by `ptouch-print`; a small FastAPI print server (`print-server/`, not built yet) reachable only over Tailscale.
- **Backups:** `scripts/backup.sh` (SQLite `.backup` + uploads) → rsync over Tailscale to a Synology DS218+.

## Decisions and why

- **Portability first.** Azure only provides a VM and the model. Everything else is a compose stack + `deploy/data/`. Images go to GHCR, not ACR. The capture API must support plain API keys, not only managed identity.
- **Not on the Synology:** Docker on the DS218+ (2 GB RAM, runs Plex) performed badly before.
- **Not on the Pi 5:** it runs Umbrel with a Bitcoin node; don't mix.
- **Visual Studio credits** are licensed for dev/test, have a spending limit (subscription suspends when exhausted) and end if Roman's job changes. Hence: off-Azure backups, no Marketplace or third-party-billed services (Claude via Foundry would not be covered), keep it movable.
- **Part-DB base currency:** CHF (cannot change after first prices).

## Status (2026-10-27)

- [x] Repo, README, Terraform (`infra/`), compose stack (`deploy/`), vision prompt/schema/CLI (`vision/`), backup script, getting-started guide
- [x] On Roman's Mac: `terraform init`, `terraform validate`, `terraform plan` succeed
- [x] `terraform apply` (2026-10-27): VM up, cloud-init done, `shelfie-vm` on the tailnet; Azure OpenAI `oai-shelfie-xv86jc` with `gpt-5-mini` deployed
- [x] Compose stack deployed to `/srv/shelfie` on the VM (2026-10-27); Part-DB at `https://parts.tailf98c37.ts.net`
- [ ] **Next:** test `vision/identify.py` (docs/getting-started.md §4)
- [ ] Tune `vision/prompt.md` with real parts
- [ ] Print server on the Pi 3
- [ ] Capture API, then PWA

Tailscale prep is done (MagicDNS, HTTPS certificates, two pre-approved one-time auth keys).

## Environment and gotchas

- Roman works on a **MacBook Air** (also has Arch Linux and Windows machines). Repo is at `~/Projects/shelfie-parts`.
- **Turn ProtonVPN off** for anything that downloads from HashiCorp (`brew install`, `terraform init`); with it on, TLS connections to `releases.hashicorp.com` fail or hang.
- Terraform comes from `hashicorp/tap` (not in Homebrew core anymore). OpenTofu would also work.
- `terraform init` can hang silently on the large azurerm download; Ctrl+C may not react (use `pkill -9 terraform`, then `rm -rf .terraform .terraform.lock.hcl` and retry).
- SSH to the Pi: `ssh homelab` (Tailscale SSH enabled). The VM will be `shelfie-vm` on the tailnet.
- Secrets live only in git-ignored files: `infra/terraform.tfvars`, `deploy/.env`, `vision/.env`. Never commit them.

## Conventions

- **Git:** Roman is the commit author; Claude is credited with a `Co-Authored-By` trailer. Roman's commits are SSH-signed. Commit `516a939` is intentionally left as is; do not rewrite pushed history or force-push `main`.
- **Terraform:** azurerm `~> 5.11` (v5: `resource_provider_registrations = "legacy"` is set on purpose for the fresh subscription).
- **Stack:** .NET for the backend, React for the frontend, Python for small tools on the Pi and the vision CLI (`uv run`, PEP 723 inline deps).
- Prefer small, working increments; each roadmap step should be usable on its own.
