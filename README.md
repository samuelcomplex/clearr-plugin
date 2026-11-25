# Watch Tracker – Jellyfin plugin

Tracks who has started each TV show and season, and who has finished it. It flags viewers who gave up, and can delete seasons or whole shows once everyone who started them has finished.

## Compatibility

| Jellyfin | Status |
|---|---|
| **12.x** (latest; tested on 12.4) | ✅ Supported, in daily use |
| **10.11.x** | ✅ Supported. The API calls the plugin makes are identical to 12.x |
| 10.10 and older | ❌ Not supported |

One package works on both 10.11 and 12.x.

## Features

- **Per-user progress** for every show and season: *Not started*, *In progress* or *Finished*. It uses Jellyfin's own watched and resume data, so existing watch history counts from day one.
- **Flags** for viewers who went quiet: **Sampled** (watched only a few episodes), **Stalled** (got partway, then stopped) and **Mid-episode** (stopped in the middle of an episode).
- **Automatic cleanup** of seasons, or whole shows, once everyone who started them has finished.
- **Keep** a show or season with one click, so it's never deleted.
- **Safety first**: it starts in Dry run mode, shows sizes before anything is deleted, and has a per-run size limit and a full history log.
- **Sonarr integration**: deleted seasons are unmonitored so they aren't downloaded again, while future seasons keep coming.

## Install

### From the plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories** and click **+**.
2. Add the repository:
   - Name: `Watch Tracker`
   - URL: `https://raw.githubusercontent.com/samuelcomplex/jellyfin-plugin-watchtracker/main/manifest.json`
3. Go to the **Catalog**, open **Watch Tracker**, click **Install**, then restart Jellyfin.

Updates then show up in the Catalog like any other plugin.

### Manually

1. Download the newest `watchtracker_x.y.z.zip` from the [`dist`](dist) folder.
2. Create a folder named `Watch Tracker` in Jellyfin's plugin folder and unzip the file into it:
   - Docker: `<your /config volume>/data/plugins/` (some images use `<config>/plugins/`)
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\`
3. Restart Jellyfin.

After installing, **Watch Tracker** appears in the dashboard side menu.

## Safety

The plugin starts in **Dry run** mode. **Nothing is ever deleted unless *Cleanup mode* is set to *Delete*.**

- **Cleanup mode** applies to everything: the daily scheduled task and the *Delete eligible now* button. In *Off* or *Dry run*, the button is hidden and the server refuses every deletion request.
- **Preview cleanup** lists what would be deleted, with the size of each item and a total.
- **Delete eligible now** (Delete mode only) shows that same list and total first, and deletes nothing unless you confirm.
- **Max GB deleted per run** (default 50 GB) limits how much a single run can delete. Anything over the limit waits for the next run.
- **History** records every deletion, dry-run hit and failure, with sizes.

Recommended: leave it in Dry run for a week or so, check the History tab and mark anything you want to hold onto as **Keep**. Then switch to Delete.

## How it decides

### Status per viewer

| Status | Meaning |
|---|---|
| **Not started** | No episode played or partly played |
| **In progress** | Some episodes played, not all |
| **Finished** | Every episode in the library is marked played |

### Flags

A viewer who is *In progress* and has made no progress for **30 days** (configurable) is flagged:

- **Sampled**: they watched **3 episodes or fewer** (configurable).
- **Stalled**: they got further than that, then stopped.
- **Mid-episode**: added to either flag when their last activity was stopping partway through an episode.

### When a season can be removed

All of these must be true:

- at least one person has started it
- everyone who started it has finished it. Flagged viewers count as "not finished" unless you untick *Flagged viewers still block cleanup*.
- the **grace period** (default 7 days) has passed since the last person finished
- neither the season nor its show is marked **Keep**
- it isn't the **newest season of a show that's still Continuing**, so a season that's still airing is never removed

### When a whole show can be removed

The same rules apply across all its seasons. In addition, by default:

- the show's status must be **Ended**. Continuing shows only lose finished seasons.
- none of its seasons is marked Keep. The other seasons can still be removed one by one.

Specials (season 0) are ignored unless you enable them. They are removed when the whole show is.

## Keeping shows and seasons

Click **Keep** next to a show or a season on the Progress tab, and it is never deleted. Click **Kept ✓** to undo.

- The plugin adds the `keep` tag in Jellyfin, so you can see and filter by it there.
- It also stores the keep itself, so the protection survives a "Replace all metadata" refresh that wipes tags.
- Adding the tag yourself in Jellyfin (*Edit metadata → Tags*) works too.

## What gets deleted

- **Season with its own folder** (`Show/Season 02/`): the folder is removed, including subtitles, nfo files and images.
- **Flat layout** (all episodes directly in the show folder): only that season's episode files and their sidecar files are deleted.
- **Whole show**: the show folder is removed. As a safeguard, nothing is deleted if another show is found inside that folder.

Jellyfin deletes permanently; there is no recycle bin.

## Sonarr

If you enter your Sonarr URL and API key in Settings, the plugin unmonitors what it is about to delete before deleting it:

- **A season**: that season is unmonitored.
- **A whole show**: every existing season is unmonitored. The show itself **stays monitored**, so future seasons still download, as long as the show's *Monitor New Seasons* setting in Sonarr is *All*.
- If Sonarr can't be reached, **nothing is deleted** and the History tab shows *Failed*.

*Test Sonarr* checks the URL and key as typed. You don't need to save first. The URL works with or without `http://`.

## Settings reference

| Setting | Default | What it does |
|---|---|---|
| Cleanup mode | Dry run | Off / Dry run / Delete. Applies to the scheduled task and the button |
| Max GB deleted per run | 50 | 0 = no limit |
| Delete finished seasons | On | |
| Delete whole shows when finished | On | |
| Only delete whole shows that have Ended | On | |
| Never delete the newest season of a continuing show | On | |
| Count specials (season 0) | Off | |
| Grace period (days) | 7 | Days to wait after the last viewer finishes |
| Keep tag | `keep` | Tag that protects a show or season |
| Flag after days without progress | 30 | |
| "Sampled" episode limit | 3 | |
| Flagged viewers still block cleanup | On | |
| Write flags as Jellyfin tags | Off | Adds tags like `wt:stalled:Anna` and `wt:cleanup-ready` to shows |
| Ignored users | none | For example a test or kids account |
| Sonarr URL / API key | empty | Optional, see above |

## Where to find things

- **Progress tab**: every show with each viewer's status and flags, and the reason each season is or isn't ready. Filters: ready, flagged, watching, untouched and kept.
- **Settings tab**: everything in the table above.
- **History tab**: what was deleted (or would have been), when, how big it was, and why.
- **Dashboard → Scheduled Tasks → Watch Tracker → Clean up finished shows**: runs daily at 04:00. You can change the time there.

## Building

Normal build against the official Jellyfin packages (needs internet access to NuGet):

```
dotnet build src/Jellyfin.Plugin.WatchTracker -c Release
```

Offline build against the bundled API stubs in `stubs/`. Their signatures are copied from Jellyfin 10.11.11 and checked against 12.4:

```
dotnet build src/Jellyfin.Plugin.WatchTracker -c Release -p:UseStubs=true
```

Tests:

- `tests/LogicTests` runs scripted viewing histories through the analyzer, keep handling and cleanup, against temporary folders.
- `tests/SonarrTest`, together with `tests/fake_sonarr.py`, checks the Sonarr calls.
