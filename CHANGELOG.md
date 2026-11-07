# Changelog

## v0.1.0 — 2026-11-07

First release.

- Sign in to Marquee with a code shown on the plugin's settings page.
- Mirrors the Marquee library as `.strm` files in Jellyfin's naming, with TMDB ids in folder names.
- Adds "Marquee Films" and "Marquee Shows" libraries on the first sync.
- Syncs every 15 minutes (adjustable), removes titles that left Marquee, follows MKV → MP4 conversions.
- Copies subtitles saved in Marquee beside each video.
- Sends Jellyfin watch progress to Marquee's Continue watching.
- Builds for Jellyfin 12 (net10.0) and Jellyfin 10.11 (net9.0).
