// Plays the published site with a touch screen (Chrome's touch emulation, real touch events through the
// DevTools protocol): tap a tile to carry it and tap the table to place it, drag a tile with one finger,
// draw with a tap on the button, and move the camera with one finger (pan) and two fingers (pinch and twist).
// A tablet has no R key and no wheel, so rotating a carried tile is not possible by touch; this test turns it
// with the keyboard where a tile needs it and says in the log when that was needed.
// Usage: node touch.mjs <url>
import { chromium } from 'playwright-core';

const base = process.argv[2] ?? 'http://localhost:8080/';
const url = `${base}${base.includes('?') ? '&' : '?'}seed=7&debug`;
const errors = [];
const fail = message => errors.push(`touch: ${message}`);

const browser = await chromium.launch({
  channel: 'chrome',
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
});
const context = await browser.newContext({ viewport: { width: 1280, height: 800 }, hasTouch: true });
const page = await context.newPage();
page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
page.on('console', m => m.type() === 'error' && errors.push(`console: ${m.text()}`));
const cdp = await context.newCDPSession(page);

const pause = ms => page.waitForTimeout(ms);
const api = fn => page.evaluate(fn);
const settle = () => page.waitForFunction(() => !window.__triominos.busy(), null, { timeout: 60_000 });
const logCount = async name => (await page.evaluate(() => window.__triominos.log().map(e => e.name))).filter(n => n === name).length;
let rotatedByKey = 0;

/** A raw touch event: points are [x, y] pairs, one per finger. */
const touch = (type, points = []) =>
  cdp.send('Input.dispatchTouchEvent', { type, touchPoints: points.map(([x, y], id) => ({ x, y, id })) });

async function tap([x, y]) {
  await touch('touchStart', [[x, y]]);
  await pause(60);
  await touch('touchEnd');
  await pause(250);
}

async function swipe(from, to, steps = 8) {
  await touch('touchStart', [from]);
  for (let i = 1; i <= steps; i++) {
    await touch('touchMove', [[from[0] + ((to[0] - from[0]) * i) / steps, from[1] + ((to[1] - from[1]) * i) / steps]]);
    await pause(30);
  }

  await pause(200);
  await touch('touchEnd');
  await pause(300);
}

const held = () => page.evaluate(() => window.__triominos.heldInfo());
const cellAt = (row, col) => page.evaluate(([r, c]) => window.__triominos.cellScreen(r, c), [row, col]);

/** Turns the carried tile to the rotation that fits; touch cannot do this, so the keyboard does. */
async function rotateTo(rotation) {
  for (let i = 0; i < 3 && (await held()).rotation !== rotation; i++) {
    await page.keyboard.press('r');
    await pause(150);
    rotatedByKey++;
  }
}

/** Waits for a move to finish: animated, then the next screen. */
async function finishMove() {
  await page.waitForFunction(() => window.__triominos.busy(), null, { timeout: 5_000, polling: 'raf' });
  await page.waitForSelector('.overlay', { timeout: 60_000 });
  await settle();
}

async function startTurn() {
  await page.waitForSelector('.overlay button.primary', { timeout: 15_000 });
  await page.tap('.overlay button.primary');
  await page.waitForFunction(() => window.__triominos?.rackTiles().length > 0, null, { timeout: 15_000 });
  await settle();
}

/** Carries the first tile that fits somewhere by a tap; returns what is held, or null. */
async function carryByTap({ onlyMarked }) {
  for (const tile of (await api(() => window.__triominos.rackTiles())).filter(t => !onlyMarked || t.marked)) {
    await tap([tile.x, tile.y]);
    await pause(400);
    const info = await held();
    if (info && info.fits.length > 0) return info;
    await page.keyboard.press('Escape');
    await pause(200);
  }

  return null;
}

