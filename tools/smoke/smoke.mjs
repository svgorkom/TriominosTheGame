// Smoke test of the published site: loads it in headless Chrome, plays demo moves and fails on
// any JavaScript error or a missing scene. Usage: node smoke.mjs <url> [screenshot.jpg]
import { chromium } from 'playwright-core';

const url = process.argv[2] ?? 'http://localhost:8080/';
const screenshot = process.argv[3];

const browser = await chromium.launch({
  channel: 'chrome',
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
});
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
const errors = [];
page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
page.on('console', m => m.type() === 'error' && errors.push(`console: ${m.text()}`));

await page.goto(url);
await page.waitForSelector('canvas.scene', { timeout: 60_000 });
await page.waitForSelector('.message', { timeout: 60_000 });
await page.waitForTimeout(3000);

const next = page.getByRole('button', { name: 'Volgende zet' });
for (let i = 0; i < 10 && (await next.isEnabled()); i++) {
  await next.click();
  await page.waitForTimeout(250);
}

await page.waitForTimeout(1500);
console.log('message:', await page.innerText('.message'));
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
