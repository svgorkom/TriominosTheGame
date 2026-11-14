import { describe, expect, it } from 'vitest';
import { addMove, cleanMove, cleanName, join, leave, MAX_MOVES, newRoom, snapshot, start, type Room } from '../src/room';

let counter = 0;
const token = () => `token-${++counter}`;
const settings = { hintsMax: 3, hintPenalty: 10 };

function roomWith(...names: string[]): Room {
  const room = newRoom('BCDFG');
  for (const name of names) {
    expect(join(room, name, undefined, token).ok).toBe(true);
  }

  return room;
}

describe('names', () => {
  it('are trimmed and must have 1 to 16 characters', () => {
    expect(cleanName('  Anna ')).toBe('Anna');
    expect(cleanName('')).toBeNull();
    expect(cleanName('   ')).toBeNull();
    expect(cleanName('a'.repeat(16))).not.toBeNull();
    expect(cleanName('a'.repeat(17))).toBeNull();
    expect(cleanName(5)).toBeNull();
    expect(cleanName('bad\nname')).toBeNull();
  });
});

describe('join', () => {
  it('seats players in order and gives each a token', () => {
    const room = newRoom('BCDFG');
    const first = join(room, 'Anna', undefined, token);
    const second = join(room, 'Ben', undefined, token);

    expect(first).toMatchObject({ ok: true, seat: 0 });
    expect(second).toMatchObject({ ok: true, seat: 1 });
    expect(first.ok && second.ok && first.token !== second.token).toBe(true);
  });

  it('refuses a name that is taken, ignoring case and spaces around it', () => {
    const room = roomWith('Anna');

    expect(join(room, ' anna ', undefined, token)).toEqual({ ok: false, error: 'name_taken' });
  });

  it('refuses a fifth player and a blank name', () => {
    const room = roomWith('A', 'B', 'C', 'D');

    expect(join(room, 'E', undefined, token)).toEqual({ ok: false, error: 'room_full' });
    expect(join(newRoom('BCDFG'), ' ', undefined, token)).toEqual({ ok: false, error: 'bad_name' });
  });

  it('gives a known token its seat back, also when the room is full or the game has started', () => {
    const room = roomWith('Anna', 'Ben');
    const ben = room.players[1].token;
    start(room, 0, settings, 7);

    expect(join(room, 'whatever', ben, token)).toEqual({ ok: true, seat: 1, token: ben });
  });

  it('refuses newcomers once the game has started', () => {
    const room = roomWith('Anna', 'Ben');
    start(room, 0, settings, 7);

    expect(join(room, 'Cas', undefined, token)).toEqual({ ok: false, error: 'already_started' });
    expect(join(room, 'Cas', 'unknown-token', token)).toEqual({ ok: false, error: 'already_started' });
  });
});

describe('leave', () => {
  it('gives up the seat before the start, which moves the later players up', () => {
    const room = roomWith('Anna', 'Ben', 'Cas');

    expect(leave(room, 0)).toBe(true);
    expect(room.players.map((p) => p.name)).toEqual(['Ben', 'Cas']);
  });

  it('keeps the seat once the game has started', () => {
    const room = roomWith('Anna', 'Ben');
    start(room, 0, settings, 7);

    expect(leave(room, 1)).toBe(false);
    expect(room.players).toHaveLength(2);
  });
});

describe('start', () => {
  it('is for the host with at least two players and valid settings', () => {
    const room = roomWith('Anna');
    expect(start(room, 0, settings, 7)).toEqual({ ok: false, error: 'too_few_players' });

    join(room, 'Ben', undefined, token);
    expect(start(room, 1, settings, 7)).toEqual({ ok: false, error: 'not_host' });
    expect(start(room, 0, { hintsMax: -1, hintPenalty: 10 }, 7)).toEqual({ ok: false, error: 'bad_message' });
    expect(start(room, 0, 'nope', 7)).toEqual({ ok: false, error: 'bad_message' });
    expect(room.started).toBe(false);

    expect(start(room, 0, settings, 7)).toEqual({ ok: true });
    expect(room).toMatchObject({ started: true, seed: 7, settings });
  });

  it('deals again with a new seed and an empty log', () => {
    const room = roomWith('Anna', 'Ben');
    start(room, 0, settings, 7);
    addMove(room, 0, { k: 'draw' }, 0);

    start(room, 0, settings, 8);

    expect(room.seed).toBe(8);
    expect(room.log).toEqual([]);
  });
});

describe('moves', () => {
  it('must have the shape of a move', () => {
    expect(cleanMove({ k: 'place', tile: 3, row: -1, col: 2, rot: 1 })).toEqual({ k: 'place', tile: 3, row: -1, col: 2, rot: 1 });
    expect(cleanMove({ k: 'draw', extra: 'dropped' })).toEqual({ k: 'draw' });
    expect(cleanMove({ k: 'place', tile: 3, row: 0, col: 0, rot: 3 })).toBeNull();
    expect(cleanMove({ k: 'place', tile: 1.5, row: 0, col: 0, rot: 0 })).toBeNull();
    expect(cleanMove({ k: 'place', tile: 1, row: 1e9, col: 0, rot: 0 })).toBeNull();
    expect(cleanMove({ k: 'cheat' })).toBeNull();
    expect(cleanMove(null)).toBeNull();
  });

  it('are numbered in the order they come in', () => {
    const room = roomWith('Anna', 'Ben');
    start(room, 0, settings, 7);

    expect(addMove(room, 0, { k: 'draw' }, 0)).toMatchObject({ ok: true, n: 0 });
    expect(addMove(room, 1, { k: 'pass' }, 1)).toMatchObject({ ok: true, n: 1 });
    expect(room.log).toEqual([
      { seat: 0, move: { k: 'draw' } },
      { seat: 1, move: { k: 'pass' } },
    ]);
  });

  it('are refused before the start, when the sender is behind and when the log is full', () => {
    const room = roomWith('Anna', 'Ben');
    expect(addMove(room, 0, { k: 'draw' }, 0)).toEqual({ ok: false, error: 'not_started' });

    start(room, 0, settings, 7);
    addMove(room, 0, { k: 'draw' }, 0);
    expect(addMove(room, 1, { k: 'draw' }, 0)).toEqual({ ok: false, error: 'out_of_sync' });
    expect(addMove(room, 1, { k: 'draw' }, undefined)).toEqual({ ok: false, error: 'bad_message' });

    room.log = Array.from({ length: MAX_MOVES }, () => ({ seat: 0, move: { k: 'draw' as const } }));
    expect(addMove(room, 0, { k: 'draw' }, MAX_MOVES)).toEqual({ ok: false, error: 'too_many_moves' });
  });
});

describe('snapshot', () => {
  it('shows a lobby without game data, and a game with everything needed to replay it', () => {
    const room = roomWith('Anna', 'Ben');
    expect(snapshot(room, 1, 'secret', new Set([0, 1]))).toEqual({
      t: 'room',
      code: 'BCDFG',
      seat: 1,
      token: 'secret',
      players: [
        { seat: 0, name: 'Anna', connected: true },
        { seat: 1, name: 'Ben', connected: true },
      ],
      started: false,
    });

    start(room, 0, settings, 7);
    addMove(room, 0, { k: 'draw' }, 0);
    expect(snapshot(room, 0, 'secret', new Set([0]))).toMatchObject({
      started: true,
      seed: 7,
      settings,
      log: [{ seat: 0, move: { k: 'draw' } }],
      players: [
        { seat: 0, connected: true },
        { seat: 1, connected: false },
      ],
    });
  });
});
