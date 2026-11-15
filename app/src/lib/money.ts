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
