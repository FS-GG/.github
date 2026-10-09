// Real dashboard/browser consumes the fresh CLI-produced projection.
const fs = require('fs');
const http = require('http');
const path = require('path');
const { chromium, expect } = require('../telemetry-dashboard/node_modules/@playwright/test');

(async () => {
  const root = path.resolve(__dirname, '../../telemetry-dashboard');
  const privateRoot = process.argv[2];
  const dashboard = fs.readFileSync(path.join(privateRoot, 'dashboard.json'));
  const errors = [];
  const server = http.createServer((request, response) => {
    const pathname = new URL(request.url, 'http://localhost').pathname;
    if (pathname === '/data/dashboard.json') {
      response.setHeader('content-type', 'application/json'); response.end(dashboard); return;
    }
    const files = {'/':'index.html','/index.html':'index.html','/app.js':'app.js','/efficiency.js':'efficiency.js','/styles.css':'styles.css'};
    const selected = files[pathname];
    if (!selected) { response.statusCode = 404; response.end(); return; }
    response.setHeader('content-type', selected.endsWith('.js') ? 'text/javascript' : selected.endsWith('.css') ? 'text/css' : 'text/html');
    response.end(fs.readFileSync(path.join(root, selected)));
  });
  let browser;
  try {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    browser = await chromium.launch({headless:true});
    const page = await browser.newPage();
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    await expect(page.locator('#local-content')).toContainText('14 / wal');
    await expect(page.locator('#local-content')).toContainText('unknown');
    await expect(page.locator('#error')).not.toBeVisible();
    if (errors.length) throw new Error(JSON.stringify(errors));
    const text = await page.locator('#local-content').innerText();
    fs.writeFileSync(path.join(privateRoot,'browser-result.json'), JSON.stringify({
      evidenceKind:'synthetic-test-events', renderedCanonicalHost:true,
      unknownCoverageVisible:true, pageErrors:errors, localText:text
    },null,2)+'\n');
    await page.screenshot({path:path.join(privateRoot,'browser.png'),fullPage:true});
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
