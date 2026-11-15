import { test, expect, btn, login, tab, visible } from './fixtures';
import type { Page } from '@playwright/test';

// Demo member starts with 4 JD cash and 5 JD shopping balance.
async function openStoreProduct(page: Page, name: RegExp) {
  await login(page);
  await tab(page, 'المتجر').click();
  await expect(page.getByText('اصرف رصيد التسوق هون')).toBeVisible();
  await visible(page, 'button', name).click();
  await expect(page.getByText('الكمية')).toBeVisible();
}
const balances = async (page: Page) => {
  await tab(page, 'المحفظة').click();
  await expect(page.getByText('الحركات')).toBeVisible();
  return (await page.getByText('الرصيد', { exact: true }).locator('xpath=../..').innerText()).replace(/[⁦⁩]/g, '');
};

test.describe('F04 spend shopping balance in the store', () => {
  test('F04-H1 buy with shopping balance first, then cash; order appears in my orders', async ({ page, a11y }) => {
    await openStoreProduct(page, /شاحن سريع/); // 9 JD
    await a11y('store product');
    await page.getByRole('button', { name: /^اشترِ ·/ }).click();
    await expect(page.getByText('تأكيد الشراء')).toBeVisible();
    await expect(page.getByText('من رصيد التسوق').first().locator('xpath=..')).toContainText('5.000');
    await expect(page.getByText('من الرصيد', { exact: true }).locator('xpath=..')).toContainText('4.000');
    // first purchase: add an address inside the sheet
    await page.getByLabel('المحافظة').fill('عمّان'); await page.getByLabel('المدينة').fill('عمّان');
    await page.getByLabel('الشارع والبناية').fill('شارع ١'); await page.getByLabel('رقم للتواصل').fill('0791234567');
    await btn(page, 'احفظ العنوان').click();
    await expect(page.getByRole('radio', { name: /شارع ١/ })).toHaveAttribute('aria-checked', 'true');
    await a11y('checkout');
    await btn(page, 'أكّد الشراء').click();
    await expect(page.getByText(/شاحن سريع.*×\s*\u2066?1/)).toBeVisible();
    await expect(page.getByText('قيد التجهيز')).toBeVisible();
    await a11y('my orders');
    await page.goBack(); await page.goBack();
    const b = await balances(page);
    expect(b).toContain('0.000');
  });

  test('F04-N1 not enough balance: no purchase, offers a top-up', async ({ page }) => {
    await openStoreProduct(page, /شاحن سريع/);
    await btn(page, 'زيادة').click(); // 18 JD > 9 JD total balance
    await page.getByRole('button', { name: /^اشترِ ·/ }).click();
    await expect(page.getByText('رصيدك ما بيكفي لهالطلب')).toBeVisible();
    await expect(btn(page, 'اشحن المحفظة')).toBeVisible();
    await expect(btn(page, 'أكّد الشراء')).toHaveCount(0);
  });

  test('F04-E1 stock limits: out of stock can\'t be bought, quantity stops at stock', async ({ page }) => {
    await login(page);
    await tab(page, 'المتجر').click();
    await visible(page, 'button', /مج حراري/).click();
    await expect(page.getByRole('button', { name: 'خلص' })).toHaveAttribute('aria-disabled', 'true');
    await page.goBack();
    await visible(page, 'button', /سماعات أذن سلكية/).click(); // 3 in stock
    for (let i = 0; i < 5; i++) if (await btn(page, 'زيادة').isEnabled()) await btn(page, 'زيادة').click();
    await expect(page.getByRole('button', { name: /^اشترِ ·/ })).toContainText('18.000');
  });

  test('F04-E2 double click on confirm makes one order', async ({ page }) => {
    await openStoreProduct(page, /كفر جوال/); // 4.5 JD
    await page.getByRole('button', { name: /^اشترِ ·/ }).click();
    await page.getByLabel('المحافظة').fill('عمّان'); await page.getByLabel('المدينة').fill('عمّان');
    await page.getByLabel('الشارع والبناية').fill('شارع ٢'); await page.getByLabel('رقم للتواصل').fill('0791234567');
    await btn(page, 'احفظ العنوان').click();
    await btn(page, 'أكّد الشراء').dblclick();
    // only the orders list writes "×": one line means one order
    await expect(page.getByText(/كفر جوال شفاف ×/)).toHaveCount(1);
  });
});
