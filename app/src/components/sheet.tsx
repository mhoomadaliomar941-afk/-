import { ReactNode } from 'react';
import { Modal, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { X } from 'lucide-react-native';
import { Row, Text } from './ui';
import { radius, space, useColors } from '../theme';
import { t } from '../i18n';

export function StickyBar({ children }: { children: ReactNode }) {
  const c = useColors();
  const insets = useSafeAreaInsets();
  return (
    <View style={{ position: 'absolute', bottom: 0, start: 0, end: 0, padding: space(4), paddingBottom: space(4) + insets.bottom,
      backgroundColor: c['surface-raised'], borderTopWidth: 1, borderTopColor: c.border }}>
      <View style={{ maxWidth: 608, width: '100%', alignSelf: 'center' }}>{children}</View>
    </View>
  );
}

export function Sheet({ visible, onClose, title, children }: { visible: boolean; onClose: () => void; title: string; children: ReactNode }) {
  const c = useColors();
  const insets = useSafeAreaInsets();
  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable accessibilityLabel={t.common.cancel} onPress={onClose} style={{ flex: 1, backgroundColor: c.scrim, opacity: 0.5 }} />
      <View accessibilityViewIsModal style={{ backgroundColor: c['surface-raised'], borderTopLeftRadius: radius.xl, borderTopRightRadius: radius.xl,
        padding: space(5), paddingBottom: space(5) + insets.bottom, gap: space(4) }}>
        <View style={{ maxWidth: 600, width: '100%', alignSelf: 'center', gap: space(4) }}>
          <Row style={{ justifyContent: 'space-between' }}>
            <Text variant="lg" accessibilityRole="header">{title}</Text>
            <Pressable accessibilityRole="button" accessibilityLabel={t.common.cancel} onPress={onClose} style={{ width: 44, height: 44, alignItems: 'center', justifyContent: 'center' }}>
              <X color={c['text-muted']} size={22} />
            </Pressable>
          </Row>
          {children}
        </View>
      </View>
    </Modal>
  );
}

