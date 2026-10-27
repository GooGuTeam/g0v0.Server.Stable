# g0v0.Server.Stable

This project is a part of g0v0 server v2.
It is g0v0's osu!(stable) server implementation.

## Current Status

This project is under development. The checklist below describes the current implementation status in this repository.

- [x] osu!(stable) protocol
    - [x] Login, device verification and packet polling
    - [x] In-game registration
    - [x] Presence, statistics and friend list management
    - [x] Friend add/remove packets
    - [x] Spectating and isolated spectator chat channels
    - [x] Multiplayer rooms
        - [x] Standard, team and free-mod gameplay
        - [x] Tournament-client room observers
        - [x] Tournament management commands (`!mp`; `makeprivate` and `clearhost` remain unsupported)
    - [x] Score submission, score processing and leaderboards
    - [x] Replay upload, download and view tracking
    - [x] Achievement unlocks
- [ ] Chatting
    - [x] Public, multiplayer and spectator channels
    - [x] Online private messages
    - [x] Friend and private-message privacy settings
    - [x] Away messages and automatic replies
    - [x] BanchoBot commands (`!help`, `!roll`, `!re`, `!pr` and `!mp`, including playlist queue management)
    - [ ] Offline private messages and login-time mail delivery
    - [ ] Stable chat silence
- [ ] Legacy web endpoints
    - [x] Beatmap information, leaderboards, favourites, ratings and seasonal backgrounds
    - [x] osu!direct search and beatmap-set lookup
    - [x] Beatmap downloads and updated `.osu` files
    - [x] Replay download
    - [x] Screenshot upload
    - [x] `/p/doyoureallywanttoaskpeppy`
    - [ ] Beatmap, mapset and replay comments
    - [ ] Legacy private-message mark-as-read endpoint
    - [ ] Beatmap submission

## Credits

The osu!(stable) protocol implementation and compatibility behavior in this project were developed with reference to
[bancho.py](https://github.com/osuAkatsuki/bancho.py).

## License

g0v0 server v2 series is licensed under the [MIT licence](https://opensource.org/licenses/MIT). Please
see [the licence file](LICENCE) for more information.
[tl;dr](https://tldrlegal.com/license/mit-license) you can do whatever you want as long as you **include the original
copyright and license notice in any copy of the software/source**.

"osu!" or "ppy" is a registered trademark of ppy Pty Ltd and is not affiliated with g0v0 server.
