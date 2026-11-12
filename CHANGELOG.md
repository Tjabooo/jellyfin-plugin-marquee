# Changelog

## v0.2.0 — 2026-11-12

- The media app is now called Lilin, and so is the plugin: Jellyfin.Plugin.Lilin, its settings page and the
  `/c/jf/v1` keys (`liljf_…`). Remove the old plugin and install this one from the plugin repository, then
  sign in again with a code.

## v0.1.1 — 2026-11-07

- Fixed: saving the settings with the default folder typed out, or with a trailing slash, deleted the synced library (it was taken for a move to a new folder). Folders are now compared as folders.
- Moving the folder syncs into the new one straight away instead of at the next scheduled sync.

## v0.1.0 — 2026-11-07

First release.

- Sign in to Lilin with a code shown on the plugin's settings page.
- Mirrors the Lilin library as `.strm` files in Jellyfin's naming, with TMDB ids in folder names.
- Adds "Lilin Films" and "Lilin Shows" libraries on the first sync.
- Syncs every 15 minutes (adjustable), removes titles that left Lilin, follows MKV → MP4 conversions.
- Copies subtitles saved in Lilin beside each video.
- Sends Jellyfin watch progress to Lilin's Continue watching.
- Builds for Jellyfin 12 (net10.0) and Jellyfin 10.11 (net9.0).
