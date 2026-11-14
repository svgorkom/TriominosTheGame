// The Durable Object of one room: the sockets of its players and what is kept between visits.
// The rules of the room itself are in room.ts.

import { DurableObject } from 'cloudflare:workers';
import {
  addMove,
  join,
  leave,
  newRoom,
  PROTOCOL_VERSION,
  playerViews,
  snapshot,
  start,
  type LogEntry,
  type Room,
  type RoomError,
} from './room';

export interface Env {
  ROOMS: DurableObjectNamespace<GameRoom>;
  ASSETS: Fetcher;
}

/** A room nobody touched for this long is deleted, so abandoned rooms do not fill the storage. */
const IDLE_MS = 24 * 60 * 60 * 1000;

/** The alarm is moved at most this often, because every alarm write counts as a stored row. */
const ALARM_REFRESH_MS = 10 * 60 * 1000;

/** Messages are tiny; anything bigger is not from the game. */
const MAX_MESSAGE_LENGTH = 2048;

const META_KEY = 'room';
const MOVE_PREFIX = 'm:';
const moveKey = (n: number) => `${MOVE_PREFIX}${String(n).padStart(5, '0')}`;

interface Attachment {
  /** The player's token; the seat follows from it because seats shift when someone leaves before the start. */
  token: string;
}

type Meta = Omit<Room, 'log'>;

