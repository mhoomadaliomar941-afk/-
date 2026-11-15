import { test, expect, btn, login, tab } from './fixtures';

test.describe('F05 top up', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
    await tab(page, 'المحفظة').click();
    await btn(page, 'اشحن').click();
    await expect(page.getByText('طريقة الدفع')).toBeVisible();
  });

  test('F05-H1 5 JD by card lands in the balance and the ledger', async ({ page, a11y }) => {
    await a11y('S06 top-up');
    await page.getByRole('button', { name: /^ادفع/ }).click();
    await expect(page.getByText('الحركات')).toBeVisible();
    await expect(page.getByText('الرصيد', { exact: true }).locator('xpath=..')).toContainText('9.000');
    await expect(page.getByText(/\+5\.000/).first()).toBeVisible();
    await a11y('S07 wallet');
  });

  test('F05-E1 other amount', async ({ page }) => {
    await page.getByLabel('مبلغ آخر').fill('7');
    await expect(page.getByRole('button', { name: /^ادفع/ })).toContainText('7.000');
  });

  test('F05-E2 unavailable method cannot be chosen', async ({ page }) => {
    const cliq = page.getByRole('radio', { name: /كليك/ });
    await expect(cliq).toHaveAttribute('aria-disabled', 'true');
    await cliq.click({ force: true });
    await expect(page.getByRole('radio', { name: /بطاقة بنكية/ })).toHaveAttribute('aria-checked', 'true');
  });
});
