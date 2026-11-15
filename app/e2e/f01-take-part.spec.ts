import { test, expect, btn, login, tab, visible } from './fixtures';
import type { Page } from '@playwright/test';

const openProduct = async (page: Page, name: RegExp) => {
  await visible(page, 'button', name).click();
  await expect(page.getByText('كم مكان بدك؟')).toBeVisible();
};
const cash = async (page: Page) => {
  await tab(page, 'المحفظة').click();
  await expect(page.getByText('الحركات')).toBeVisible();
  return (await page.getByText('الرصيد', { exact: true }).locator('xpath=..').innerText()).replace(/[⁦⁩]/g, '');
};

test.describe('F01 take part', () => {
  test.beforeEach(async ({ page }) => { await login(page); });

  test('F01-H1 buy the last 3 spots', async ({ page, a11y }) => {
    await openProduct(page, /هاتف ذكي برو/);
    await a11y('S04 product');
    await btn(page, 'زيادة').click(); await btn(page, 'زيادة').click();
    await page.getByRole('button', { name: /^احجز ·/ }).click();
    await expect(page.getByText('تأكيد الحجز')).toBeVisible();
    await expect(page.getByText('بعد الحجز')).toBeVisible();
    await a11y('S05 confirm');
    await btn(page, 'ادفع واحجز').click();
    await expect(page.getByText(/أرقام أماكنك/)).toContainText('#48');
    await expect(page.getByText(/أرقام أماكنك/)).toContainText('#50');
    await page.goBack(); await page.goBack();
    expect(await cash(page)).toContain('1.000');
  });

  test('F01-E1 double click on pay charges once', async ({ page }) => {
    await openProduct(page, /ساعة ذكية/);
    await page.getByRole('button', { name: /^احجز ·/ }).click();
    await expect(page.getByText('بعد الحجز')).toBeVisible();
    await btn(page, 'ادفع واحجز').dblclick();
    await expect(page.getByRole('tab', { name: 'جارية' })).toBeVisible();
    expect(await cash(page)).toContain('3.000');
    await tab(page, 'مشاركاتي').click();
    await expect(visible(page, 'button', /ساعة ذكية/)).toBeVisible();
    await expect(page.getByText(/^1 أماكن/)).toHaveCount(1);
  });

  test('F01-E2 stepper stops at the per-member limit and at spots left', async ({ page }) => {
    await openProduct(page, /ساعة ذكية/); // 48 left, limit 10
    for (let i = 0; i < 15; i++) if (await btn(page, 'زيادة').isEnabled()) await btn(page, 'زيادة').click();
    await expect(page.getByText('10', { exact: true })).toBeVisible();
    await expect(btn(page, 'زيادة')).toHaveAttribute('aria-disabled', 'true');
    await page.goBack();
    await openProduct(page, /هاتف ذكي برو/); // 3 left, limit 5
    for (let i = 0; i < 6; i++) if (await btn(page, 'زيادة').isEnabled()) await btn(page, 'زيادة').click();
    await expect(page.getByRole('button', { name: /^احجز ·/ })).toContainText('3.000');
  });

  test('F01-E3 balance too low offers a top-up and charges nothing', async ({ page }) => {
    await openProduct(page, /جهاز ألعاب/); // 2 JD per spot
    await btn(page, 'زيادة').click(); await btn(page, 'زيادة').click();
    await page.getByRole('button', { name: /^احجز ·/ }).click();
    await expect(page.getByText('رصيدك ما بيكفي')).toBeVisible();
    await expect(btn(page, 'اشحن المحفظة')).toBeVisible();
    await expect(btn(page, 'ادفع واحجز')).toHaveCount(0);
  });

  test('F01-E4 closing the sheet books nothing', async ({ page }) => {
    await openProduct(page, /سماعات/);
    await page.getByRole('button', { name: /^احجز ·/ }).click();
    await expect(page.getByText('تأكيد الحجز')).toBeVisible();
    await page.getByRole('button', { name: 'إلغاء' }).last().click();
    await expect(page.getByText('تأكيد الحجز')).toHaveCount(0);
    await page.goBack();
    expect(await cash(page)).toContain('4.000');
  });

  test('F01-E6 desktop width keeps content centred', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    const card = visible(page, 'button', /هاتف ذكي برو/);
    const box = (await card.boundingBox())!;
    expect(box.width).toBeLessThanOrEqual(640);
    expect(Math.abs(box.x + box.width / 2 - 720)).toBeLessThan(40);
  });
});
