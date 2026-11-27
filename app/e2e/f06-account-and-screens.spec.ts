import { test, expect, btn, login, tab } from './fixtures';

test('F06-N1 delete is blocked while there is a balance', async ({ page, a11y }) => {
  await login(page);
  await tab(page, 'حسابي').click();
  await a11y('S15 profile');
  await btn(page, 'حذف الحساب').click();
  await btn(page, 'احذف حسابي').click();
  await expect(page.getByText(/عندك رصيد/)).toBeVisible();
});

test('X-2 remaining screens pass axe', async ({ page, a11y }) => {
  await login(page);
  await tab(page, 'مشاركاتي').click(); await a11y('S08 mine');
  await tab(page, 'المتجر').click(); await expect(page.getByText('اصرف رصيد التسوق هون')).toBeVisible(); await a11y('S12 store');
  await tab(page, 'حسابي').click();
  await btn(page, 'المالكين').click(); await expect(page.getByText('ليث ع.')).toBeVisible(); await a11y('S11 owners');
  await page.goBack();
  await btn(page, 'المساعدة والشروط').click(); await a11y('S16 help');
});

test.describe('X-3 another time zone', () => {
  test.use({ timezoneId: 'America/New_York' });
  test('a fresh transaction is labelled today', async ({ page }) => {
    await login(page);
    await tab(page, 'المحفظة').click();
    await btn(page, 'اشحن').click();
    await page.getByRole('radio', { name: /بطاقة بنكية/ }).click();
    await page.getByRole('button', { name: /^ادفع/ }).click();
    await expect(page.getByText(/^اليوم · الرصيد/).first()).toBeVisible();
  });
});
