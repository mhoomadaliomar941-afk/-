import { test, expect, btn, login, tab, visible } from './fixtures';
import type { Page } from '@playwright/test';

const shopping = (page: Page) => page.getByText(/رصيد التسوق:/).locator('visible=true').last();

async function openStore(page: Page) {
  await login(page);
  await tab(page, 'المتاجر').click();
  await expect(shopping(page)).toContainText('5.000');
  await visible(page, 'button', /سوبرماركت/).click();
  await expect(btn(page, 'أنشئ رمز')).toBeVisible();
}

test.describe('F04 spend shopping balance', () => {
  test('F04-H1 code with countdown, balance held', async ({ page, a11y }) => {
    await openStore(page);
    await a11y('S13 redeem');
    await page.getByLabel('المبلغ').fill('1.5');
    await btn(page, 'أنشئ رمز').click();
    await expect(page.getByText(/ورّي الرمز للكاشير/)).toBeVisible();
    await expect(page.getByText(/ينتهي خلال/)).toBeVisible();
    await expect(shopping(page)).toContainText('3.500');
    await a11y('S13 code');
  });

  test('F04-N1 more than the balance is refused', async ({ page }) => {
    await openStore(page);
    await page.getByLabel('المبلغ').fill('999');
    await btn(page, 'أنشئ رمز').click();
    await expect(page.getByText('رصيد التسوق ما بيكفي')).toBeVisible();
    await expect(shopping(page)).toContainText('5.000');
  });

  test('F04-E1 unused code expires after 15 minutes and the balance comes back', async ({ page }) => {
    await page.clock.install();
    await openStore(page);
    await page.getByLabel('المبلغ').fill('2');
    await btn(page, 'أنشئ رمز').click();
    await expect(shopping(page)).toContainText('3.000');
    await page.clock.runFor(901_000);
    await expect(page.getByText('انتهى الرمز')).toBeVisible();
    await page.clock.runFor(61_000); // the expiry job runs every minute
    await expect(shopping(page)).toContainText('5.000');
  });

  test('F04-E2 BUG-007 an expired code is fetched once, not on every render', async ({ page }) => {
    await page.clock.install();
    await openStore(page);
    await page.getByLabel('المبلغ').fill('1');
    await btn(page, 'أنشئ رمز').click();
    await expect(page.getByText(/ينتهي خلال/)).toBeVisible();
    // runFor fires timers in order: we land just after expiry, before the minute job has run.
    await page.clock.runFor(901_000);
    await expect(page.getByText('انتهى الرمز')).toBeVisible();
    await page.clock.runFor(5_000);
    const n = await page.evaluate(() => (globalThis as unknown as { __fakeCalls: { getRedemption: number } }).__fakeCalls.getRedemption);
    expect(n).toBeLessThanOrEqual(2);
  });
});
