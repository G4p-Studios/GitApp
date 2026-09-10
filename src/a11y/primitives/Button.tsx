/**
 * The button primitive.
 *
 * Fabric gives a View no semantics at all, so every one of these props is
 * load-bearing. Screens must never hand-roll a Pressable with an
 * accessibilityRole; that is how inconsistencies creep in.
 *
 * See docs/ARCHITECTURE.md section 3.1.
 */

import React, {forwardRef, useCallback} from 'react';
import {
  Pressable,
  Text,
  type StyleProp,
  type ViewStyle,
} from 'react-native';

import {chord, matches} from '../keys';

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
        style={style}
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
        {children ?? <Text>{label}</Text>}
      </Pressable>
    );
  },
);
