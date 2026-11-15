import { ScrollViewStyleReset } from 'expo-router/html';
import type { PropsWithChildren } from 'react';
import { tokens } from '../theme/tokens.generated';

// Web only: the document shell. Right-to-left Arabic, theme background before JS loads.
export default function Root({ children }: PropsWithChildren) {
  return (
    <html lang="ar" dir="rtl">
      <head>
        <meta charSet="utf-8" />
        <meta httpEquiv="X-UA-Compatible" content="IE=edge" />
        <meta name="viewport" content="width=device-width, initial-scale=1, shrink-to-fit=no" />
        <ScrollViewStyleReset />
        <style dangerouslySetInnerHTML={{ __html: `body{background:${tokens.light.bg}}@media (prefers-color-scheme: dark){body{background:${tokens.dark.bg}}}` }} />
      </head>
      <body>{children}</body>
    </html>
  );
}
