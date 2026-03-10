import { chromium } from 'playwright';
import fs from 'node:fs/promises';
import path from 'node:path';

const [svgPath, assetsDir] = process.argv.slice(2);

if (!svgPath || !assetsDir) {
  throw new Error('Usage: node render-svg-assets.mjs <svgPath> <assetsDir>');
}

const sourceUrl = `file:///${svgPath.replace(/\\/g, '/')}`;

const targets = [
  ['Square150x150Logo.scale-200.png', 300, 300],
  ['Square44x44Logo.scale-200.png', 88, 88],
  ['Square44x44Logo.targetsize-24_altform-unplated.png', 24, 24],
  ['StoreLogo.png', 50, 50],
  ['LockScreenLogo.scale-200.png', 48, 48],
  ['Wide310x150Logo.scale-200.png', 620, 300],
  ['SplashScreen.scale-200.png', 1240, 600],
];

const browser = await chromium.launch({ headless: true });

try {
  for (const [name, width, height] of targets) {
    const page = await browser.newPage({
      viewport: { width, height },
      deviceScaleFactor: 1,
    });

    const html = `
      <!doctype html>
      <html>
        <body style="margin:0;display:grid;place-items:center;width:100vw;height:100vh;background:transparent;overflow:hidden;">
          <img src="${sourceUrl}" style="display:block;max-width:100%;max-height:100%;width:100%;height:100%;object-fit:contain;" />
        </body>
      </html>`;

    await page.setContent(html, { waitUntil: 'load' });
    await page.screenshot({
      path: path.join(assetsDir, name),
      omitBackground: true,
    });
    await page.close();
  }
} finally {
  await browser.close();
}

await fs.access(assetsDir);
