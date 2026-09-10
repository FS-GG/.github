import { chromium } from '@playwright/test';

const [bootstrapUrl, expectedWorkspace, expectedItem, action = 'logout'] = process.argv.slice(2);

if (!bootstrapUrl || !expectedWorkspace) {
  throw new Error('expected bootstrap URL and workspace ID');
}

const browser = await chromium.launch({ headless: true });

try {
  const context = await browser.newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(5000);
  const response = await page.goto(bootstrapUrl);

  if (!response || response.status() !== 200) {
    throw new Error(`bootstrap navigation ended with ${response?.status()}`);
  }

  await page.locator('#workspace').waitFor({ state: 'attached' });
  await page.waitForFunction(value => document.querySelector('#workspace')?.textContent === value, expectedWorkspace);

  if (expectedItem) {
    await page.getByRole('listitem').filter({ hasText: expectedItem }).waitFor({ state: 'visible' });
    await page.locator('#health').filter({ hasText: '1 applied receipts' }).waitFor({ state: 'visible' });
  }

  if (page.url().includes('/bootstrap/')) {
    throw new Error('one-use bootstrap capability remained in the browser URL');
  }

  if ((await page.evaluate(() => document.cookie)) !== '') {
    throw new Error('HttpOnly session was visible to page script');
  }

  if (action === 'expire') {
    await page.waitForTimeout(400);
    await page.locator('#workspace').evaluate(node => node.dispatchEvent(new Event('change')));
    await page.locator('#local-ended').filter({ hasText: 'Restart telemetry dashboard serve for a fresh URL.' }).waitFor({ state: 'visible' });
    if (await page.locator('#login').isVisible()) throw new Error('host access-key form was shown in local mode');
  } else {
    const logoutStatus = expectedItem ? await Promise.all([
    page.waitForResponse(response => response.url().endsWith('/api/logout')),
    page.locator('#logout').click()
  ]).then(([response]) => response.status()) : await page.evaluate(async () => {
    const response = await fetch('/api/logout', { method: 'POST' });
    return response.status;
  });

    if (logoutStatus !== 204) {
      throw new Error(`logout returned ${logoutStatus}`);
    }

    if (expectedItem) {
      await page.locator('#local-ended').filter({ hasText: 'Restart telemetry dashboard serve for a fresh URL.' }).waitFor({ state: 'visible' });
      if (await page.locator('#login').isVisible()) throw new Error('host access-key form was shown in local mode');
    }

    const afterLogout = await page.goto(new URL('/', bootstrapUrl).href);

    if (!afterLogout || afterLogout.status() !== 401) {
      throw new Error(`revoked browser session returned ${afterLogout?.status()}`);
    }
  }
} finally {
  await browser.close();
}
