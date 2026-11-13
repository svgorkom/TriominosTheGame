// Smoke test of the published site: loads it in headless Chrome and plays real moves with the
// mouse (the deal, start tile, a normal move, drawing), failing on any JavaScript error, a missing
// scene, a move the game does not accept, or animations that do not run or do not finish. Moves
// are animated, so the test waits for the scene to settle after every action.
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
  await page.waitForSelector('.overlay', { timeout: 60_000 });
  await pause(2500);
  console.log('start:', (await page.innerText('.overlay')).replace(/\s+/g, ' '));

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

  // 4. An idle table draws no frames: render on demand.
  const frames = await page.evaluate(() => window.__triominos.frames());
  await pause(1500);
  const idleFrames = (await page.evaluate(() => window.__triominos.frames())) - frames;
  console.log('frames drawn while idle:', idleFrames, '| draw calls:', await page.evaluate(() => window.__triominos.drawCalls()));
  if (idleFrames > 1) fail(`the idle table kept drawing frames (${idleFrames})`);

  const names = await logNames();
  console.log('animations:', ['deal', 'placed', 'landed', 'drawn', 'ready'].map(n => `${n}=${count(names, n)}`).join(' '));
  if (count(names, 'deal') < 1) fail('the deal was not animated');
  console.log('players:', (await page.innerText('.players')).replace(/\s+/g, ' '));
  console.log('pool:', await page.innerText('.pool-count'));
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
