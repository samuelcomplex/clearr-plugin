# Clearr for Jellyfin

[![Sync with Clearr and release](https://github.com/samuelcomplex/jellyfin-plugin-watchtracker/actions/workflows/release.yml/badge.svg)](https://github.com/samuelcomplex/jellyfin-plugin-watchtracker/actions/workflows/release.yml)
[![Compatibility check](https://github.com/samuelcomplex/jellyfin-plugin-watchtracker/actions/workflows/compatibility.yml/badge.svg)](https://github.com/samuelcomplex/jellyfin-plugin-watchtracker/actions/workflows/compatibility.yml)

![Clearr](https://raw.githubusercontent.com/samuelcomplex/clearr/main/docs/logo/banner.png)

**[Clearr](https://github.com/samuelcomplex/clearr) as a Jellyfin plugin.** It clears a show, season or movie once everyone who started watching it has finished: the same engine and the same UI as the Clearr app, running inside Jellyfin.

*Formerly **Watch Tracker**. It's the same plugin ID, so it installs as an update and brings your settings, keep list and history along.*

![Clearr](https://raw.githubusercontent.com/samuelcomplex/clearr/main/docs/screenshots/shows.png)

## App or plugin?

Both do the same thing. Pick one, and don't run both in Delete mode.

| | Clearr app (Docker) | Clearr plugin (this repo) |
|---|---|---|
| Runs | As its own container, next to Sonarr/Radarr | Inside Jellyfin |
| Reads progress | Jellyfin's API (needs an API key) | Jellyfin directly |
| Daily run | Built-in schedule | *Dashboard → Scheduled Tasks → Clearr* |
| Settings and UI | Its own web UI | *Dashboard → Clearr* (same UI) |
| Password, API key, HTTPS | Yes | Not needed (Jellyfin handles sign-in) |
| Removes | Through Sonarr/Radarr | Through Sonarr/Radarr, **plus an optional Jellyfin fallback** for items they don't manage |

Everything else is the same code: per-person progress, dropped-off viewers, Keep (with Sonarr/Radarr tags), grace period, newest-season protection, previews with sizes, the per-run size limit, the new-download guard and history. See the [Clearr README](https://github.com/samuelcomplex/clearr#readme) for how it decides.

## Install

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**, click **+** and add:
   `https://raw.githubusercontent.com/samuelcomplex/jellyfin-plugin-watchtracker/main/manifest.json`
2. Under **Catalog**, open **Clearr**, click **Install**, and restart Jellyfin.
3. Open **Clearr** in the dashboard menu. Connect **Sonarr** and/or **Radarr** under **Settings**, test them, and save.

It starts in **Dry run**. Nothing is ever removed unless *Cleanup mode* is **Delete**.

Works with **Jellyfin 10.11 and 12.x** from one package.

## The Jellyfin fallback (plugin only)

*Settings → Connections → "Also remove items Sonarr/Radarr don't manage, through Jellyfin"* is **off by default**.

When it's on, finished items that Sonarr/Radarr don't know about are deleted from disk by Jellyfin itself:
- a season's folder, if it has its own folder inside the show; otherwise just that season's episode files,
- a whole show's folder, never if another show is inside it,
- a movie's folder (or just its file, if it shares a folder).

Such items show a **via Jellyfin** label in the lists. They aren't unmonitored anywhere, so if another tool manages them they may come back.

## How it stays in sync with Clearr

This repo contains only the Jellyfin-specific parts:
- the library reader,
- the fallback deleter,
- the dashboard page frame,
- the scheduled task,
- the API that the shared UI talks to.

Everything else comes from Clearr itself. [`external/clearr`](external) is a git submodule of the Clearr repo, and the plugin compiles Clearr's shared `Clearr.Core` library directly. The UI is Clearr's own, served from that library.

The [**Sync with Clearr and release**](.github/workflows/release.yml) workflow checks Clearr every 6 hours. When Clearr has new commits, it:
1. pulls them,
2. runs the plugin tests,
3. builds against the official Jellyfin 10.11 packages,
4. packages the result as `2.<Clearr version>.<n>`,
5. adds it to `manifest.json`.

Jellyfin then offers it as an update. Every release's changelog links to the exact Clearr commit it was built from.

The weekly [**compatibility check**](.github/workflows/compatibility.yml) builds against Jellyfin 10.11, the newest 12.x and the newest release overall (plus pre-releases as an early warning). It then installs the plugin into **real Jellyfin containers** (10.11.11 and latest) and checks that it loads, its API answers, the delete guard holds and the scheduled task is registered. Clearr's own repo tests the shared engine end to end against real Sonarr and Radarr. Failures open a GitHub issue.

## Building

```
git clone --recursive https://github.com/samuelcomplex/jellyfin-plugin-watchtracker
dotnet build src/Jellyfin.Plugin.Clearr -c Release                    # official Jellyfin 10.11 packages
dotnet run --project tests/PluginTests -p:UseStubs=true               # offline tests against the stubs in stubs/
```
