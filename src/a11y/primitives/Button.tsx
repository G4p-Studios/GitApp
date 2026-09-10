/**
 * The button primitive.
 *
 * Fabric gives a View no semantics at all, so every one of these props is
 * load-bearing. Screens must never hand-roll a Pressable with an
 * accessibilityRole; that is how inconsistencies creep in.
 *
 * See docs/ARCHITECTURE.md section 3.1.
 */

import React, {forwardRef, useCallback, useMemo, useState} from 'react';
import {
  Pressable,
  StyleSheet,
  Text,
  type StyleProp,
  type ViewStyle,
} from 'react-native';

import {chord, matches} from '../keys';
import type {Palette} from '../../theme';
import {metrics, radius, space, type as typeScale, useTheme} from '../../theme';

const SPACE = chord('Space');
const ENTER = chord('Enter');

export type ButtonProps = {
  /**
   * The control's identity. Never include the word "button": the UIA control
   * type already says so, and screen readers announce both.
   */
  label: string;
  /** Supplementary context, mapped to UIA FullDescription. */
  description?: string;
  /** What invoking it does, only when that is not obvious from the label. */
  hint?: string;
  onPress: () => void;
  disabled?: boolean;
  /**
   * In-progress state. This is the only route to UIA ItemStatus on Fabric,
   * which is hardcoded to the string "Busy"; arbitrary status text is not
   * available. See docs/ARCHITECTURE.md section 3.3.
   */
  busy?: boolean;
  /** Mnemonic character, surfaced as UIA AccessKey. */
  accessKey?: string;
  style?: StyleProp<ViewStyle>;
  children?: React.ReactNode;
};

export const Button = forwardRef<React.ComponentRef<typeof Pressable>, ButtonProps>(
  function Button(
    {label, description, hint, onPress, disabled, busy, accessKey, style, children},
    ref,
  ) {
    const {color} = useTheme();
    const [focused, setFocused] = useState(false);

    const styles = useMemo(() => makeStyles(color), [color]);

    // Fabric fires onClick for pointer input, but keyboard activation of a
    // generic View is not automatic. Handle both keys explicitly.
    const onKeyDown = useCallback(
      (event: Parameters<typeof matches>[0]) => {
        if (disabled) {
          return;
        }
        if (matches(event, SPACE) || matches(event, ENTER)) {
          onPress();
        }
      },
      [disabled, onPress],
    );

    return (
      <Pressable
        ref={ref}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        style={[
          styles.button,
          disabled && styles.disabled,
          // A visible focus indicator is not decoration. RNW 0.84 shows system
          // focus visuals for keyboard input only, but the default ring is
          // easy to lose against a themed surface, so draw our own in the
          // user's accent colour.
          focused && styles.focused,
          style,
        ]}
        onPress={disabled ? undefined : onPress}
        onKeyDown={onKeyDown}
        keyDownEvents={[
          {code: 'Space', handledEventPhase: 3},
          {code: 'Enter', handledEventPhase: 3},
        ]}
        focusable={!disabled}
        tabIndex={disabled ? -1 : 0}
        accessible
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityDescription={description}
        accessibilityHint={hint}
        accessibilityAccessKey={accessKey}
        accessibilityState={{disabled: !!disabled, busy: !!busy}}>
        {children ?? (
          <Text style={[styles.label, disabled && styles.labelDisabled]}>
            {label}
          </Text>
        )}
      </Pressable>
    );
  },
);

const makeStyles = (color: Palette) =>
  StyleSheet.create({
  button: {
    minHeight: metrics.controlHeight,
    paddingHorizontal: space.md,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: color.surface,
    borderWidth: metrics.borderWidth,
    borderColor: color.border,
    borderRadius: radius.control,
  },
  focused: {
    borderColor: color.accent,
    borderWidth: metrics.focusRingWidth,
    // Keep the control the same size when the ring thickens, so a row of
    // buttons does not shift as focus moves through it.
    paddingHorizontal: space.md - (metrics.focusRingWidth - metrics.borderWidth),
  },
  disabled: {
    borderColor: color.borderSubtle,
  },
  label: {...typeScale.body, color: color.textOnSurface},
  labelDisabled: {color: color.disabled},
  });
