/**
 * Design tokens.
 *
 * Colour comes from `useTheme()`, never from a module-level constant, because
 * the palette depends on the system theme and on whether high contrast is
 * active. See palette.ts for why light and dark cannot be PlatformColors
 * under Fabric, and useTheme.ts for the selection order.
 *
 * Everything else here is theme-independent: the Fluent type ramp, the 4px
 * spacing grid, corner radii, and control metrics.
 */

export {useTheme, type Theme, type ThemeName} from './useTheme';
export {type Palette} from './palette';

/**
 * The Fluent type ramp.
 *
 * Windows 11 splits Segoe UI Variable by optical size: Display for large
 * text, Text for body, Small for captions. Using the right one is most of what
 * makes an app read as native rather than as a web page in a window.
 */
const FACE_DISPLAY = 'Segoe UI Variable Display';
const FACE_TEXT = 'Segoe UI Variable Text';
const FACE_SMALL = 'Segoe UI Variable Small';

export const type = {
  caption: {fontFamily: FACE_SMALL, fontSize: 12, lineHeight: 16},
  body: {fontFamily: FACE_TEXT, fontSize: 14, lineHeight: 20},
  bodyStrong: {
    fontFamily: FACE_TEXT,
    fontSize: 14,
    lineHeight: 20,
    fontWeight: '600',
  },
  bodyLarge: {fontFamily: FACE_TEXT, fontSize: 18, lineHeight: 24},
  subtitle: {
    fontFamily: FACE_DISPLAY,
    fontSize: 20,
    lineHeight: 28,
    fontWeight: '600',
  },
  title: {
    fontFamily: FACE_DISPLAY,
    fontSize: 28,
    lineHeight: 36,
    fontWeight: '600',
  },
} as const;

/** Fluent's 4px spacing grid. */
export const space = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 20,
  xxl: 24,
} as const;

/** Fluent corner radii: 4 for controls, 8 for surfaces and cards. */
export const radius = {
  control: 4,
  surface: 8,
} as const;

/**
 * Control metrics. The 32px minimum height is a Fluent standard and also a
 * pointer target floor; do not shrink it to fit more rows on screen.
 */
export const metrics = {
  controlHeight: 32,
  rowHeight: 32,
  sidebarWidth: 280,
  focusRingWidth: 2,
  borderWidth: 1,
} as const;