async function run() {
  await page.goto(url);
  await page.waitForSelector('.menu', { timeout: 60_000 });
  await page.tap('.menu button:has-text("Nieuw spel")');
  await page.waitForSelector('.setup');
  await page.tap('.setup button.primary');
  await page.waitForSelector('.setup', { state: 'detached', timeout: 15_000 });

  // 1. The first player carries a starting tile with a tap and places it with a tap.
  await startTurn();
  const info = await carryByTap({ onlyMarked: true });
  if (!info) {
    fail('a tap on a starting tile did not carry it');
    return;
  }

  const target = info.fits[0];
  const cell = await cellAt(target.row, target.col);
  if (target.rotation !== info.rotation) {
    // Tapping a cell where the tile does not fit in this rotation leaves it in the hand.
    await tap([cell.x, cell.y]);
    if (!(await held())) fail('a tap on a cell where the tile does not fit placed it anyway');
    await rotateTo(target.rotation);
  }

  const placedBefore = await logCount('placed');
  await tap([cell.x, cell.y]);
  await finishMove();
  if ((await logCount('placed')) <= placedBefore) fail('a tap on the table did not place the carried tile');
  console.log('tap-carry, tap-place:', await page.innerText('.message'));

  // 2. The second player draws with a tap on the button, then drags a tile with one finger.
  await startTurn();
  const poolBefore = Number(await page.innerText('.pool-count'));
  await page.tap('.actions button:has-text("Trekken")');
  await page.waitForFunction(n => Number(document.querySelector('.pool-count').textContent) < n, poolBefore, { timeout: 10_000 }).catch(() => fail('a tap on Trekken did not draw'));
  await settle();

  let dragged = false;
  for (let draws = 0; !dragged && draws < 3; draws++) {
    for (const tile of await api(() => window.__triominos.rackTiles())) {
      await touch('touchStart', [[tile.x, tile.y]]);
      for (let i = 1; i <= 7; i++) {
        await touch('touchMove', [[tile.x, tile.y - 14 * i]]);
        await pause(30);
      }

      await pause(400);
      const now = await held();
      if (!now || now.fits.length === 0) {
        await touch('touchEnd');
        await page.keyboard.press('Escape');
        await pause(200);
        continue;
      }

      const spot = now.fits[0];
      await rotateTo(spot.rotation);
      const to = await cellAt(spot.row, spot.col);
      const from = [tile.x, tile.y - 14 * 7];
      for (let i = 1; i <= 8; i++) {
        await touch('touchMove', [[from[0] + ((to.x - from[0]) * i) / 8, from[1] + ((to.y - from[1]) * i) / 8]]);
        await pause(30);
      }

      await pause(250);
      const before = await logCount('placed');
      await touch('touchEnd');
      await finishMove();
      if ((await logCount('placed')) <= before) fail('dragging a tile with one finger did not place it');
      dragged = true;
      break;
    }

    if (!dragged) {
      await page.tap('.actions button:has-text("Trekken")');
      await pause(500);
      await settle();
    }
  }

  console.log('one-finger drag:', dragged ? await page.innerText('.message') : 'no tile fitted');

  // 3. The camera: one finger pans, two fingers pinch and twist.
  await startTurn();
  const before = await cellAt(0, 0);
  await swipe([640, 300], [540, 260]);
  const panned = await cellAt(0, 0);
  const pan = Math.hypot(panned.x - before.x, panned.y - before.y);
  console.log('one-finger pan moved the board', Math.round(pan), 'px');
  if (pan < 10) fail('one finger did not pan the camera');

  await touch('touchStart', [[500, 300], [780, 300]]);
  for (let i = 1; i <= 8; i++) {
    await touch('touchMove', [[500 - 10 * i, 300 - 8 * i], [780 + 10 * i, 300 + 8 * i]]);
    await pause(30);
  }

  await touch('touchEnd');
  await pause(800);
  const twisted = await cellAt(0, 0);
  const pinch = Math.hypot(twisted.x - panned.x, twisted.y - panned.y);
  console.log('two-finger pinch and twist moved the board', Math.round(pinch), 'px');
  if (pinch < 10) fail('two fingers did not move the camera');

  console.log(`rotations done with the keyboard: ${rotatedByKey} (touch has no way to rotate a carried tile)`);
}

try {
  await run();
} catch (e) {
  fail(e.message.split('\n')[0]);
  console.log('page text:', (await page.evaluate(() => document.body.innerText.slice(0, 300)).catch(() => '')).replace(/\s+/g, ' '));
}

await browser.close();
if (errors.length > 0) {
  console.error(errors.join('\n'));
  process.exit(1);
}
