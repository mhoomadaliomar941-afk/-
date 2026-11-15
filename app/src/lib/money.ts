// All amounts are integer fils (1 JD = 1000 fils). Never use floats for money.
export function formatAmount(fils: number): string {
  const sign = fils < 0 ? '−' : '';
  const abs = Math.abs(fils);
  return `${sign}${Math.floor(abs / 1000)}.${String(abs % 1000).padStart(3, '0')}`;
}

/** "1.000 د.ا" — LTR isolate around the sign and digits, so they never reorder in Arabic text.
 *  showPlus: "+5.000 د.ا" for credits in a ledger. */
export function formatJD(fils: number, lang: 'ar' | 'en' = 'ar', showPlus = false): string {
  const n = `⁦${showPlus && fils > 0 ? '+' : ''}${formatAmount(fils)}⁩`;
  return lang === 'ar' ? `${n} د.ا` : `JD ${n}`;
}

export const JD = 1000;

/** "12.5" or "12.500" dinars -> 12500 fils; null for anything that isn't a positive amount with up to 3 decimals. */
export function parseJD(input: string): number | null {
  const s = input.trim().replace(',', '.');
  if (!/^\d{1,6}(\.\d{1,3})?$/.test(s)) return null;
  const [whole, frac = ''] = s.split('.');
  const fils = Number(whole) * 1000 + Number(frac.padEnd(3, '0'));
  return fils > 0 ? fils : null;
}
export const filsToInput = (fils: number) => (fils % 1000 === 0 ? String(fils / 1000) : formatAmount(fils));

/** Jordanian mobile from whatever the person typed or pasted: "+962 79 123 4567",
 *  "00962791234567", "0791234567", "79 123 4567" -> "79 123 4567" (max 9 digits, grouped). */
export function normalizeJoMobile(input: string): string {
  let d = input.replace(/[^\d]/g, '');
  if (d.startsWith('00962')) d = d.slice(5);
  else if (d.startsWith('962')) d = d.slice(3);
  if (d.startsWith('0')) d = d.slice(1);
  d = d.slice(0, 9);
  return [d.slice(0, 2), d.slice(2, 5), d.slice(5)].filter(Boolean).join(' ');
}
