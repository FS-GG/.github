import { chromium } from '@playwright/test';

const [bootstrapUrl, expectedWorkspace] = process.argv.slice(2);

if (!bootstrapUrl || !expectedWorkspace) {
  throw new Error('expected bootstrap URL and workspace ID');
}

const browser = await chromium.launch({ headless: true });

try {
  const context = await browser.newContext();
  const page = await context.newPage();
  const response = await page.goto(bootstrapUrl);

  if (!response || response.status() !== 200) {
    throw new Error(`bootstrap navigation ended with ${response?.status()}`);
  }

  await page.locator('#workspace').waitFor({ state: 'attached' });
  await page.waitForFunction(value => document.querySelector('#workspace')?.textContent === value, expectedWorkspace);

  if (page.url().includes('/bootstrap/')) {
    throw new Error('one-use bootstrap capability remained in the browser URL');
  }

  if ((await page.evaluate(() => document.cookie)) !== '') {
    throw new Error('HttpOnly session was visible to page script');
  }

  const logoutStatus = await page.evaluate(async () => {
    const response = await fetch('/api/logout', { method: 'POST' });
    return response.status;
  });

  if (logoutStatus !== 204) {
    throw new Error(`logout returned ${logoutStatus}`);
  }

  const afterLogout = await page.goto(new URL('/', bootstrapUrl).href);

  if (!afterLogout || afterLogout.status() !== 401) {
    throw new Error(`revoked browser session returned ${afterLogout?.status()}`);
  }
} finally {
  await browser.close();
}
