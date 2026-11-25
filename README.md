# Watch Tracker – Jellyfin plugin

Tracks who has started each TV show and season, and who has finished it. It flags viewers who gave up, and can delete seasons or whole shows once everyone who started them has finished.

Built for **Jellyfin 12.x** (the API it uses is identical in 10.11).

## Install from repository (recommended)

1. In Jellyfin, open Dashboard → Plugins → Repositories (or Catalog → gear icon), click **+**, and add:
   - Name: `Watch Tracker`
   - URL: `https://raw.githubusercontent.com/samuelcomplex/jellyfin-plugin-watchtracker/main/manifest.json`
2. In the Catalog, open **Watch Tracker**, click **Install**, then restart Jellyfin.

Jellyfin installs updates from the same place.

## Install (manual)

1. Stop the Jellyfin container (Portainer → Stop).
2. Copy the `Watch Tracker_1.0.0.0` folder from `WatchTracker-1.0.0-jellyfin10.11.zip` into Jellyfin's plugin folder:
   - Docker: `<your /config volume>/plugins/`
   - Windows install: `%ProgramData%\Jellyfin\Server\plugins\`
3. Start Jellyfin. **Watch Tracker** now appears in the dashboard side menu and under Dashboard → Plugins.

The plugin starts in **Dry run** mode. **Nothing is ever deleted unless Cleanup mode is set to Delete.** That applies to both the scheduled task and the **Delete eligible now** button, which stays greyed out in other modes, and the server refuses deletion requests too.

## How it decides

All status comes from Jellyfin's own played and resume data. Nothing is tracked separately, so existing watch history counts from day one.

| Per user, per season / show | Meaning |
|---|---|
| **Not started** | No episode played or partly played |
| **In progress** | Some episodes played, not all |
| **Finished** | Every episode present is marked played |

**Flags.** A viewer who is In progress and has made no progress for *N* days (default 30) is flagged:

- **Sampled**: they watched at most *X* episodes (default 3).
- **Stalled**: they got further than that, then stopped.
- **Mid-episode** (added to either flag): their last activity was stopping partway through an episode.

**A season can be cleaned up when:**

- at least one person started it, and
- everyone who started it has finished it. Flagged viewers count as "not finished" unless you untick *Flagged viewers still block cleanup*.
- the grace period (default 7 days) has passed since the last person finished,
- the show doesn't have the **keep** tag, and
- it isn't the newest season of a show that's still *Continuing* (so a season that's still airing isn't removed).

**A whole show** follows the same rules across all its seasons. By default, its status must also be *Ended*. Continuing shows only lose finished seasons.

Specials (season 0) are ignored unless enabled. They go when the whole show goes.

## Deleting

- **Cleanup mode** controls everything. In Off or Dry run, the server refuses every deletion request, whichever button or request asks for it.
- Each run deletes at most **Max GB deleted per run** (default 50 GB). Anything over the limit waits for the next run.
- **Delete eligible now** first shows the exact list with sizes and a total, and deletes nothing unless you confirm.
- Sizes are shown in the preview, the result and the History tab.

- A season with its own folder (`Show/Season 02/`) has the folder removed, including subtitles, nfo files and images.
- In a flat layout (all episodes directly in the show folder), only that season's episode files and their sidecar files are deleted.
- A whole show has its folder removed. As a safeguard, nothing is deleted if another show is found inside that folder.
- If **Sonarr** is configured, the deleted season (or, for a whole show, every existing season) is unmonitored first so it isn't downloaded again. The show itself stays monitored, so future seasons still download. If Sonarr can't be reached, nothing is deleted and the History tab shows "Failed".

## Where to find things

- **Progress tab**: every show, each viewer's status and flags, and why each season is or isn't ready. Has filters for ready, flagged, watching and untouched shows. *Preview cleanup* shows what would go and how big it is. *Delete eligible now* only works in Delete mode, and asks you to confirm the exact list first.
- **History tab**: every deletion, dry-run hit and failure.
- **Dashboard → Scheduled Tasks → Watch Tracker → Clean up finished shows**: runs daily at 04:00 by default. You can change the time there.
- Optional **flag tags**: `wt:stalled:Anna`, `wt:sampled:Bjorn`, `wt:mid-episode:…` and `wt:cleanup-ready` are written as tags on the show, so you can filter by them in any Jellyfin app.

## Building

Normal build (needs internet for NuGet):

```
dotnet build src/Jellyfin.Plugin.WatchTracker -c Release
```

Offline build against the bundled API stubs (`stubs/`, signatures copied from Jellyfin v10.11.11):

```
dotnet build src/Jellyfin.Plugin.WatchTracker -c Release -p:UseStubs=true
```

`tests/LogicTests` runs scripted viewing scenarios through the analyzer and the cleanup against temporary folders. `tests/SonarrTest` together with `tests/fake_sonarr.py` checks the Sonarr calls.
