# AGENTS.md

ASP.NET Core 10 server for osu!stable clients in g0v0 v2 (Bancho login and packet polling, presence, spectating, chat, and legacy score/beatmap web endpoints). The solution has two projects: `g0v0.Server.Stable/` (web host) and `g0v0.Server.Stable.Tests/` (NUnit 4).

## Sibling-repo dependency (read first)

`g0v0.Server.Stable/g0v0.Server.Stable.csproj` has a `ProjectReference` to `..\..\g0v0.Server.Common\g0v0.Server.Common\g0v0.Server.Common.csproj`. Restore and build require `GooGuTeam/g0v0.Server.Common` checked out as a sibling directory:

```text
<parent>/
  g0v0.Server.Common/      # https://github.com/GooGuTeam/g0v0.Server.Common
  g0v0.Server.Stable/      # this repo
```

If `dotnet restore`/`build` fails with a missing project path, check this layout first. CI mirrors it in `.github/workflows/ci.yml`.

`Common` carries the EF Core models and repositories, `ConfigManager`, `IPathProvider`, GeoIP, Redis/cache and storage registration, beatmap fetching, rulesets, and the shared `ScoreProcessingService`/`ScoreProcessingQueue`. It also supplies the `osu.Game.*` dependencies and legacy serialization helpers. Check `Common` before adding a local implementation of shared infrastructure; its `AGENTS.md` explains repository registration and the MySQL/PostgreSQL backends. Database schema and repository changes belong there.

## Commands

Run from the Stable repo root (the directory containing `g0v0.Server.Stable.sln`):

- Restore: `dotnet restore`
- Build: `dotnet build`
- Test all: `dotnet test`
- Single fixture: `dotnet test --filter "FullyQualifiedName~LoginRouteHandlerTests"`
- Run server: `dotnet run --project g0v0.Server.Stable/g0v0.Server.Stable.csproj`
- Format check (CI gate): `dotnet format --verify-no-changes --verbosity diagnostic`
- Apply formatting: `dotnet format`
- CI-equivalent build (after restore): `dotnet build --no-restore --configuration Release /warnaserror '/p:WarningsNotAsErrors=NU1902%3BNU1903'`
- Install InspectCode: `dotnet tool install --global JetBrains.ReSharper.GlobalTools --version 2026.2.1`
- InspectCode (after a Release build): `jb inspectcode g0v0.Server.Stable.sln --no-build --no-updates --severity=WARNING --swea --output=artifacts/inspectcode.sarif` (create `artifacts/` first).
- CI test command (after a Release build): `dotnet test --no-build --configuration Release --verbosity normal --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage"`

CI has three jobs: `build-warnaserror` (format verification and an analyzer build), `inspectcode` (warning-level analysis with solution-wide analysis enabled), and `test` (NUnit plus coverage). Format drift, code/analyzer warnings, InspectCode warnings/errors, or failing tests fail CI. The build keeps `NU1902`/`NU1903` NuGet audit notices visible without promoting them to errors; preserve that distinction when reproducing CI.

`global.json` pins SDK `10.0.0` (`rollForward: latestMinor`, no prereleases). `Directory.Build.props` injects StyleCop.Analyzers, Meziantou.Analyzer, Roslynator, `EnforceCodeStyleInBuild`, and XML documentation generation into every project. It pins the built-in analyzer rule set to `9.0-recommended`; do not duplicate the shared analyzer references in `.csproj` files.

## Runtime configuration (not in source control)

`ConfigManager` reads JSON under `{ContentRoot}/config/`, normally `g0v0.Server.Stable/config/` when running the web project. `.gitignore` excludes that directory, `g0v0.Server.Stable/storage/`, the repo-root `geoip/`, and `artifacts/`.

- `general.json` — database selection/credentials, Redis, fetcher credentials, and public URLs. `UseLegacyDatabase: true` selects the MySQL v1 schema; `false` selects PostgreSQL v2. `WebUrl` derives from `FrontendUrl` (falling back to `ServerUrl`) and supplies verification/profile/chart links.
- `game.json` — gameplay feature flags, including RX/AP mode mapping used for submissions and leaderboards.
- `storage.json` — replay storage (`Local`, `S3`, or `R2`), using Common's `StorageConfiguration`.
- `verification.json` — login device verification. `EnableSessionVerification` and `DeviceTrustDurationDays` control whether a Stable session may enter the player registry and how long its device remains trusted.
- Optional `geoip.json` — GeoIP database location and refresh settings. `AddGeoIP()` uses defaults when it is absent and registers both `IGeoIPService` and `TimeProvider`.
- Optional `chat.json` — `ChatConfig`; Stable uses `MessageLengthLimit` and falls back to defaults when the file is absent.
- Optional `registration.json` — `RegistrationConfig`; `AllowInGameRegistration` and `DisallowedUsernames`. Absent means in-game registration is allowed and only the lazer API's `banned_name` defaults are refused.

