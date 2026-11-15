import { useState } from 'react';
import { View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import { Clock, Gift, PartyPopper, ShieldCheck } from 'lucide-react-native';
import { Button, ErrorState, ProductArt, Screen, Skeleton, SpotsProgress, Text } from '../../components/ui';
import { Sheet } from '../../components/sheet';
import { api } from '../../data';
import { useQuery } from '../../data/useQuery';
import { useSession } from '../../lib/session';
import { verify } from '../../data/fairness';
import { radius, space, useColors } from '../../theme';
import { formatJD } from '../../lib/money';
import { t } from '../../i18n';

// S09: the outcome of a closed product. Polls via the data layer's change events.
export default function Result() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const c = useColors();
  const { user } = useSession();
  const p = useQuery(() => api.getProduct(id), [id]);
  const a = useQuery(() => api.getAssignment(id), [id]);
  const parts = useQuery(() => api.listParticipations().then((xs) => xs.filter((x) => x.productId === id)), [id]);
  const [proof, setProof] = useState(false);

  if (p.error || a.error) return <Screen edges={[]}><ErrorState onRetry={() => { p.reload(); a.reload(); }} /></Screen>;
  if (!p.data || a.data === undefined) return <Screen edges={[]}><Skeleton height={160} r={radius.lg} /><Skeleton width="60%" /></Screen>;

  const product = p.data;
  const assignment = a.data;
  const paid = (parts.data ?? []).reduce((s, x) => s + x.amountFils, 0);
  const mySpots = (parts.data ?? []).flatMap((x) => x.spotNumbers);
  const won = !!assignment && assignment.ownerId === user?.id;
  const tookPart = mySpots.length > 0;

  const banner = (bg: string, Icon: typeof Gift, fg: string, title: string, body: string, action?: [string, () => void, 'primary' | 'secondary']) => (
    <View accessibilityLiveRegion="polite" style={{ backgroundColor: bg, borderRadius: radius.lg, padding: space(5), gap: space(3), alignItems: 'center' }}>
      <Icon color={fg} size={48} strokeWidth={1.5} />
      <Text variant="xl" style={{ textAlign: 'center' }} accessibilityRole="header">{title}</Text>
      <Text tone="text-muted" style={{ textAlign: 'center' }}>{body}</Text>
      {action ? <Button full size="lg" title={action[0]} onPress={action[1]} variant={action[2]} /> : null}
    </View>
  );

  return (
    <Screen edges={[]}>
      <View style={{ flexDirection: 'row', gap: space(3), alignItems: 'center' }}>
        <ProductArt category={product.category} size="thumb" />
        <Text variant="lg" style={{ flex: 1 }}>{product.titleAr}</Text>
      </View>
      {!assignment
        ? <>
            {banner(c.surface, Clock, c['text-muted'], t.result.pendingTitle, t.result.pendingBody)}
            <SpotsProgress filled={product.filledSpots} total={product.totalSpots} size="large" />
          </>
        : won
          ? banner(c['success-soft'], PartyPopper, c.success, t.result.ownerTitle, t.result.ownerBody(assignment.winningSpot),
              [t.result.address, () => router.push(`/order/${product.id}`), 'primary'])
          : tookPart
            ? banner(c['accent-soft'], Gift, c['accent-strong'], t.result.creditTitle(formatJD(paid)), t.result.creditBody,
                [t.result.shop, () => router.push('/(tabs)/stores'), 'secondary'])
            : banner(c.surface, ShieldCheck, c['text-muted'], t.product.assigned, `${t.result.winning} ⁦#${assignment.winningSpot}⁩`)}
      {tookPart ? <Text variant="sm" tone="text-muted">أرقام أماكنك: {mySpots.map((n) => `⁦#${n}⁩`).join('، ')}</Text> : null}
      {assignment ? <Button title={t.result.verify} variant="ghost" icon={<ShieldCheck color={c.accent} size={18} />} onPress={() => setProof(true)} /> : null}

      {assignment ? (
        <Sheet visible={proof} onClose={() => setProof(false)} title={t.result.verify}>
          {([[t.result.winning, `#${assignment.winningSpot} / ${product.totalSpots}`], [t.result.seedHash, product.seedHash],
             [t.result.seed, assignment.serverSeed], [t.result.beacon, `${assignment.beaconRound}: ${assignment.beaconValue}`],
             [t.result.formula, 'sha256(seed + beacon)[0..15] mod total + 1']] as const).map(([k, v]) => (
            <View key={k} style={{ gap: 2 }}>
              <Text variant="xs" tone="text-muted">{k}</Text>
              <Text variant="sm" selectable style={{ writingDirection: 'ltr', textAlign: 'left' }}>{v}</Text>
            </View>
          ))}
          <Text variant="sm" tone={verify(assignment, product.seedHash, product.totalSpots) ? 'success' : 'danger'}>
            {verify(assignment, product.seedHash, product.totalSpots) ? '✓ النتيجة مطابقة للمعادلة والبصمة المنشورة' : '✗ النتيجة غير مطابقة'}
          </Text>
        </Sheet>
      ) : null}
    </Screen>
  );
}
