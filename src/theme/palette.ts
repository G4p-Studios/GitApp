/**
 * Colour palettes.
 *
 * This is the one file in the app allowed to name a colour, and the reason is
 * worth stating because it looks like a violation of our own rule.
 *
 * Under RNW Fabric there is no `PlatformColor` name that returns dark-theme
 * values (upstream issue 11489):
 *
 *   - The WinUI Fluent brush names are aliased to a table of hardcoded
 *     *light* values.
 *   - The `UIElementType` names are the classic Win32 system colours. They
 *     track high contrast correctly, but not the Windows 11 light/dark
 *     setting, so they are permanently light outside high contrast.
 *
 * Using either one in dark mode produces a light window, which is what the
 * first attempt at this shipped. So for the ordinary light and dark themes we
 * supply Fluent's own values ourselves, and switch on `useColorScheme()`.
 *
 * High contrast is different and must not be handled this way. Under a high
 * contrast theme the user has chosen specific colours and the app is required
 * to obey them, so that palette is entirely `PlatformColor` system names,
 * which the system resolves to the active high contrast set.
 *
 * The long-term fix for light/dark is a native custom resource loader, which
 * is tier 1 of Fabric's lookup and would let us drop the literals here.
 */

import {PlatformColor, type ColorValue} from 'react-native';

export type Palette = {
  background: ColorValue;
  surface: ColorValue;
  surfaceHover: ColorValue;
  text: ColorValue;
  textSecondary: ColorValue;
  textOnSurface: ColorValue;
  selected: ColorValue;
  selectedText: ColorValue;
  link: ColorValue;
  accent: ColorValue;
  border: ColorValue;
  borderSubtle: ColorValue;
  disabled: ColorValue;
};

/**
 * The user's accent colour. `Accent` is a UIColorType, resolved through
 * `UISettings.GetColorValue`, so it is correct in every theme and is the one
 * system colour worth using outside high contrast.
 */
const accent = PlatformColor('Accent') as ColorValue;

/** Fluent light theme. Values from WinUI's Light resource dictionary. */
export const light: Palette = {
  background: '#F3F3F3', // SolidBackgroundFillColorBase
  surface: '#FBFBFB', // ControlFillColorDefault, flattened
  surfaceHover: '#F6F6F6', // ControlFillColorSecondary, flattened
  text: '#1B1B1B', // TextFillColorPrimary
  textSecondary: '#5D5D5D', // TextFillColorSecondary
  textOnSurface: '#1B1B1B',
  selected: accent,
  selectedText: '#FFFFFF', // TextOnAccentFillColorPrimary
  link: accent,
  accent,
  border: '#E5E5E5', // ControlStrokeColorDefault, flattened
  borderSubtle: '#EDEDED',
  disabled: '#9D9D9D', // TextFillColorDisabled
};

/** Fluent dark theme. Values from WinUI's Dark resource dictionary. */
export const dark: Palette = {
  background: '#202020', // SolidBackgroundFillColorBase
  surface: '#2D2D2D', // ControlFillColorDefault, flattened
  surfaceHover: '#323232', // ControlFillColorSecondary, flattened
  text: '#FFFFFF', // TextFillColorPrimary
  textSecondary: '#C5C5C5', // TextFillColorSecondary
  textOnSurface: '#FFFFFF',
  selected: accent,
  selectedText: '#000000', // TextOnAccentFillColorPrimary, dark theme
  link: accent,
  accent,
  border: '#3A3A3A', // ControlStrokeColorDefault, flattened
  borderSubtle: '#303030',
  disabled: '#787878', // TextFillColorDisabled
};

/**
 * High contrast. Every entry is a system colour, because under a high
 * contrast theme the user's chosen palette is the only correct answer.
 *
 * `Highlight` and `HighlightText` are a pair and must always be applied
 * together; a high contrast theme redefines both, and using one with a colour
 * of our own produces unreadable selected rows.
 */
export const highContrast: Palette = {
  background: PlatformColor('Window') as ColorValue,
  surface: PlatformColor('ButtonFace') as ColorValue,
  surfaceHover: PlatformColor('ButtonFace') as ColorValue,
  text: PlatformColor('WindowText') as ColorValue,
  textSecondary: PlatformColor('GrayText') as ColorValue,
  textOnSurface: PlatformColor('ButtonText') as ColorValue,
  selected: PlatformColor('Highlight') as ColorValue,
  selectedText: PlatformColor('HighlightText') as ColorValue,
  link: PlatformColor('Hotlight') as ColorValue,
  accent: PlatformColor('Highlight') as ColorValue,
  border: PlatformColor('WindowText') as ColorValue,
  borderSubtle: PlatformColor('NonTextMedium') as ColorValue,
  disabled: PlatformColor('GrayText') as ColorValue,
};
