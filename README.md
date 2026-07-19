# FroggePlugin

A [Dalamud](https://dalamud.dev/) plugin for FFXIV — an in-game companion client for
[Frogge](https://github.com/FroggeBot), talking to [FroggeAPI](https://github.com/FroggeBot/FroggeAPI)
over HTTP. Lets venue members interact with Frogge features (VIP status, events, ...) without
leaving the game.

This is no longer a bare scaffold: the pairing-code auth flow is built and working end-to-end, and
the window (`/frogge`) has real, live-verified features — VIP Status/History/Perks, Events browsing
with real self-signup, and read-only Profiles viewing. See
[`Docs/docs/getting-started/plugin.md`](../Docs/docs/getting-started/plugin.md) for the
feature-level walkthrough a venue member would see.

## Authentication

FroggeAPI's existing service-token scheme (`Schemas/src/schemas/servicetoken.py`,
`issue_service_token`/`verify_service_token`) is an HMAC secret shared only between the API, Bot,
and Worker server processes — all trusted, non-distributed code. A Dalamud plugin ships as a DLL on
every player's machine, so that secret is never embedded here.

Instead, the plugin uses a separate pairing-code flow scoped to one Discord user:

1. In Discord, the member runs `/plugin-link` (Bot) and gets a short-lived pairing code.
2. In-game, they open `/frogge` and enter that code into `MainWindow`'s link screen.
3. `FroggeApiClient.RedeemPairingCodeAsync` posts the code to `/plugin-auth/redeem`; the API
   verifies it and returns a token scoped to that one Discord user (and only the guild(s) they
   belong to), plus their Discord user id/username.
4. The token is stored in `Configuration.AuthToken` (with `LinkedDiscordUserId`/
   `LinkedDiscordUsername`) and set on every subsequent request via
   `FroggeApiClient.SetAuthToken` / the `Bearer` header. A "Forget" action calls
   `RevokeAsync` (`DELETE /plugin-auth/me`) and clears the stored token to unlink.

All guild-scoped plugin routes (`/plugin/vip/...`, `/plugin/events/...`, `/plugin/profiles/...`)
are authenticated this way — never with the API/Bot/Worker service-token secret.

## Project layout

- `FroggePlugin/Plugin.cs` — `IDalamudPlugin` entrypoint; constructs `FroggeApiClient` and
  `MainWindow`, registers the `/frogge` command and the window system.
- `FroggePlugin/Configuration.cs` — persisted plugin config: `ApiBaseUrl`, plus the pairing-flow
  state (`AuthToken`, `LinkedDiscordUserId`, `LinkedDiscordUsername`).
- `FroggePlugin/Windows/MainWindow.cs` (~1,280 lines) — the plugin's main ImGui window: the
  link/unlink screen, a shared style toolkit (colors, card/badge/button helpers), and every
  feature page (Home, VIP Status/History/Perks, Events/EventList/EventDetail with Join/Leave
  signup, Profiles/ProfileDetail).
- `FroggePlugin/Api/FroggeApiClient.cs` (~220 lines) — the authenticated HTTP client: pairing-code
  redemption/revocation, `/plugin-auth/me`, and the VIP/Events/Profiles read (and Events
  signup/leave) endpoints, all snake_case-JSON `record`-typed DTOs.

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

Not yet its own GitHub repo — still local under the `FroggeBot/` workspace directory. Auth
(pairing-code link/unlink), VIP Status/History/Perks, Events browsing with real self-signup, and
read-only Profiles viewing are all shipped and live-verified in-game. Not yet in the official
Dalamud plugin repository — still installed manually as a local dev plugin.
