# Watch History Manager for Jellyfin

Watch History Manager is a Jellyfin plugin for better handling of episode watch history.

It is mainly built for situations where anime or series episodes have long outros, previews, credits, or endings. In these cases Jellyfin may keep the previous episode in "Continue Watching" even though the user already started the next episode.

The plugin adds two main features:

1. Automatically mark the previous episode as watched when the next episode starts and configurable conditions are met.
2. Add support for a "Set starting point" button through JavaScript Injector, so a series can be reset to a selected episode.

## Features

### Auto mark previous episode as watched

The plugin monitors episode playback events.

When a user starts the next episode of the same series and season, the plugin checks the previous episode. If the previous episode has been watched far enough, it is automatically marked as watched.

This is useful for anime such as Bleach, where the last minutes may contain outro, preview, credits, or other content that users often skip.

Configurable options:

1. Enable or disable automatic marking.
2. Minimum watched percentage.
3. Maximum remaining seconds.
4. Ignore specials and season 0.

The condition is evaluated as:

```text
Mark previous episode as watched if:
watched percentage >= configured minimum percentage
OR
remaining seconds <= configured maximum remaining seconds

## Installation

Requirements
Jellyfin Server 10.11.x.
.NET 9 SDK for building from source.
JavaScript Injector plugin for the optional "Set starting point" button in Jellyfin Web.

The automatic watch history feature works as a backend plugin feature.

The "Set starting point" button requires JavaScript Injector because normal Jellyfin backend plugins do not directly modify existing Jellyfin Web episode pages.

Installation through Jellyfin Plugin Repository

Add the plugin repository in Jellyfin:

Dashboard > Plugins > Repositories > Add

Repository URL:

https://raw.githubusercontent.com/philipkraeutl/watch-history-manager/main/manifest.json

Then install the plugin from:

Dashboard > Plugins > Catalog

After installation, restart Jellyfin.
```
