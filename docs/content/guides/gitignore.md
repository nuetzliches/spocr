---
title: Git Ignore Recommendations
description: Practical .gitignore guidance for SpocR repositories and the sample web API.
---

# .gitignore Recommendations

This guide lists pragmatic .gitignore entries for working with SpocR and the sample `web-api`.

## Repository root

- Build artifacts
  - `bin/`
  - `obj/`
  - `.vs/`

- Local tools and caches
  - `.ai/.cache/`
  - `debug/`
  - `pull.log`

- Snapshots and temp
  - Keep SpocR snapshots (e.g., `.spocr/schema/*.json`) if they are part of your workflow. Otherwise, ignore with care:
    - `.spocr/cache/` (if present)

## Sample `web-api`

- Build outputs
  - `samples/web-api/bin/`
  - `samples/web-api/obj/`

- User secrets and local settings (if used)
  - `samples/web-api/appsettings.Development.local.json`

## Docs application (Nuxt)

- Node modules and build outputs
  - `docs/node_modules/`
  - `docs/.nuxt/`
  - `docs/.output/`
  - `docs/.cache/`

- Local content database
  - `docs/.data/`

> Tip: Treat generated SpocR DataContext files as source (commit them) when you want a stable CI without DB access. If you prefer fully reproducible builds from the database at CI time, you may ignore `DataContext/` in your app projects — ensure your pipeline runs `spocr pull` + `spocr build` first.

