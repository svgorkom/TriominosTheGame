// Plays a few clients against a running `wrangler dev` (default http://localhost:8787) and checks
// the answers. Run: npm run dev in one terminal, npm run e2e in another.
import assert from 'node:assert/strict';
import WebSocket from 'ws';

const base = process.argv[2] ?? 'http://localhost:8787';
const wsBase = base.replace(/^http/, 'ws');

class Client {
  constructor(code) {
    this.messages = [];
    this.waiting = [];
    this.socket = new WebSocket(`${wsBase}/ws/${code}`);
    this.socket.on('message', (data) => {
      const text = data.toString();
      if (text === 'pong') {
        this.deliver({ t: 'pong' });
        return;
      }

      this.deliver(JSON.parse(text));
    });
    this.opened = new Promise((resolve, reject) => {
      this.socket.once('open', resolve);
      this.socket.once('error', reject);
    });
    this.closed = new Promise((resolve) => this.socket.once('close', (code) => resolve(code)));
  }

  deliver(message) {
    const index = this.waiting.findIndex((w) => w.t === message.t);
    if (index >= 0) {
      this.waiting.splice(index, 1)[0].resolve(message);
    } else {
      this.messages.push(message);
    }
  }

  next(t, ms = 3000) {
    const index = this.messages.findIndex((m) => m.t === t);
    if (index >= 0) {
      return Promise.resolve(this.messages.splice(index, 1)[0]);
    }

    return new Promise((resolve, reject) => {
      const waiter = { t, resolve };
      this.waiting.push(waiter);
      setTimeout(() => reject(new Error(`no "${t}" within ${ms} ms; got ${JSON.stringify(this.messages)}`)), ms);
    });
  }

  send(message) {
    this.socket.send(typeof message === 'string' ? message : JSON.stringify(message));
  }

  async quiet(ms = 300) {
    await new Promise((r) => setTimeout(r, ms));
    return this.messages.splice(0);
  }
}

const settings = { hintsMax: 3, hintPenalty: 10 };
const step = (text) => console.log(`- ${text}`);

step('create a room');
const created = await fetch(`${base}/api/rooms`, { method: 'POST' });
assert.equal(created.status, 201);
const { code } = await created.json();
assert.match(code, /^[BCDFGHJKLMNPQRSTVWXZ]{5}$/);

step('an unknown room is not found');
const missing = await new Promise((resolve) => {
  const probe = new WebSocket(`${wsBase}/ws/BBBBB`);
  probe.on('unexpected-response', (_request, response) => resolve(response.statusCode));
  probe.on('error', () => {});
});
assert.equal(missing, 404);

step('the first player gets seat 0');
const anna = new Client(code);
await anna.opened;
anna.send({ t: 'join', v: 1, name: 'Anna' });
const annaRoom = await anna.next('room');
assert.equal(annaRoom.seat, 0);
assert.equal(annaRoom.started, false);
assert.ok(annaRoom.token);

step('a wrong protocol version is refused');
const old = new Client(code);
await old.opened;
old.send({ t: 'join', v: 99, name: 'Old' });
assert.equal((await old.next('error')).error, 'wrong_version');
old.socket.close();

step('the second player joins and the first one hears of it');
const ben = new Client(code);
await ben.opened;
ben.send({ t: 'join', v: 1, name: 'Ben' });
const benRoom = await ben.next('room');
assert.equal(benRoom.seat, 1);
const players = await anna.next('players');
assert.deepEqual(players.players.map((p) => [p.name, p.connected]), [['Anna', true], ['Ben', true]]);

step('a name that is taken is refused');
const dup = new Client(code);
await dup.opened;
dup.send({ t: 'join', v: 1, name: 'anna' });
assert.equal((await dup.next('error')).error, 'name_taken');
dup.socket.close();

step('only the host can start');
ben.send({ t: 'start', settings });
assert.equal((await ben.next('error')).error, 'not_host');
anna.send({ t: 'start', settings });
const [startedA, startedB] = await Promise.all([anna.next('started'), ben.next('started')]);
assert.equal(startedA.seed, startedB.seed);
assert.deepEqual(startedA.settings, settings);

step('a newcomer cannot join a game that has started');
const late = new Client(code);
await late.opened;
late.send({ t: 'join', v: 1, name: 'Late' });
assert.equal((await late.next('error')).error, 'already_started');
late.socket.close();

step('moves reach the others in order, and not the sender');
anna.send({ t: 'move', n: 0, move: { k: 'place', tile: 5, row: 0, col: 0, rot: 1 } });
const move0 = await ben.next('move');
assert.deepEqual(move0, { t: 'move', n: 0, seat: 0, move: { k: 'place', tile: 5, row: 0, col: 0, rot: 1 } });
ben.send({ t: 'move', n: 1, move: { k: 'draw' } });
assert.deepEqual(await anna.next('move'), { t: 'move', n: 1, seat: 1, move: { k: 'draw' } });
assert.deepEqual(await anna.quiet(), []);

step('a move from a client that is behind is refused');
anna.send({ t: 'move', n: 1, move: { k: 'pass' } });
assert.equal((await anna.next('error')).error, 'out_of_sync');

step('keep-alive is answered');
ben.send('ping');
await ben.next('pong');

step('a player who drops out comes back with the token and the whole log');
anna.socket.close();
const gone = await ben.next('players');
assert.equal(gone.players[0].connected, false);
const again = new Client(code);
await again.opened;
again.send({ t: 'join', v: 1, name: 'Anna', token: annaRoom.token });
const back = await again.next('room');
assert.equal(back.seat, 0);
assert.equal(back.started, true);
assert.equal(back.seed, startedA.seed);
assert.equal(back.log.length, 2);
assert.equal((await ben.next('players')).players[0].connected, true);

step('the host deals again: new seed, empty log');
again.send({ t: 'start', settings: { hintsMax: 0, hintPenalty: 0 } });
const second = await ben.next('started');
assert.deepEqual(second.settings, { hintsMax: 0, hintPenalty: 0 });
again.send({ t: 'move', n: 0, move: { k: 'pass' } });
assert.equal((await ben.next('move')).n, 0);

step('the static site is served');
const home = await fetch(`${base}/`);
assert.equal(home.status, 200);

again.socket.close();
ben.socket.close();
console.log('all good');