Configuration uses **Newtonsoft.Json**. The default file name is the snake_case type name plus `.json`; `[ConfigFile("...")]` overrides it (all files listed above have explicit names). `Get<T>()` requires the file to exist; `TryGet<T>()` supports optional files. Only properties marked `[Reloadable]` change on `Reload<T>()`. Use the config classes in `Common` as the schema reference.

`RulesetManager` is constructed with `{ContentRoot}/rulesets`. Replay paths are resolved through `IStorageService` and the content-root-based `WebPathProvider`.

## Code style (non-default)

Enforced by `.editorconfig`, `.globalconfig`, `stylecop.json`, and `Directory.Build.props`:

- File header on every `.cs`: `// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.` followed by a blank line.
- `using` directives **outside** the namespace, `System.*` first, no blank lines between groups; file-scoped namespaces; LF; 4 spaces; no trailing newline in C# files.
- SA1124 (regions) and SA1309 (`_underscore` fields) are intentionally disabled; preserve existing conventions.
- XML doc comments are required on public/exposed members (`documentExposedElements: true`). Tests suppress `SA0001;SA1600;SA1602;CS1591;CS0618;MA0048`; do not add boilerplate documentation to tests just to satisfy production rules.
- `IDE0005` (unused usings) and `IDE0055` (formatting) are warnings, promoted to errors by CI's `/warnaserror` build.

## Architecture

Entrypoint: `g0v0.Server.Stable/Program.cs`. `ConfigureServices` registers `IPathProvider` and `ConfigManager` before Common's repository, GeoIP, Redis (`ServerIdentify.Stable`), cache, fetcher, and storage extensions. The database context follows `GeneralConfig.UseLegacyDatabase`.

`IPlayerManager`, `IRoomManager`, `IChatServer`, `RulesetManager`, `StableScoreSubmissionTracker`, and the hosted `ScoreProcessingQueue` are singletons. Route handlers, `StableWebAuthenticator`, `IPacketFacade`, and the score processor are scoped. `PacketFacade` supplies the player/room/chat managers, rulesets, and `IServiceScopeFactory` to packet readers and handlers. Singleton services create scopes for repository access; keep request-scoped database services out of stored session state.

`ConfigureApp` enables forwarded headers and HTTPS redirection, then maps:

- `GET /healthz` — service health response.
- `POST /users` — `RegisterRouteHandler`, in-game account creation.
- `POST /` — `LoginRouteHandler`, the unframed login handshake.
- `POST /packet` — `PacketRouteHandler`, authenticated binary packet polling.
- `POST /web/osu-submit-modular-selector.php` — `SubmitScoreRouteHandler`.
- `POST /web/osu-getbeatmapinfo.php` — `BeatmapInfoRouteHandler`.
- `GET /web/osu-osz2-getscores.php` — `LeaderboardRouteHandler`.
- `GET /web/osu-getfriends.php` — `FriendsRouteHandler`.

### Registration

- `RegisterRouteHandler` serves the form-urlencoded `user[username]`/`user[user_email]`/`user[password]` body the osu! clients post to `/users`. The rules live in Common's `UserCredentialValidator` so other servers apply the same policy: 2-15 characters for the username, a dotted-domain email, and a password of 8-32 characters with more than three distinct characters built from at least two character classes. Do not reimplement them here.
- The endpoint answers `ok` (`text/plain`) or the `{"form_error":{"user":{...}}}` document the clients attach to the matching input. Do not replace it with a plain status code: the client reports nothing useful for an unparseable body. Uniqueness, the disabled switch, and any other rule produce that shape; the disabled switch is checked before validation so a closed endpoint reveals nothing.
- `check` non-zero only validates the submission; the client uses it while the player is still typing, so it must not create the account.
- Hashing and verification both go through Common's `UserPasswordHasher`, which covers the **hex MD5** of the password rather than the plain text, because `StableWebAuthenticator` verifies the MD5 the login handshake sends. A hash of the plain text makes the new account unable to log in (`PlainPasswordBcryptCannotBeVerifiedUsingAStableMd5` covers the same trap). Do not call `BCrypt.Net.BCrypt` here.
- The account and its per-mode statistics rows are written by `IUserRepository.CreateAsync(user, modes, ...)` as one transaction. `UserStatisticsDefaults.GetModes` seeds 0-3 plus the relax/autopilot modes when `game.json` enables them; seeding separately would leave an account without statistics, and a retry would then hit "username already taken". A `DbUpdateException` from a lost unique-index race is reported as a generic server error.

