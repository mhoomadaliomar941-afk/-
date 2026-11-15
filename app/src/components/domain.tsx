import { View } from 'react-native';
import { ArrowDownLeft, ArrowUpRight, Clock, Gift, ShoppingBag, Store as StoreIcon, Ticket } from 'lucide-react-native';
import { Card, ProductArt, productChip, Row, SpotsProgress, StatusChip, Text } from './ui';
import { radius, space, useColors } from '../theme';
import { formatJD } from '../lib/money';
import { t } from '../i18n';
import type { Product, Wallet, WalletTxn } from '../data';

const num = (n: number) => `⁦${n}⁩`;

export function ProductCard({ p, onPress }: { p: Product; onPress: () => void }) {
  const chip = productChip(p.status, p.filledSpots, p.totalSpots);
  const label = `${p.titleAr}، ${t.product.spotPrice} ${formatJD(p.spotPriceFils)}، ${t.product.spotsTaken(String(p.filledSpots), String(p.totalSpots))}`;
  return (
    <Card onPress={onPress} label={label}>
      <ProductArt category={p.category} />
      <Row style={{ justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <Text variant="lg" style={{ flex: 1 }} numberOfLines={2}>{p.titleAr}</Text>
        <StatusChip {...chip} />
      </Row>
      <Row style={{ justifyContent: 'space-between' }}>
        <Text variant="sm" tone="text-muted">{t.product.spotPrice}</Text>
        <Text weight={600}>{formatJD(p.spotPriceFils)}</Text>
      </Row>
      <SpotsProgress filled={p.filledSpots} total={p.totalSpots} />
      <Text variant="sm" tone="text-muted">{t.product.spotsTaken(num(p.filledSpots), num(p.totalSpots))}</Text>
    </Card>
  );
}

export function WalletBalanceCard({ w }: { w: Wallet }) {
  const c = useColors();
  const col = (label: string, fils: number, hint: string) => (
    <View style={{ flex: 1, gap: 2 }}>
      <Text variant="sm" tone="on-accent" style={{ opacity: 0.9 }}>{label}</Text>
      <Text variant="2xl" tone="on-accent" style={{ fontVariant: ['tabular-nums'] }}>{formatJD(fils)}</Text>
      <Text variant="xs" tone="on-accent" style={{ opacity: 0.85 }}>{hint}</Text>
    </View>
  );
  return (
    <View style={{ backgroundColor: c.accent, borderRadius: radius.xl, padding: space(5), flexDirection: 'row', gap: space(4) }}>
      {col(t.wallet.cash, w.cashFils, t.wallet.cashHint)}
      {col(t.wallet.shopping, w.shoppingFils, t.wallet.shoppingHint)}
    </View>
  );
}

const TX_ICON = { topup: ArrowDownLeft, spend: Ticket, credit: Gift, redeem: StoreIcon, refund: ArrowDownLeft, adjust: ArrowUpRight, purchase: ShoppingBag };
export function TransactionRow({ x }: { x: WalletTxn }) {
  const c = useColors();
  const Icon = x.status === 'pending' ? Clock : TX_ICON[x.type];
  const plus = x.amountFils > 0;
  const toneBg = x.status === 'pending' ? c['warning-soft'] : plus ? c['success-soft'] : c['accent-soft'];
  const toneFg = x.status === 'pending' ? c.warning : plus ? c.success : c['accent-strong'];
  const when = new Date(x.createdAt);
  const date = new Date().toDateString() === when.toDateString() ? t.common.today : when.toLocaleDateString('ar-JO', { day: 'numeric', month: 'short' });
  return (
    <Row gap={3} style={{ paddingVertical: space(2) + 2, borderBottomWidth: 1, borderBottomColor: c.border }}>
      <View style={{ width: 36, height: 36, borderRadius: radius.pill, backgroundColor: toneBg, alignItems: 'center', justifyContent: 'center' }}>
        <Icon color={toneFg} size={18} />
      </View>
      <View style={{ flex: 1 }}>
        <Text numberOfLines={1}>{t.wallet.types[x.type]}{x.label ? `: ${x.label}` : ''}</Text>
        <Text variant="sm" tone="text-muted">{date} · {x.bucket === 'cash' ? t.wallet.cash : t.wallet.shopping}</Text>
      </View>
      {x.status === 'pending' ? <StatusChip label={t.wallet.pending} tone="warning" />
        : <Text weight={600} tone={plus ? 'success' : 'text'} style={{ fontVariant: ['tabular-nums'] }}>{formatJD(x.amountFils, 'ar', true)}</Text>}
    </Row>
  );
}
