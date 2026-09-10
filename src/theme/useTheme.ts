/**
 * Theme selection.
 *
 * Three themes, in priority order:
 *
 *   1. High contrast, if the user has one active. This wins over everything,
 *      including an explicit light/dark preference, because the user has
 *      chosen those colours for a reason and an app that overrides them is
 *      broken for the person who needs it most.
 *   2. Dark, if the system is in dark mode.
 *   3. Light.
 *
 * See src/theme/palette.ts for why light and dark cannot come from
 * PlatformColor under Fabric.
 */

import {useEffect, useState} from 'react';
import {useColorScheme} from 'react-native';

import {dark, highContrast, light, type Palette} from './palette';

export type ThemeName = 'light' | 'dark' | 'highContrast';

export type Theme = {
  name: ThemeName;
  color: Palette;
  isHighContrast: boolean;
};

/**
 * RNW exposes high contrast through its AppTheme module. The module is marked
 * deprecated upstream and may not be registered in every configuration, so
 * every access is guarded: a missing module must degrade to "no high
 * contrast", never crash the app on startup.
 */
function readHighContrast(): boolean {
  try {
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const {AppTheme} = require('react-native-windows');
    return AppTheme?.isHighContrast === true;
  } catch {
    return false;
  }
}

function subscribeToHighContrast(onChange: (value: boolean) => void): () => void {
  try {
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const {AppTheme} = require('react-native-windows');
    if (!AppTheme?.addListener) {
      return () => {};
    }

    const subscription = AppTheme.addListener('highContrastChanged', () => {
      onChange(AppTheme.isHighContrast === true);
    });

    return () => subscription?.remove?.();
  } catch {
    return () => {};
  }
}

export function useTheme(): Theme {
  const scheme = useColorScheme();
  const [isHighContrast, setHighContrast] = useState(readHighContrast);

  useEffect(() => subscribeToHighContrast(setHighContrast), []);

  if (isHighContrast) {
    return {name: 'highContrast', color: highContrast, isHighContrast: true};
  }

  return scheme === 'dark'
    ? {name: 'dark', color: dark, isHighContrast: false}
    : {name: 'light', color: light, isHighContrast: false};
}
