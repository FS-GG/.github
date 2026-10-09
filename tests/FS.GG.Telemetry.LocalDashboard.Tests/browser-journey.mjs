import { chromium } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const [bootstrapUrl, expectedWorkspace, expectedItem, action = 'logout', proofDirectory] = process.argv.slice(2);

if (!bootstrapUrl || !expectedWorkspace) {
  throw new Error('expected bootstrap URL and workspace ID');
}

const browser = await chromium.launch({ headless: true });

try {
  const context = await browser.newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(5000);
  const snapshotResponse = proofDirectory ? page.waitForResponse(response => response.url().endsWith('/api/snapshot') && response.status() === 200) : null;
  const response = await page.goto(bootstrapUrl);

  if (!response || response.status() !== 200) {
    throw new Error(`bootstrap navigation ended with ${response?.status()}`);
  }

  await page.locator('#workspace').waitFor({ state: 'attached' });
  await page.waitForFunction(value => document.querySelector('#workspace')?.textContent === value, expectedWorkspace);

  if (expectedItem) {
    await page.getByRole('listitem').filter({ hasText: expectedItem }).waitFor({ state: 'visible' });
    if (!proofDirectory) await page.locator('#health').filter({ hasText: '1 applied receipts' }).waitFor({ state: 'visible' });
  }

  if (page.url().includes('/bootstrap/')) {
    throw new Error('one-use bootstrap capability remained in the browser URL');
  }

  if ((await page.evaluate(() => document.cookie)) !== '') {
    throw new Error('HttpOnly session was visible to page script');
  }

  if (proofDirectory) {
    const snapshot = await (await snapshotResponse).json();
    if (snapshot.workspaceId !== expectedWorkspace || !snapshot.items.some(item => item.id === expectedItem)) {
      throw new Error('operational snapshot is not the exact scoped item');
    }
    const item = snapshot.items.find(item => item.id === expectedItem);
    if (item.state.outcome !== 'delivered' || !item.steps || item.steps.ciStepCount < 1) {
      throw new Error('genuine delivered CI steps are absent');
    }
    if (snapshot.operational.pendingBatches !== 0 || snapshot.operational.appliedReceipts < 1 || snapshot.operational.rejectedReceipts !== 0) {
      throw new Error('operational receipts are not applied and drained');
    }
    const card = page.getByRole('listitem').filter({ hasText: expectedItem });
    await card.locator('details').evaluate(node => { node.open = true; });
    await card.getByText(/CI steps/, { exact: false }).first().waitFor({ state: 'visible' });
    const visibleText = await card.innerText();
    if (!visibleText.includes('delivered') || !visibleText.includes('unknown')) {
      throw new Error('native delivery or unknown coverage was not rendered');
    }
    fs.writeFileSync(path.join(proofDirectory, 'snapshot.json'), JSON.stringify(snapshot, null, 2)+'\n');
    fs.writeFileSync(path.join(proofDirectory, 'browser-proof.json'), JSON.stringify({
      evidenceKind:'genuine-operational-ci', workspaceId:expectedWorkspace,
      itemId:expectedItem, delivered:true, ciSteps:item.steps.ciStepCount,
      unknownCoverageVisible:true, visibleText
    }, null, 2)+'\n');
    await page.screenshot({path:path.join(proofDirectory, 'dashboard.png'), fullPage:true});
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
