// Checks the loading screen and what the first visit downloads, on the published site: the screen is there
// while Blazor downloads and shows its progress, it is replaced by the app, it shows a message with a retry
// button when Blazor cannot start (the files are blocked) or when WebAssembly is missing, and says so when
// JavaScript is off. Also: the scene is fetched once and before the first WebAssembly file (so it loads next to
// them, not after), and no ICU data is downloaded.
// Usage: node loading.mjs <url> [loaded.json]  (loaded.json: the paths a first visit downloaded)
import { writeFileSync } from 'node:fs';
import { chromium } from 'playwright-core';

const base = process.argv[2] ?? 'http://localhost:8080/';
const loadedOut = process.argv[3];
const errors = [];
const fail = message => errors.push(`loading: ${message}`);

const browser = await chromium.launch({
  channel: 'chrome',
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
});

/** Path of a request relative to the site, without the query. */
const relative = url => new URL(url).pathname.slice(new URL(base).pathname.length);

async function normalLoad() {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  const requested = [];
  page.on('request', r => requested.push(relative(r.url())));
  page.on('pageerror', e => fail(`page error: ${e.message}`));
  // Slow the runtime down a little so the screen is there to be looked at.
  await page.route('**/_framework/dotnet.native.*.wasm', async route => {
    await new Promise(r => setTimeout(r, 1500));
    await route.continue();
  });

  await page.goto(base, { waitUntil: 'commit' });
  await page.waitForSelector('.loading', { timeout: 10_000 });
  const title = await page.innerText('.loading h1');
  if (title !== 'Triominos') fail(`the loading screen shows "${title}" as its title`);
  if (!(await page.isVisible('.loading-bar'))) fail('the loading screen shows no progress bar');

  // Blazor writes its progress to a CSS variable while it downloads.
  let percentage = '';
  try {
    await page.waitForFunction(() => getComputedStyle(document.documentElement).getPropertyValue('--blazor-load-percentage').trim() !== '', null, { timeout: 15_000 });
    percentage = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--blazor-load-percentage').trim());
  } catch {
    fail('Blazor never set --blazor-load-percentage, so the progress bar cannot move');
  }

  const shown = await page.evaluate(() => getComputedStyle(document.querySelector('.loading-percent'), '::after').content);
  console.log(`loading screen: percentage variable "${percentage}", text "${shown}"`);
  await page.waitForSelector('.menu', { timeout: 60_000 });
  if (await page.locator('.loading').count()) fail('the loading screen is still there when the menu shows');
  await page.waitForSelector('canvas.scene', { timeout: 30_000 });

  const scene = requested.filter(p => p === 'js/scene/scene.js');
  const firstWasm = requested.findIndex(p => p.startsWith('_framework/') && p.endsWith('.wasm'));
  const sceneAt = requested.indexOf('js/scene/scene.js');
  console.log(`first visit: ${requested.length} requests, scene.js requested ${scene.length}x (request ${sceneAt + 1}, first wasm at ${firstWasm + 1})`);
  if (scene.length !== 1) fail(`scene.js was requested ${scene.length} times, expected 1 (the preload and the import should share one fetch)`);
  if (sceneAt < 0 || sceneAt > firstWasm) fail('scene.js was requested after the first WebAssembly file: it should load next to them');
  const icu = requested.filter(p => p.includes('icudt'));
  if (icu.length > 0) fail(`ICU data was downloaded: ${icu.join(', ')}`);

  if (loadedOut) {
    writeFileSync(loadedOut, JSON.stringify([...new Set(requested.filter(p => p && !p.endsWith('/') && !p.endsWith('.json')))]));
  }

  await page.close();
}

async function blockedStart(label, pattern, expected) {
  const page = await browser.newPage({ viewport: { width: 390, height: 700 } });
  await page.route(pattern, route => route.abort());
  await page.goto(base);
  try {
    await page.waitForSelector('.loading.failed', { timeout: 30_000 });
    const text = await page.innerText('.loading');
    console.log(`${label}:`, text.replace(/\s+/g, ' '));
    if (!text.includes(expected)) fail(`${label}: the message does not say "${expected}"`);
    if (!(await page.isVisible('.loading-problem button'))) fail(`${label}: no retry button`);
    if (await page.locator('.loading-bar').count()) fail(`${label}: the progress bar is still shown`);
  } catch {
    fail(`${label}: no message showed on the loading screen`);
  }

  await page.close();
}

async function withoutJavaScript() {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto(base);
  const text = await page.innerText('.loading-card');
  console.log('no JavaScript:', text.replace(/\s+/g, ' '));
  if (!text.includes('JavaScript')) fail('without JavaScript the screen does not say it needs JavaScript');
  if (text.includes('geladen')) fail('without JavaScript the screen still promises that the game is loading');
  await context.close();
}

async function withoutWebAssembly() {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.addInitScript(() => { delete window.WebAssembly; });
  await page.goto(base);
  try {
    await page.waitForSelector('.loading.failed', { timeout: 10_000 });
    const text = await page.innerText('.loading');
    console.log('no WebAssembly:', text.replace(/\s+/g, ' '));
    if (!text.includes('WebAssembly')) fail('without WebAssembly the message does not mention it');
  } catch {
    fail('without WebAssembly no message showed');
  }

  await context.close();
}

try {
  await normalLoad();
  await blockedStart('files blocked', '**/_framework/*.wasm', 'mislukt');
  await blockedStart('blazor script blocked', '**/_framework/blazor.webassembly.js', 'mislukt');
  await withoutJavaScript();
  await withoutWebAssembly();
} catch (e) {
  fail(e.message.split('\n')[0]);
}

await browser.close();
if (errors.length > 0) {
  console.error(errors.join('\n'));
  process.exit(1);
}
