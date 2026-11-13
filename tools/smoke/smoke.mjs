// Smoke test of the published site: loads it in headless Chrome, goes through the main menu, the
// settings and the player setup to start a game, and plays real moves with the mouse (the deal,
// start tile, a normal move, drawing), failing on any JavaScript error, a missing scene, a move
// the game does not accept, or animations that do not run or do not finish. Moves are animated,
// so the test waits for the scene to settle after every action. The sounds are checked by their
// log entries (headless Chrome has no loudspeaker); muting in the settings must silence them. At
// the end the page is reloaded: the settings and the last players must come back from the browser's
// local storage, and a damaged value must fall back to its default.
// The test starts a fresh browser, so the local storage is empty and the game starts from its
// defaults; run against a browser profile that holds settings and the first checks would differ.
// Usage: node smoke.mjs <url> [screenshot.jpg]
import { chromium } from 'playwright-core';

const base = process.argv[2] ?? 'http://localhost:8080/';
const screenshot = process.argv[3];
// A fixed seed makes every run play the same game; ?debug exposes tile positions on the page.
const url = `${base}${base.includes('?') ? '&' : '?'}seed=7&debug`;

const browser = await chromium.launch({
  channel: 'chrome',
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const errors = [];
const log = [];
page.on('console', m => log.push(`${m.type()}: ${m.text()}`));
page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
page.on('console', m => m.type() === 'error' && errors.push(`console: ${m.text()}`));

const fail = message => errors.push(`smoke: ${message}`);
const message = () => page.innerText('.message');
const pause = ms => page.waitForTimeout(ms);
const logNames = () => page.evaluate(() => window.__triominos.log().map(e => e.name));
const count = (names, prefix) => names.filter(n => n.startsWith(prefix)).length;

/** Waits until the scene has finished every animation, including the sliding rack. */
const settle = () => page.waitForFunction(() => !window.__triominos.busy(), null, { timeout: 60_000 });

/** Waits (briefly) until the page reaches a state; Blazor renders a moment after a click or key press. */
async function until(what, predicate, arg) {
  try {
    await page.waitForFunction(predicate, arg, { timeout: 5_000 });
  } catch {
    fail(what);
  }
}

/** Opens the settings from the menu, tries both settings and goes back to the menu. */
async function checkSettings() {
  await page.click('.menu button:has-text("Instellingen")');
  await page.waitForSelector('.settings');
  // The speed setting reaches the scene (setAnimationSpeed); an error there would show as a page error.
  await page.click('.settings .choice:has-text("Zeer snel")');
  await until('the speed choice was not selected', () => [...document.querySelectorAll('.settings .choice.selected')].some(c => c.textContent === 'Zeer snel'));
  await page.click('.settings .choice:has-text("Normaal")');
  await page.click('.settings .choice:has-text("Laag")');
  await page.click('.settings .choice:has-text("Middel")');

  // The sound setting: the volume slider only works while the sound is on, and a change is previewed.
  await page.click('.settings .choice:text-is("Uit")');
  await until('the volume slider stays active with the sound off', () => document.querySelector('.settings input[type=range]')?.disabled === true);
  await page.click('.settings .choice:text-is("Aan")');
  await until('the volume slider stays disabled with the sound on', () => document.querySelector('.settings input[type=range]')?.disabled === false);
  await page.fill('.settings input[type=range]', '40');
  await until('the volume was not shown', () => document.querySelector('.settings output')?.textContent === '40');
  await page.click('.settings button.primary');
  await page.waitForSelector('.menu');
}

/** Names of the sounds the scene played so far, such as "sound:land". */
const soundNames = async () => (await logNames()).filter(n => n.startsWith('sound:'));

/** Checks the player setup (2 to 4 players, unique names) and starts the game with two default players. */
async function startGameFromSetup() {
  const rows = n => until(`the setup should show ${n} players`, count => document.querySelectorAll('.setup-player').length === count, n);
  const disabled = (what, selector, want) =>
    until(what, ([sel, expected]) => document.querySelector(sel)?.disabled === expected, [selector, want]);

  await page.click('.menu button:has-text("Nieuw spel")');
  await page.waitForSelector('.setup');
  await rows(2);

  await page.click('.setup button:has-text("Speler toevoegen")');
  await page.click('.setup button:has-text("Speler toevoegen")');
  await rows(4);
  await until('the setup allows more than 4 players', () => [...document.querySelectorAll('.setup button')].find(b => b.textContent.includes('toevoegen'))?.disabled === true);
  await page.click('.setup-player:nth-child(4) .remove');
  await rows(3);
  await page.click('.setup-player:nth-child(3) .remove');
  await rows(2);
  await disabled('the setup allows fewer than 2 players', '.setup-player:nth-child(1) .remove', true);

  // The same name twice (ignoring case) or a blank name cannot start a game.
  await page.fill('.setup-player:nth-child(2) .name-input', 'speler 1');
  await disabled('the setup starts a game with two players of the same name', '.setup button.primary', true);
  await until('no message about the repeated name', () => /eigen naam/.test(document.querySelector('.setup .problem')?.textContent ?? ''));
  await page.fill('.setup-player:nth-child(2) .name-input', '');
  await disabled('the setup starts a game with a blank name', '.setup button.primary', true);
  // A name and a colour of their own, which the reload at the end must bring back.
  await page.fill('.setup-player:nth-child(2) .name-input', 'Bea');
  await page.click('.setup-player:nth-child(2) .swatch-button:nth-child(3)');
  await disabled('the setup does not start with valid names', '.setup button.primary', false);

  await page.click('.setup button.primary');
  await page.waitForSelector('.setup', { state: 'detached', timeout: 15_000 });
}

async function startTurn() {
  await page.waitForSelector('.overlay button.primary', { timeout: 15_000 });
  await page.click('.overlay button.primary');
  await page.waitForFunction(() => window.__triominos?.rackTiles().length > 0, null, { timeout: 15_000 });
  await settle();
}

/** Picks up a rack tile, rotates it to a fitting rotation, drops it there. Returns false if no tile fits. */
async function playAnyTile({ onlyMarked }) {
  const tiles = await page.evaluate(() => window.__triominos.rackTiles());
  for (const tile of tiles.filter(t => !onlyMarked || t.marked)) {
    await page.mouse.move(tile.x, tile.y);
    await page.mouse.down();
    await page.mouse.move(tile.x, tile.y - 90, { steps: 6 });
    await pause(300);
    const held = await page.evaluate(() => window.__triominos.heldInfo());
    if (!held || held.fits.length === 0) {
      await page.keyboard.press('Escape');
      await page.mouse.up();
      await pause(200);
      continue;
    }

    const target = held.fits[0];
    for (let i = 0; i < 3 && (await page.evaluate(() => window.__triominos.heldInfo().rotation)) !== target.rotation; i++) {
      await page.keyboard.press('r');
      await pause(150);
    }

    const cell = await page.evaluate(([r, c]) => window.__triominos.cellScreen(r, c), [target.row, target.col]);
    await page.mouse.move(cell.x, cell.y, { steps: 8 });
    await pause(250);
    const before = await logNames();
    await page.mouse.up();

    // The move is animated: the scene is busy and the next screen does not show until it is done.
    await page.waitForFunction(() => window.__triominos.busy(), null, { timeout: 5_000, polling: 'raf' });
    if (await page.locator('.overlay').count()) fail('the next screen showed up before the animation was done');
    await page.waitForSelector('.overlay', { timeout: 60_000 });
    await settle();
    const after = await logNames();
    for (const step of ['placed', 'landed', 'points:', 'ready']) {
      if (count(after, step) <= count(before, step)) fail(`the move did not play its "${step}" animation`);
    }

    return true;
  }

  return false;
}

async function run() {
  await page.goto(url);
  await page.waitForSelector('canvas.scene', { timeout: 60_000 });
  await page.waitForSelector('.menu', { timeout: 60_000 });
  await pause(1000);
  console.log('menu:', (await page.innerText('.menu')).replace(/\s+/g, ' '));
  await checkSettings();
  await startGameFromSetup();
  await page.waitForSelector('.overlay h2', { timeout: 60_000 });
  await pause(1500);
  console.log('start:', (await page.innerText('.overlay')).replace(/\s+/g, ' '));
  if ((await page.locator('.player').count()) !== 2) fail('the score panel does not show two players');

  // 1. The starting player must lay one of the marked tiles on the middle cell.
  await startTurn();
  if (!(await playAnyTile({ onlyMarked: true }))) fail('could not play a starting tile');
  console.log('after start:', await message());
  if (!/begint met/.test(await message())) fail(`start move not accepted: ${await message()}`);

  // 2. The other player draws with the space bar, then plays a tile that fits.
  await startTurn();
  const poolBefore = Number(await page.innerText('.pool-count'));
  const drawnBefore = count(await logNames(), 'drawn');
  await page.keyboard.press('Space');
  await page.waitForFunction(n => window.__triominos.log().filter(e => e.name === 'drawn').length > n, drawnBefore, { timeout: 10_000 });
  await settle();
  const poolAfter = Number(await page.innerText('.pool-count'));
  console.log('pool', poolBefore, '->', poolAfter, '|', await message());
  if (poolAfter !== poolBefore - 1) fail('space did not draw a tile');

  let played = await playAnyTile({ onlyMarked: false });
  for (let draws = 0; !played && draws < 2; draws++) {
    await page.keyboard.press('Space');
    await pause(500);
    await settle();
    played = await playAnyTile({ onlyMarked: false });
  }

  console.log('second move:', await message());
  if (played && !/legt/.test(await message())) fail(`second move not accepted: ${await message()}`);

  // 3. Back to the first player, with the rack and board in view.
  if (played) {
    await startTurn();
  }

  // 4. The menu opens over the running game and the game goes on from where it was.
  await page.click('.actions button:has-text("Menu")');
  await page.waitForSelector('.menu');
  if (!(await page.locator('.menu button:has-text("Verder spelen")').count())) fail('the menu offers no way back to the running game');
  await page.click('.menu button:has-text("Verder spelen")');
  await page.waitForSelector('.menu', { state: 'detached' });
  await pause(500);

  // 5. An idle table draws no frames: render on demand.
  const frames = await page.evaluate(() => window.__triominos.frames());
  await pause(1500);
  const idleFrames = (await page.evaluate(() => window.__triominos.frames())) - frames;
  console.log('frames drawn while idle:', idleFrames, '| draw calls:', await page.evaluate(() => window.__triominos.drawCalls()));
  if (idleFrames > 1) fail(`the idle table kept drawing frames (${idleFrames})`);

  const names = await logNames();
  console.log('animations:', ['deal', 'placed', 'landed', 'drawn', 'ready'].map(n => `${n}=${count(names, n)}`).join(' '));
  if (count(names, 'deal') < 1) fail('the deal was not animated');

  // 6. The sounds went with the animations (the audio context itself may be suspended in headless Chrome).
  const sounds = await soundNames();
  console.log('sounds:', ['deal', 'land', 'draw', 'bonus'].map(n => `${n}=${count(sounds, `sound:${n}`)}`).join(' '), '| audio:', await page.evaluate(() => window.__triominos.audio()));
  if (count(sounds, 'sound:deal') < 1) fail('the deal made no sound');
  if (count(sounds, 'sound:draw') < 1) fail('drawing a tile made no sound');
  if (count(sounds, 'sound:land') < count(names, 'landed')) fail('a tile landed without a sound');

  // 7. With the sound off, the next move is silent.
  await page.click('.actions button:has-text("Menu")');
  await page.click('.menu button:has-text("Instellingen")');
  await page.click('.settings .choice:text-is("Uit")');
  await page.click('.settings button.primary');
  await page.click('.menu button:has-text("Verder spelen")');
  await page.waitForSelector('.menu', { state: 'detached' });
  const soundsBefore = (await soundNames()).length;
  const drawnNow = count(await logNames(), 'drawn');
  await page.keyboard.press('Space');
  await page.waitForFunction(n => window.__triominos.log().filter(e => e.name === 'drawn').length > n, drawnNow, { timeout: 10_000 });
  await settle();
  if ((await soundNames()).length !== soundsBefore) fail('the game made sound with the sound turned off');
  console.log('players:', (await page.innerText('.players')).replace(/\s+/g, ' '));
  console.log('pool:', await page.innerText('.pool-count'));

  await checkStored();
}

/** Opens the settings from the main menu. */
async function openSettings() {
  await page.click('.menu button:has-text("Instellingen")');
  await page.waitForSelector('.settings');
}

/** The names and the chosen colours on the player setup, as "name/colour". */
const setupPlayers = () =>
  page.evaluate(() =>
    [...document.querySelectorAll('.setup-player')].map(
      row => `${row.querySelector('.name-input').value}/${row.querySelector('.swatch-button.selected')?.getAttribute('aria-label')}`,
    ),
  );

/** Reloads the page: what was set comes back, and a damaged value falls back to its own default. */
async function checkStored() {
  // 8. Sound off, volume 40, normal speed, medium quality and the two players of this game.
  await page.goto(url);
  await page.waitForSelector('.menu', { timeout: 60_000 });
  await openSettings();
  const chosen = () => page.evaluate(() => [...document.querySelectorAll('.settings .choice.selected')].map(c => c.textContent.trim()));
  await until('the settings were not remembered after a reload', () => document.querySelector('.settings output')?.textContent === '40');
  const remembered = await chosen();
  console.log('stored settings:', remembered.join(', '), '| volume', await page.innerText('.settings output'));
  if (remembered.join() !== 'Middel,Normaal,Uit') fail(`the settings were not remembered: ${remembered.join()}`);
  await page.click('.settings button.primary');
  await page.click('.menu button:has-text("Nieuw spel")');
  await page.waitForSelector('.setup');
  const players = await setupPlayers();
  console.log('stored players:', players.join(', '));
  if (players.join() !== 'Speler 1/Robijn,Bea/Amethist') fail(`the last players were not remembered: ${players.join()}`);

  // 9. A damaged volume and a damaged player list are the defaults; the other settings stay.
  await page.evaluate(() => {
    localStorage.setItem('triominos.volume', 'loud');
    localStorage.setItem('triominos.players', 'not what we wrote');
  });
  await page.goto(url);
  await page.waitForSelector('.menu', { timeout: 60_000 });
  await openSettings();
  await until('a damaged volume did not fall back to its default', () => document.querySelector('.settings output')?.textContent === '60');
  const afterDamage = await chosen();
  if (afterDamage.join() !== 'Middel,Normaal,Uit') fail(`a damaged volume changed another setting: ${afterDamage.join()}`);
  await page.click('.settings button.primary');
  await page.click('.menu button:has-text("Nieuw spel")');
  await page.waitForSelector('.setup');
  const defaults = await setupPlayers();
  if (defaults.join() !== 'Speler 1/Robijn,Speler 2/Saffier') fail(`a damaged player list did not fall back to the default players: ${defaults.join()}`);
}

try {
  await run();
} catch (e) {
  errors.push(`smoke: ${e.message.split('\n')[0]}`);
  console.log('page state:', JSON.stringify(await page.evaluate(() => ({ search: location.search, debugApi: !!window.__triominos, rack: window.__triominos?.rackTiles().length, text: document.body.innerText.slice(0, 400) })).catch(e => String(e))));
  console.log('browser log:\n' + log.slice(-25).join('\n'));
}

if (screenshot) {
  await page.screenshot({ path: screenshot, type: 'jpeg', quality: 60 });
}

await browser.close();

if (errors.length > 0) {
  console.error(errors.join('\n'));
  process.exit(1);
}
