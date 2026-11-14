# Plan: online play on Cloudflare

Status: built on branch `feat/cloudflare-multiplayer`, awaiting review. Requested 2026-11-14: put the game online on Cloudflare (free account), publish from GitHub, support multiplayer, keep it simple.

## Goals

- The game is served from Cloudflare and deployed by GitHub Actions on every push to `main`.
- 2 to 4 people play one game, each at their own screen, in a room they join with a code or a link.
- Simple: no accounts, no matchmaking, nothing for the server to know about the rules.

## Non-goals (for now)

- Computer players in an online game (the setup screen is for games on one screen).
- Spectators, chat, moving the host role, removing a player who left for good.
- Protection against a player who cheats with the browser's developer tools (see the trust model).

## Architecture

```
browser (Blazor WebAssembly + Three.js)  ──HTTP──▶  Cloudflare Worker ──▶ static files (the published site)
        │                                                   │
        └───────────── WebSocket /ws/CODE ─────────────────▶ GameRoom (one Durable Object per room)
```

- **One Worker serves everything** (`server/`). `wrangler.jsonc` makes only `/api/*` and `/ws/*` run the Worker; every other request is a static file, which costs no Worker requests.
- **The room knows no rules.** Every client plays the whole game itself. `TriominosGame` is deterministic, so the same seed and the same actions in the same order give the same game on every screen. The room only seats players and puts the actions in one order.
- **Core gets `GameAction`** (`PlaceAction`, `DrawAction`, `PassAction`, `HintAction`): a command as a value that can be sent. Applying a log of actions to a game dealt from the same seed reproduces the game.
- **`PlaySession` gets a local seat.** The screen of an online game shows its own rack whoever is to move; the actions of the others come in through `ApplyRemote`, which only accepts the player to move (everyone filters the same way, so the games cannot drift apart).
- **A move is played at once at the screen that makes it** and then sent. This is safe because only the player to move can act, so two actions never cross.
- **Reconnecting.** A seat belongs to a token (kept in local storage with the room code). A dropped connection is retried with a short delay; the room answers with the whole log and the screen plays what it missed. A refreshed page does the same and replays the game without animation.
- **Order is checked.** Every action carries the number of actions its sender has seen. The room refuses one that does not match the length of its log (`out_of_sync`), and a screen that sees a gap fetches the game again.

### Protocol (JSON over the WebSocket)

Client to room: `join {v, name, token?}`, `start {settings}` (host only), `move {n, move}`, `leave`, and the text `ping`.
Room to client: `room {code, seat, token, players, started, seed?, settings?, log?}`, `players {players}`, `started {seed, settings, players}`, `move {n, seat, move}`, `error {error}`, and the text `pong`.
`move` is `{k: "place", tile, row, col, rot}`, `{k: "draw"}`, `{k: "pass"}` or `{k: "hint"}`. The room checks only the shape. The protocol version is 1 (`MoveCodec.ProtocolVersion`, `PROTOCOL_VERSION`); the room refuses another one.

### Rooms

- A code is 5 consonants; a room is a Durable Object named by it. Seat 0 is the host and starts (and restarts) the game with a new seed.
- Before the start a player can leave and the later seats move up. After the start a seat stays with its token.
- A room nobody touched for 24 hours deletes itself (an alarm), so abandoned rooms do not fill the free storage.
- Limits: 4 players, names of 1 to 16 characters (unique, ignoring case), 5000 actions per game, messages of at most 2 KiB.

## Trust model

Every screen runs the whole game, so every screen holds every rack and the order of the pot. The interface hides them, but a player who reads the browser's memory can see them. That is fine for friends. A game with strangers needs the rules on the server: port `Triominos.Core` to TypeScript (or run it in the Worker) and send each player only their own rack.

## Cost

The free plan is enough to start. Static files are free and unlimited. A game costs a few hundred WebSocket messages, and Durable Objects are on the free plan (SQLite storage). If a daily limit is reached, further operations fail until 00:00 UTC; the Workers Paid plan (USD 5 a month) lifts that. Check the current limits on developers.cloudflare.com before relying on them.

## Setup (once)

1. A Cloudflare account, with a workers.dev subdomain (Workers & Pages in the dashboard asks for it the first time).
2. An API token with the permission *Workers Scripts: Edit* (the "Edit Cloudflare Workers" template) and the account id.
3. In the GitHub repository, Settings, Secrets and variables, Actions: `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID`.
4. Run the *Deploy to Cloudflare* workflow (or push to `main`). The site is at `https://triominos.<subdomain>.workers.dev`; the name comes from `server/wrangler.jsonc`.

## Local development

```bash
dotnet publish src/Triominos.Web/Triominos.Web.csproj -c Release -o publish
cd server
npm ci
npm run dev        # http://localhost:8787, the site and the rooms
npm test           # the rules of a room
npm run e2e        # a few clients against `npm run dev`
```

`dotnet run --project src/Triominos.Web` still works for a game on one screen; online play needs the Worker because the dev server has no rooms.

## Open items

- Read the first deploy: whether Cloudflare serves the Blazor files compressed (it should; the pre-compressed `.br` and `.gz` files are left out of the upload on purpose).
- Computer players online, a way to remove a player who left for good, and a phone layout are not built.
