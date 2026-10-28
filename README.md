# Shelfie 📸📦

**Snap a part. Know what you have.**

Shelfie is a self-hosted inventory for electronic parts. Point your phone camera at a part, its bag or an AliExpress listing, and Shelfie identifies it with AI, checks whether you already own it, and adds it to your inventory, label included.

It exists to solve one very specific problem:

> You order a part on AliExpress, wait three weeks for it to arrive, and then find the same part in a box you already had.

---

## Features

- **Photo capture**: take a photo of a part, its packaging or a product page; an AI vision model extracts name, MPN, manufacturer, category and package.
- **"Do I already have it?"**: before ordering, share a screenshot or link and Shelfie searches your inventory: *"3 in Box 4."*
- **Proper electronics inventory**: built on [Part-DB](https://github.com/Part-DB/Part-DB-server), with categories, footprints, storage locations, stock lots, datasheets and parameters.
- **Labels on demand**: prints QR-code labels on a Brother P-touch P700 via a Raspberry Pi print server.
- **Private by default**: everything is reachable only over Tailscale; nothing is exposed to the public internet.
- **Portable**: one `docker compose` stack plus two data folders. Moving hosts takes an evening, not a weekend.

## Architecture

```
  📱 Phone (PWA)                      ☁️  Azure VM (docker compose)
  ┌─────────────┐   Tailscale     ┌──────────────────────────────────┐
  │ capture-web │ ─────────────▶  │ capture-api  (ASP.NET Core)      │
  └─────────────┘                 │   │                              │
                                  │   ├──▶ Part-DB (SQLite)          │
                                  │   └──▶ Azure OpenAI (vision)     │
                                  └───────────────┬──────────────────┘
                                                  │ Tailscale
                                                  ▼
                                  🏠 Raspberry Pi 3: print-server (FastAPI)
                                                  │ USB
                                                  ▼
                                           🖨️  Brother P-touch P700
```

### Capture flow

1. Take a photo in the PWA, which uploads it to `capture-api`.
2. `capture-api` sends the image to the vision model and receives structured JSON (name, MPN, manufacturer, category, package, confidence).
3. `capture-api` searches Part-DB for matches. If one exists, the app shows where it is.
4. Otherwise you review the suggested fields, confirm, and the part, stock lot and photo are created in Part-DB.
5. `capture-api` asks the Pi print server to print a QR label linking to the part.

## Components

| Component | Technology | Runs on |
|---|---|---|
| Inventory | Part-DB (official Docker image), SQLite | Azure VM |
| Capture backend | ASP.NET Core minimal API, OpenAI .NET SDK | Azure VM |
| Capture frontend | React + Vite PWA | served by the backend |
| Vision | Azure OpenAI multimodal model, JSON-schema structured output | Azure |
| Networking | Tailscale sidecar containers (HTTPS via `tailscale serve`) | Azure VM |
| Print server | Python FastAPI, Pillow, `segno`, `ptouch-print`; docker compose + Tailscale sidecar | Raspberry Pi 3 |
| Infrastructure | Terraform, cloud-init, Ubuntu 24.04 LTS | Azure |
| Backups | nightly `sqlite3 .backup` + uploads, rsync over Tailscale | → Synology |
| CI | GitHub Actions → GitHub Container Registry | GitHub |

## Repository layout

```
shelfie-parts/
├── infra/          # Terraform: VM, network security group, Azure OpenAI deployment
├── deploy/         # docker-compose.yml, Tailscale sidecar config, .env.example
├── capture-api/    # ASP.NET Core minimal API
├── capture-web/    # React + Vite PWA
├── print-server/   # FastAPI print service for the Pi (own compose stack + Tailscale sidecar)
├── vision/         # vision prompt, JSON schema, identify.py test CLI
├── scripts/        # backup.sh
└── docs/           # getting-started.md
```

**→ Setup: [docs/getting-started.md](docs/getting-started.md)**

## Design principles

- **Portability first.** Azure provides only a Linux VM and the vision model. Everything else is containers, with images published to GHCR, not tied to any cloud registry.
- **Swappable AI.** The vision provider sits behind an OpenAI-compatible client; switching providers is a configuration change.
- **No open ports.** The VM's network security group blocks all inbound traffic. Access, SSH included, goes through Tailscale.
- **Backups off-cloud.** Data is copied nightly to the home NAS, so losing the cloud subscription never means losing the inventory.
- **Nothing exposed at home.** No port forwarding; the print server is reachable only from the capture API, via Tailscale ACLs.

## Roadmap

- [x] Infrastructure: Terraform for the Azure VM and Azure OpenAI deployment
- [x] Part-DB behind a Tailscale sidecar (docker compose)
- [x] Vision prompt, JSON schema and `identify.py` test CLI
- [x] Vision prompt tested and tuned with real parts
- [x] Print server on the Raspberry Pi 3 (P700 via `ptouch-print`)
- [ ] Capture API: identify → search → create → print
- [ ] Capture PWA
- [ ] "Do I already have it?" check from screenshots and AliExpress links
- [x] Backup script (SQLite snapshot + uploads → rsync)
- [ ] Nightly backups to the Synology scheduled
- [ ] CI: build and publish images to GHCR

## Status

🚧 Foundations in place: infrastructure, Part-DB and the vision test CLI. Not yet deployed or tested against Azure.

## License

Not decided yet.
