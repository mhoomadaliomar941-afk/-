import { test, expect, btn, login, tab, visible } from './fixtures';

test('F02 owner picked: pending, then result, proof and one notification', async ({ page, a11y }) => {
  await login(page);
  await visible(page, 'button', /هاتف ذكي برو/).click();
  await btn(page, 'زيادة').click(); await btn(page, 'زيادة').click();
  await page.getByRole('button', { name: /^احجز ·/ }).click();
  await expect(page.getByText('بعد الحجز')).toBeVisible();
  await btn(page, 'ادفع واحجز').click();
  // F02-E2 before the pick
  await expect(page.getByText('لسا ما اخترنا المالك')).toBeVisible();
  // F02-H1 updates by itself
  await expect(page.getByText(/مبروك! صار المنتج إلك|رجعنالك/)).toBeVisible({ timeout: 10_000 });
  await a11y('S09 result');
  // F02-E1 proof
  await btn(page, 'تحقق من النتيجة').click();
  await expect(page.getByText('✓ النتيجة مطابقة للمعادلة والبصمة المنشورة')).toBeVisible();
  await a11y('S09 verify');
  await page.getByRole('button', { name: 'إلغاء' }).last().click();
  // F02-E3 one notification for this product
  await page.goBack(); await page.goBack();
  await tab(page, 'حسابي').click();
  await btn(page, 'الإشعارات').click();
  await expect(page.getByRole('button', { name: /هاتف ذكي برو/ })).toHaveCount(1);
  await a11y('S14 notifications');
});
