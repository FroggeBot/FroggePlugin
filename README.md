# FroggePlugin

A [Dalamud](https://dalamud.dev/) plugin for FFXIV — an in-game companion client for
[FroggeBot](https://github.com/FroggeBot), talking to [FroggeAPI](https://github.com/FroggeBot/FroggeAPI)
over HTTP. Lets venue members interact with FroggeBot features (VIP status, events, ...) without
leaving the game.

This is currently a bare project scaffold: it builds, loads in Dalamud, and opens an empty window
via `/frogge`, but has no real features wired up yet.

## Open design question: authentication

FroggeAPI's existing service-token scheme (`Schemas/src/schemas/servicetoken.py`,
`issue_service_token`/`verify_service_token`) is an HMAC secret shared only between the API, Bot,
and Worker server processes — all trusted, non-distributed code. A Dalamud plugin ships as a DLL
on every player's machine, so that secret **must never** be embedded here: anyone could decompile
the plugin and mint a valid token for any guild.

Before this plugin can call any real (guild-scoped) FroggeAPI route, a separate, narrowly-scoped
per-character/per-user auth flow needs to be designed on the API side — e.g. the player links
their Discord identity via a short-lived pairing code issued by the Bot, and the API issues the
plugin a token scoped to that one Discord user (and only the guild(s) they belong to). Not
designed or built yet; see `FroggePlugin/Api/FroggeApiClient.cs` for where it plugs in.

## Project layout

- `FroggePlugin/Plugin.cs` — `IDalamudPlugin` entrypoint, registers the `/frogge` command and the
  window system.
- `FroggePlugin/Configuration.cs` — persisted plugin config (currently just `ApiBaseUrl`).
- `FroggePlugin/Windows/MainWindow.cs` — the plugin's main ImGui window (placeholder).
- `FroggePlugin/Api/FroggeApiClient.cs` — HTTP client stub pointed at FroggeAPI; unauthenticated
  for now (see above).

## Building

Requires the .NET SDK and a local Dalamud install (via [XIVLauncher](https://xivlauncher.app/)) —
`Dalamud.NET.Sdk` resolves the Dalamud assemblies automatically from the standard XIVLauncher addon
install path; set the `DALAMUD_HOME` environment variable if yours lives elsewhere.

```
dotnet build
```

To load it in-game for local dev, add this repo's `FroggePlugin/bin/x64/Debug` (or `Release`)
output directory as a "Dev Plugin Location" in the Dalamud plugin installer (`/xlplugins` → Dev
Plugins).

## Status

Not yet its own GitHub repo — scaffolded locally under the `FroggeBot/` workspace directory,
pending the auth design above and a decision on initial feature scope.