### Login, sessions & packet polling

- `LoginPacket.Parse` reads the three-line UTF-8 login body (username, password MD5, pipe-delimited client/device fields). Login and polling require the exact `User-Agent: osu!`; other agents receive HTTP 422.
- `StableWebAuthenticator` is shared by login and web handlers. It validates a 32-character hexadecimal password MD5, lowercases it, and verifies it against the stored bcrypt hash through `ILoginRepository`. Bots are rejected as disabled accounts. Preserve the detailed failure result used to choose login failure tokens.
- Login records the GeoIP-enriched audit and stable device/session state through `ILoginRepository`. An unverified device receives `cho-token: verification-required`, a `RequiresVerification` user-ID packet without a server notification; the client handles the verification prompt, and the session never enters `IPlayerManager`. Tournament clients require supporter status. A non-tournament session that polled within the last ten seconds blocks another regular login.
- Successful login returns `application/octet-stream` and a `cho-token` header. The initial packets include protocol version 19, user ID, privileges, friends, silence state, the user's presence/stats, and the channel directory. Always append `PacketChannelInfoEnd`, even for an empty directory: the client waits in "Receiving data..." without it. Queued presence events follow the initial response packets.
- Polling reads the `osu-token` header and resolves the exact session by token. Missing/unknown sessions get a notification and immediate `PacketRestart` in a binary response. Packets are handled in wire order, handler responses join the player's outgoing queue, and the queue is drained into the response. An empty poll still drains pending events.
- Restricted sessions may send only packets registered with `allowRestrict: true`. Exceptions from an individual handler are logged and skipped so the remaining poll can complete; `OperationCanceledException` propagates. The dispatcher rechecks session identity between packets so a logged-out or replaced session cannot continue handling the batch.

### Binary protocol & packet registration

- `Packets/Client/` implements `IClientPacket.Handle(IPlayer, IPacketFacade)`; `Packets/Server/` contains outgoing packet types. `[OsuPacket(id, ...)]` declares the wire ID and behavior. The attribute scans the Stable assembly once into `OsuPacketAttribute.Packets`; duplicate IDs fail registration. `Register`/`Unregister` exist for test-only registrations.
- `PacketConvert` frames each packet with a seven-byte little-endian header: `ushort` ID, one reserved byte, and `uint` payload length. Multiple frames are concatenated without an outer count. Serialization leaves the caller's stream open. Unknown IDs and invalid complete payloads are skipped; a truncated frame ends parsing.
- Default payload serialization uses public settable properties in metadata/declaration order. **Property order, enum backing types, and array layout are wire contracts.** Adding a public settable helper property can change the payload; use the existing custom reader/writer pattern when needed.
- `[OsuField]` describes fixed/derived array lengths, conditional fields, element masks, and null-versus-empty strings. Raw packets use `rawData: true` and a `byte[] Data` containing the complete payload without an inner length prefix. Custom readers are static `Read(SerializationReader, IPacketFacade)` methods; custom writers are instance `Write(SerializationWriter)` methods, enabled by the corresponding attribute flags.
- Multiplayer and tournament packets dispatch through `IRoomManager`. `PacketMatchScoreUpdate` reads a complete `ScoreFrame`, including the conditional ScoreV2 doubles; malformed lengths are rejected by `PacketConvert` before the handler runs. Do not turn multiplayer score updates back into raw forwarding.

### Players & spectating

- `PlayerManager` owns token and user-ID indexes under a `Lock`. Each account can have multiple tournament sessions and one regular session. `GetPlayer(int)` prefers the regular session; `GetPlayer(string)` selects an exact token; `GetPlayerAllInstances(int)` returns every session of an account.
- Registering a new regular session replaces the previous regular session. Presence is represented by the preferred session and hides restricted players from other users. Removal checks object identity, so an old session cannot remove its replacement.
- Spectator relationships use **session object identity**, not only account IDs. Both sessions must be registered; self-spectating and restricted endpoints are rejected. Switching hosts removes the previous relationship, updates fellow spectators, and notifies the host. Removing a session also clears its spectator relationships.
- `Player` owns a locked queue of already-serialized outgoing packets. Callbacks enqueue presence/stats/spectator events for the next HTTP poll. Frame forwarding uses the manager's current watcher set and preserves the raw replay payload.
- `PacketLogout` disconnects chat before removing the session. It clears `User.IsOnline` and updates `LastVisit` only after the account's final session has gone.

