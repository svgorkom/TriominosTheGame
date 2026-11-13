// Smoke test of the published site: loads it in headless Chrome and plays real moves with the
// mouse (start tile, a normal move, drawing), failing on any JavaScript error, a missing scene or
// a move the game does not accept. Usage: node smoke.mjs <url> [screenshot.jpg]
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
page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
page.on('console', m => m.type() === 'error' && errors.push(`console: ${m.text()}`));

const fail = message => errors.push(`smoke: ${message}`);
const message = () => page.innerText('.message');
const pause = ms => page.waitForTimeout(ms);

async function startTurn() {
  await page.waitForSelector('.overlay button.primary', { timeout: 20_000 });
  await page.click('.overlay button.primary');
  await page.waitForFunction(() => window.__triominos?.rackTiles().length > 0, null, { timeout: 20_000 });
  await pause(500);
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
    await page.mouse.up();
    await pause(600);
    return true;
  }

  return false;
}

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
await page.keyboard.press('Space');
await pause(500);
const poolAfter = Number(await page.innerText('.pool-count'));
console.log('pool', poolBefore, '->', poolAfter, '|', await message());
if (poolAfter !== poolBefore - 1) fail('space did not draw a tile');

let played = await playAnyTile({ onlyMarked: false });
for (let draws = 0; !played && draws < 2; draws++) {
  await page.keyboard.press('Space');
  await pause(500);
  played = await playAnyTile({ onlyMarked: false });
}

console.log('second move:', await message());
if (played && !/legt/.test(await message())) fail(`second move not accepted: ${await message()}`);

// 3. Back to the first player, with the rack and board in view.
if (played) {
  await startTurn();
}

await pause(1500);
console.log('players:', (await page.innerText('.players')).replace(/\s+/g, ' '));
console.log('pool:', await page.innerText('.pool-count'));

if (screenshot) {
  await page.screenshot({ path: screenshot, type: 'jpeg', quality: 60 });
}

await browser.close();

if (errors.length > 0) {
  console.error(errors.join('\n'));
  process.exit(1);
}
