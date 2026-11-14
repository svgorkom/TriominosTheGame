// The rules of a room, without anything from Cloudflare so they can be tested directly.
//
// The server knows nothing about Triominos. Every client plays the whole game itself from the
// same seed, and the room only decides who may join and puts the moves in one order. A move is
// therefore opaque to the room apart from its shape.

export const PROTOCOL_VERSION = 1;
export const MIN_PLAYERS = 2;
export const MAX_PLAYERS = 4;
export const MAX_NAME_LENGTH = 16;

/** Moves one game may hold. A real game has a few hundred at most; this only stops a runaway client. */
export const MAX_MOVES = 5000;

export type Move =
  | { k: 'place'; tile: number; row: number; col: number; rot: number }
  | { k: 'draw' }
  | { k: 'pass' }
  | { k: 'hint' };

export interface Settings {
  /** Hints each player may take; 0 turns hints off. */
  hintsMax: number;
  /** Points a hint costs. */
  hintPenalty: number;
}

export interface Player {
  name: string;
  token: string;
}

export interface LogEntry {
  seat: number;
  move: Move;
}

export interface Room {
  code: string;
  /** In seat order; the first player is the host. */
  players: Player[];
  started: boolean;
  seed: number;
  settings: Settings | null;
  log: LogEntry[];
}

export type RoomError =
  | 'bad_message'
  | 'bad_name'
  | 'name_taken'
  | 'room_full'
  | 'already_started'
  | 'not_joined'
  | 'not_host'
  | 'too_few_players'
  | 'not_started'
  | 'out_of_sync'
  | 'too_many_moves'
  | 'wrong_version';

export type Result<T> = ({ ok: true } & T) | { ok: false; error: RoomError };

export function newRoom(code: string): Room {
  return { code, players: [], started: false, seed: 0, settings: null, log: [] };
}

/** Returns the trimmed name, or null when it is empty or too long. */
export function cleanName(raw: unknown): string | null {
  if (typeof raw !== 'string') {
    return null;
  }

  const name = raw.trim();
  // Control characters have no place in a name; the clients show it in a score panel and in messages.
  // eslint-disable-next-line no-control-regex
  return name.length >= 1 && name.length <= MAX_NAME_LENGTH && !/[\u0000-\u001f\u007f]/.test(name) ? name : null;
}

/**
 * Seats a player. A known token takes its seat back (also after the game started); anyone else
 * needs a free seat in a game that has not started.
 */
export function join(room: Room, rawName: unknown, token: unknown, makeToken: () => string): Result<{ seat: number; token: string }> {
  if (typeof token === 'string') {
    const seat = room.players.findIndex((p) => p.token === token);
    if (seat >= 0) {
      return { ok: true, seat, token };
    }
  }

  if (room.started) {
    return { ok: false, error: 'already_started' };
  }

  const name = cleanName(rawName);
  if (name === null) {
    return { ok: false, error: 'bad_name' };
  }

  if (room.players.some((p) => p.name.toLowerCase() === name.toLowerCase())) {
    return { ok: false, error: 'name_taken' };
  }

  if (room.players.length >= MAX_PLAYERS) {
    return { ok: false, error: 'room_full' };
  }

  const fresh = makeToken();
  room.players.push({ name, token: fresh });
  return { ok: true, seat: room.players.length - 1, token: fresh };
}

/** A player leaves. Before the game starts the seat is given up; afterwards it stays theirs. */
export function leave(room: Room, seat: number): boolean {
  if (room.started || seat < 0 || seat >= room.players.length) {
    return false;
  }

  room.players.splice(seat, 1);
  return true;
}

function validSettings(raw: unknown): Settings | null {
  if (typeof raw !== 'object' || raw === null) {
    return null;
  }

  const { hintsMax, hintPenalty } = raw as Record<string, unknown>;
  if (!Number.isInteger(hintsMax) || !Number.isInteger(hintPenalty)) {
    return null;
  }

  const max = hintsMax as number;
  const penalty = hintPenalty as number;
  return max >= 0 && max <= 99 && penalty >= 0 && penalty <= 99 ? { hintsMax: max, hintPenalty: penalty } : null;
}

/** The host deals a new game (also after one finished): a new seed, an empty log. */
export function start(room: Room, seat: number, rawSettings: unknown, seed: number): Result<object> {
  if (seat !== 0) {
    return { ok: false, error: 'not_host' };
  }

  if (room.players.length < MIN_PLAYERS) {
    return { ok: false, error: 'too_few_players' };
  }

  const settings = validSettings(rawSettings);
  if (settings === null) {
    return { ok: false, error: 'bad_message' };
  }

  room.started = true;
  room.seed = seed;
  room.settings = settings;
  room.log = [];
  return { ok: true };
}

const isCoordinate = (value: unknown): value is number => Number.isInteger(value) && Math.abs(value as number) <= 10_000;

/** Returns the move when it has the shape of one, otherwise null. Only the shape is checked; the clients know the rules. */
export function cleanMove(raw: unknown): Move | null {
  if (typeof raw !== 'object' || raw === null) {
    return null;
  }

  const move = raw as Record<string, unknown>;
  switch (move.k) {
    case 'draw':
    case 'pass':
    case 'hint':
      return { k: move.k };
    case 'place':
      return Number.isInteger(move.tile) &&
        (move.tile as number) >= 0 &&
        (move.tile as number) < 1000 &&
        isCoordinate(move.row) &&
        isCoordinate(move.col) &&
        Number.isInteger(move.rot) &&
        (move.rot as number) >= 0 &&
        (move.rot as number) <= 2
        ? { k: 'place', tile: move.tile as number, row: move.row, col: move.col, rot: move.rot as number }
        : null;
    default:
      return null;
  }
}

/**
 * Puts a move at the end of the log. The sender says how many moves it has seen; when that is not
 * the length of the log the client played on a game that differs from the room's, and the move is refused.
 */
export function addMove(room: Room, seat: number, rawMove: unknown, seen: unknown): Result<{ n: number; move: Move }> {
  if (!room.started) {
    return { ok: false, error: 'not_started' };
  }

  const move = cleanMove(rawMove);
  if (move === null || !Number.isInteger(seen)) {
    return { ok: false, error: 'bad_message' };
  }

  if (seen !== room.log.length) {
    return { ok: false, error: 'out_of_sync' };
  }

  if (room.log.length >= MAX_MOVES) {
    return { ok: false, error: 'too_many_moves' };
  }

  room.log.push({ seat, move });
  return { ok: true, n: room.log.length - 1, move };
}

export interface PlayerView {
  seat: number;
  name: string;
  connected: boolean;
}

export function playerViews(room: Room, connectedSeats: ReadonlySet<number>): PlayerView[] {
  return room.players.map((p, seat) => ({ seat, name: p.name, connected: connectedSeats.has(seat) }));
}

/** What a player gets on joining: the room, their seat and, for a game under way, everything needed to replay it. */
export function snapshot(room: Room, seat: number, token: string, connectedSeats: ReadonlySet<number>) {
  return {
    t: 'room' as const,
    code: room.code,
    seat,
    token,
    players: playerViews(room, connectedSeats),
    started: room.started,
    ...(room.started ? { seed: room.seed, settings: room.settings, log: room.log } : {}),
  };
}