### Stable multiplayer

- `RoomManager` owns an in-memory registry with independent nonnegative `short` protocol IDs. `IRoom.RoomId` is the shared database's `long` ID; never cast it to a protocol ID. Joining only resolves live Stable rooms, without hydrating arbitrary database/lazer rooms.
- Membership operations take the manager's semaphore and then a room's `RoomLock`; gameplay commands take only the room lock through `ExecuteAsync`. Room methods require the caller to hold that lock. Do not acquire the membership semaphore while holding a room lock. Membership and observer indexes use session object identity.
- `Room` owns all sixteen slots, host identity, teams, per-slot mods and gameplay barriers. Client match snapshots supply settings only; their IDs, host, slots and in-progress flags are never authoritative. Locking an occupied slot removes the player and participation record immediately and sends the removed player the locked snapshot.
- Head-to-head/tag-coop use neutral teams; team-versus/tag-team-versus default to red. Free mods keep speed changes global and host-controlled; disabling free mods combines global speed with the host's personal mods. Slots cannot move or change readiness/teams/mods during a round.
- Only the participants selected at start receive gameplay packets, together with authorized tournament observers. Missing-map players and late joiners wait for the next round. Load/skip barriers fire once and are rechecked on departure; failures still wait for completion. Score frames are retained per slot and reserialized with the server's slot ID.
- Room creation, participants, host/settings/status, end dates and playlists use Common's `IRoomRepository`. Creation explicitly writes `ServerIdentify.Stable` to `Room.Source`; any future database hydration must query that source. `MapId = -1` is a transient selection state with no playlist item. Actual maps resolve by checksum through `IBeatmapRepository` (and the configured fetcher); unresolved maps are rejected. Each changed selection expires the previous item, while start seals an item with `PlayedAt`/`Expired`; replaying the same map creates another item. Name-only changes do not append items. Stable free mods become allowed mods, not lazer's beatmap/ruleset freestyle mode.
- Room channels use the existing `mp_{databaseRoomId}` storage and appear to members as `#multiplayer`. They are absent from the public directory. Tournament sessions can observe a room without occupying slots or receiving its password. `PlayerRemoved` triggers exact-session cleanup on replacement; `Player.Offline` waits for cleanup before returning.
- `Manager/RoomManagerTests` exercises serialized client packets, authoritative slots, permission checks, lobby/password isolation, room chat, playlist history, ScoreV1/V2 decoding, barriers, replacements and concurrent room operations.

### Chat

- `ChatServer` lazily loads public channel rows through Common's `IChatRepository` and caches the directory in memory. Channel names are normalized to a leading `#`; duplicate normalized names are skipped. An empty directory is valid. A load failure is logged and cached as an empty directory for that server instance.
- `StableChannel` tracks membership in memory per session. Connect lists channels; join/leave changes membership and broadcasts counts. Moderated channels require moderator/owner/developer privileges to write. Public channels must already exist in the shared repository.
- A public message requires channel membership and write permission. The server trims it, drops blank text, and truncates overlong text to `ChatConfig.MessageLengthLimit` while returning a notification. The sending session is skipped during delivery because the client echoes its own text locally.
- Private messages resolve an online Stable session by case-insensitive name; offline recipients produce a notification. Both public and PM messages attempt persistence through `IChatRepository` (PMs use `GetOrCreatePmChannelAsync`) before delivery. Persistence failures are logged and delivery continues.
- Live delivery uses Stable sessions' outgoing packet queues. Shared chat storage alone does not broadcast messages to Realtime connections; do not assume Realtime's WebSocket/activity-window chat behavior is implemented here.

### Scores & legacy web responses

