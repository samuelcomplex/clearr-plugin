# Changelog

Clearr for Jellyfin is built from [Clearr](https://github.com/samuelcomplex/clearr). Its version is `2.<Clearr minor>.<Clearr patch>.<build>`, so 2.2.0.x is built from Clearr 0.2.0. Changes to the rules, safety and UI are in [Clearr's changelog](https://github.com/samuelcomplex/clearr/blob/main/CHANGELOG.md); this file lists what's specific to the plugin.

Every release in Jellyfin's catalog also names the exact Clearr commit it was built from.

## 2.2.0 – with Clearr 0.2.0

- Renamed from **Watch Tracker** to **Clearr**. Same plugin ID: it installs as an update, and settings, the keep list and history move over automatically.
- The same engine and UI as the Clearr app, kept in sync automatically.
- Optional removal **through Jellyfin** for items Sonarr/Radarr don't manage (off by default).
- Two scheduled tasks, **Clear finished shows** and **Clear finished movies**, each following its own cleanup mode and with its own schedule in Jellyfin. Each only deletes in Delete mode, and the mode is checked again before every item.
- English and Swedish, following each Jellyfin user's own display language (English for languages Clearr doesn't have yet).
- Settings notices (an upgrade, a damaged file) show at the top of the plugin page.
- Jellyfin favourites are kept automatically (series, seasons and movies; *Keep favourites*, on by default).
- Backup and restore of settings and the keep list; restoring keeps the plugin's own Jellyfin connection.
- Whole Sonarr/Radarr root folders can be kept from Settings.
- Notifications to ntfy, Discord or a webhook after each run, in the server's display language, and `/Clearr/summary` for a Home Assistant sensor.
- Works with Jellyfin 10.11 and 12.x from one package; tested weekly against real Jellyfin containers.

## 1.x – Watch Tracker

The original plugin: per-user progress, dropped-off viewer flags, Sonarr unmonitoring, Keep per show and season.