export class GameRoom extends DurableObject<Env> {
  private room: Room | null = null;
  private alarmSetAt = 0;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    // Answered without waking the object, so a keep-alive costs no compute.
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair('ping', 'pong'));
    ctx.blockConcurrencyWhile(async () => {
      const meta = await ctx.storage.get<Meta>(META_KEY);
      if (meta) {
        const moves = await ctx.storage.list<LogEntry>({ prefix: MOVE_PREFIX });
        this.room = { ...meta, log: [...moves.values()] };
      }
    });
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === '/create' && request.method === 'POST') {
      if (this.room) {
        return new Response('exists', { status: 409 });
      }

      this.room = newRoom(request.headers.get('x-room-code') ?? '');
      await this.save();
      await this.touch(true);
      return new Response('created', { status: 201 });
    }

    if (!this.room) {
      return new Response('room_not_found', { status: 404 });
    }

    if (request.headers.get('Upgrade') !== 'websocket') {
      return new Response('Expected a WebSocket', { status: 426 });
    }

    const pair = new WebSocketPair();
    this.ctx.acceptWebSocket(pair[1]);
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
    const room = this.room;
    if (!room) {
      return;
    }

    if (typeof message !== 'string' || message.length > MAX_MESSAGE_LENGTH) {
      this.fail(ws, 'bad_message');
      return;
    }

    let data: Record<string, unknown>;
    try {
      data = JSON.parse(message);
    } catch {
      this.fail(ws, 'bad_message');
      return;
    }

    if (typeof data !== 'object' || data === null) {
      this.fail(ws, 'bad_message');
      return;
    }

    if (data.t === 'join') {
      await this.onJoin(ws, room, data);
      return;
    }

    const seat = this.seatOf(ws);
    if (seat < 0) {
      this.fail(ws, 'not_joined');
      return;
    }

    switch (data.t) {
      case 'start': {
        const seed = crypto.getRandomValues(new Uint32Array(1))[0] >>> 1;
        const result = start(room, seat, data.settings, seed);
        if (!result.ok) {
          this.fail(ws, result.error);
          return;
        }

        await this.ctx.storage.deleteAll();
        await this.save();
        await this.touch(true);
        this.broadcast({ t: 'started', seed: room.seed, settings: room.settings, players: this.views() });
        return;
      }
      case 'move': {
        const result = addMove(room, seat, data.move, data.n);
        if (!result.ok) {
          this.fail(ws, result.error);
          return;
        }

        await this.ctx.storage.put(moveKey(result.n), room.log[result.n]);
        await this.touch(false);
        this.broadcast({ t: 'move', n: result.n, seat, move: result.move }, ws);
        return;
      }
      case 'leave': {
        if (leave(room, seat)) {
          await this.save();
          ws.serializeAttachment(null);
          this.resync();
        }

        ws.close(1000, 'left');
        return;
      }
      default:
        this.fail(ws, 'bad_message');
    }
  }

  async webSocketClose(ws: WebSocket): Promise<void> {
    // The close itself is answered by the runtime (compatibility date 2022-10-31 or later).
    this.announceDeparture(ws);
  }

  async webSocketError(ws: WebSocket): Promise<void> {
    this.announceDeparture(ws);
  }

  async alarm(): Promise<void> {
    if (this.ctx.getWebSockets().length > 0) {
      await this.ctx.storage.setAlarm(Date.now() + IDLE_MS);
      return;
    }

    await this.ctx.storage.deleteAll();
    this.room = null;
  }

  private async onJoin(ws: WebSocket, room: Room, data: Record<string, unknown>): Promise<void> {
    if (data.v !== PROTOCOL_VERSION) {
      this.fail(ws, 'wrong_version');
      return;
    }

    const result = join(room, data.name, data.token, () => crypto.randomUUID());
    if (!result.ok) {
      this.fail(ws, result.error);
      return;
    }

    // The same player in a second tab or after a lost connection: the old socket makes way.
    for (const other of this.ctx.getWebSockets()) {
      if (other !== ws && this.tokenOf(other) === result.token) {
        other.serializeAttachment(null);
        other.close(4000, 'replaced');
      }
    }

    ws.serializeAttachment({ token: result.token } satisfies Attachment);
    await this.save();
    await this.touch(false);
    ws.send(JSON.stringify(snapshot(room, result.seat, result.token, this.connectedSeats())));
    this.broadcast({ t: 'players', players: this.views() }, ws);
  }

  /** Gives every joined socket the room again, for a change that moved the seats. */
  private resync(): void {
    const room = this.room;
    if (!room) {
      return;
    }

    for (const ws of this.ctx.getWebSockets()) {
      const token = this.tokenOf(ws);
      const seat = room.players.findIndex((p) => p.token === token);
      if (token && seat >= 0) {
        ws.send(JSON.stringify(snapshot(room, seat, token, this.connectedSeats())));
      }
    }
  }

  /** Tells the others that a player is gone; a socket that never joined has nothing to announce. */
  private announceDeparture(ws: WebSocket): void {
    if (this.seatOf(ws) >= 0) {
      this.broadcast({ t: 'players', players: this.views(ws) }, ws);
    }
  }

  private tokenOf(ws: WebSocket): string | null {
    return (ws.deserializeAttachment() as Attachment | null)?.token ?? null;
  }

  private seatOf(ws: WebSocket): number {
    const token = this.tokenOf(ws);
    return token && this.room ? this.room.players.findIndex((p) => p.token === token) : -1;
  }

  private connectedSeats(except?: WebSocket): Set<number> {
    const seats = new Set<number>();
    for (const ws of this.ctx.getWebSockets()) {
      if (ws !== except) {
        const seat = this.seatOf(ws);
        if (seat >= 0) {
          seats.add(seat);
        }
      }
    }

    return seats;
  }

  private views(except?: WebSocket) {
    return this.room ? playerViews(this.room, this.connectedSeats(except)) : [];
  }

  private broadcast(message: object, except?: WebSocket): void {
    const text = JSON.stringify(message);
    for (const ws of this.ctx.getWebSockets()) {
      if (ws !== except && this.seatOf(ws) >= 0) {
        try {
          ws.send(text);
        } catch {
          // A socket that is closing; its own close event tells the others.
        }
      }
    }
  }

  private fail(ws: WebSocket, error: RoomError): void {
    ws.send(JSON.stringify({ t: 'error', error }));
  }

  private async save(): Promise<void> {
    if (this.room) {
      const { log: _log, ...meta } = this.room;
      await this.ctx.storage.put(META_KEY, meta);
    }
  }

  private async touch(force: boolean): Promise<void> {
    const now = Date.now();
    if (force || now - this.alarmSetAt > ALARM_REFRESH_MS) {
      this.alarmSetAt = now;
      await this.ctx.storage.setAlarm(now + IDLE_MS);
    }
  }
}