- Web handlers authenticate their own credential parameters: `u`/`h` for beatmap info and friends, `us`/`ha` for leaderboards, and the decrypted submission username plus multipart `pass` for scores. Keep these protocol-specific names and response formats intact.
- `StableScoreSubmission` handles the encrypted colon-separated score payload, online checksum, score conversion, and `.osr` construction. Encryption is **Rijndael-CBC with a 256-bit block and PKCS7 padding**; substituting standard AES changes the protocol. MD5 checksums are part of Stable's wire contract.
- `SubmitScoreRouteHandler` validates multipart field counts, decrypts both `score` and `s`, verifies the online checksum and `bmk`, and requires an online, unrestricted, non-tournament player. `score` is both a text field (encrypted submission) and an optional file part (compressed replay frames). A passed play requires a replay of at least 13 bytes; uploads above 8 MiB are rejected.
- `StableScoreSubmissionTracker` serializes retries by `(userId, checksum)` and retains their allocated score ID with a one-day sliding expiration. Its lease must be disposed. Deduplication is local to the server instance; it does not survive a restart or coordinate separate instances.
- `ToScore` creates a `Type = "stable"`, unprocessed score with `ClassicTotalScore` and classic mods; `ScoreModeUtils` applies configured RX/AP mode mapping. After allocating the score ID, replay frames are wrapped into a complete `.osr`, stored at `replays/{scoreId}_{beatmapId}_{userId}_lazer_replay.osr`, then marked uploaded. Preserve this shared storage naming convention despite the `lazer_replay` suffix.
- Passed scores are processed **before responding** through Common's `IScoreProcessingService` so result charts contain calculated pp, rank, and accuracy. Capture immutable statistics snapshots before processing: the processor mutates tracked statistics. Processing failures wake the background queue; failed plays also wake it for play-count processing and return `error: no`.
- `ScoreProcessingQueue` is implemented in Common, registered here as both a singleton and hosted service, and filters persisted pending rows by `"stable"`. It scans at startup and after notifications/timeouts, uses fresh scopes for processing, and retries failures. The readiness predicate permits failed plays or scores with an uploaded replay (`!score.Passed || score.HasReplay`). Preserve that guard when changing replay handling.
- `StableWebFormat` owns legacy status, grade, boolean, leaderboard-row, and chart formatting. Use invariant culture, `True`/`False` for score-row booleans, numeric leaderboard positions for chart ranks, and `N` for unplayed grades. `ToRankStatus` shifts the stored enum by one and clamps negative results to zero.
- Leaderboards use `legacyOnly: true` and display `ClassicTotalScore`; they support global, mods, friends, and country filters. Beatmap-info responses retain request indexes and skip unknown maps. Pipe/newline positions and empty fields are client contracts; preserve each endpoint's literal failure responses (`error: no`, `error: pass`, `-1|false`, `1|false`, or HTTP status as applicable).

## Tests

NUnit 4, Moq, and `Microsoft.NET.Test.Sdk` 17. Tests use in-process handlers/managers with mocked repositories, fetchers, GeoIP, storage, and score processing rather than real DB/Redis/S3 services.

- `LoginPacketTests`, `PacketConvertTests`, `PacketSerializeTests`, and `PacketValueTests` cover parsing, framing, serialization, and wire values. `BanchoPacketCompatibilityTests` consumes the embedded `Fixtures/bancho-packets.json` byte fixtures; `StableScoreSubmissionTests` also uses embedded `Fixtures/bancho-score.json`. Keep fixture resources embedded when adding cases.
- Route tests construct `DefaultHttpContext`, execute `IResult`, and inspect binary/text responses directly. Extend the existing setup for `LoginRouteHandlerTests`, `PacketRouteHandlerTests`, and `SubmitScoreRouteHandlerTests` instead of requiring the production host or external services.
- Configuration-dependent fixtures create a throwaway `config/` tree under `Path.GetTempPath()` and delete it in teardown. `ConfigManager` reads real files; use the same approach and write only the config files the test exercises.
- Chat-dependent tests register `IChatRepository` in their scope factory. `Routes/TestChatRepository.cs` provides a reusable mock; an unconfigured repository otherwise leaves the login channel directory empty.
- `PacketRouteHandlerTests` is `[NonParallelizable]` because it temporarily changes the global packet registration table. Restore/remove runtime registrations in cleanup and preserve isolation when adding such tests.
- `PlayerManagerTests`, `PlayerTests`, and `PlayerActionsTests` cover session replacement, multiple clients, outgoing queues, and spectator relationships. `ChatServerTests` covers stored channels, memberships, message delivery, and limits.
- `ScoreProcessingServiceTests` and `ScoreProcessingQueueTests` exercise Common's processor/queue from the Stable project. Queue tests explicitly start/stop/dispose their workers and use bounded waits; preserve cleanup so background work does not leak across tests. `StableScoreSubmissionTrackerTests` covers retry identity and lease serialization.

## Gotchas

- Read the actual mappings in `Program.ConfigureApp` before changing request dispatch. Currently `/` maps directly to login and `/packet` to polling; XML comments describing token-based polling on `/` do not add such routing.
- Several packet/route XML comments contain historical packet-number or response-shape descriptions. Use `[OsuPacket]`, serializer code, and byte fixtures as the source of truth for IDs and framing.
- Keep binary login/poll failures in the form the client expects. Replacing packet notifications with generic HTTP errors makes the client treat recoverable conditions as a lost connection.
- Config, local storage, GeoIP databases, and generated artifacts are ignored runtime data. Use throwaway files for tests and keep deployment credentials out of tracked documentation.
