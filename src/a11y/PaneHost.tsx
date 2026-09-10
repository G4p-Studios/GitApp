/**
 * Pane cycling.
 *
 * Fabric exposes no UIA landmarks, so a screen reader user has no built-in way
 * to jump between regions of a window. F6 is the Windows convention for that
 * and this is where we implement it. Every screen in GitApp is composed of
 * panes; a screen without them is a bug.
 *
 * See docs/ARCHITECTURE.md sections 2.3 and 3.4.
 */

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
} from 'react';
import {
  Text,
  View,
  type AccessibilityRole,
  type StyleProp,
  type ViewStyle,
} from 'react-native';

import {Announcer} from './Announcer';
import {FocusManager, type PaneId} from './FocusManager';
import {KeyMap, chord} from './keys';

const paneKeys = new KeyMap<'nextPane' | 'prevPane'>([
  {id: 'prevPane', chords: [chord('Shift+F6')], label: 'Previous pane'},
  {id: 'nextPane', chords: [chord('F6')], label: 'Next pane'},
]);

type PaneContextValue = {
  paneId: PaneId;
  /**
   * Children call this from onFocus so the pane remembers where the user was.
   * Without it, F6 back into a pane resets to the top and the user loses
   * their place in a thousand-row list.
   */
  noteFocus: (target: {focus: () => void} | null) => void;
};

const PaneContext = createContext<PaneContextValue | null>(null);

/**
 * RNW's native `GetControlTypeFromString` maps accessibilityRole "pane" to
 * UIA_PaneControlTypeId, but the shipped TypeScript union omits it. The cast
 * is deliberate and verified against the provider source; drop it if the type
 * surface catches up.
 */
const PANE_ROLE = 'pane' as AccessibilityRole;

export function usePane(): PaneContextValue | null {
  return useContext(PaneContext);
}

/**
 * Wraps the whole window. Owns the F6 chords and announces pane entry.
 *
 * Shift+F6 is listed before F6 in the keymap because resolution is
 * first-match and the matcher is modifier-exact; the ordering is belt and
 * braces against a future non-exact matcher.
 */
export function PaneHost({children}: {children: React.ReactNode}) {
  useEffect(
    () =>
      FocusManager.subscribeToPaneChange(pane => {
        // Announce the destination, not the departure. Screen reader users
        // navigating quickly need to know where they landed.
        Announcer.announce(`${pane.name} pane`);
      }),
    [],
  );

  const onKeyDown = useCallback((event: Parameters<typeof paneKeys.resolve>[0]) => {
    paneKeys.dispatch(event, {
      nextPane: () => FocusManager.cyclePane(1),
      prevPane: () => FocusManager.cyclePane(-1),
    });
  }, []);

  return (
    <View
      style={{flex: 1}}
      onKeyDown={onKeyDown}
      keyDownEvents={paneKeys.handledKeys}>
      {children}
    </View>
  );
}

export type PaneProps = {
  id: PaneId;
  /** Spoken on entry. A noun, not a sentence: "Repositories", "Changes". */
  name: string;
  /** Cycle position. Leave gaps (10, 20, 30) so panes can be inserted later. */
  order: number;
  style?: StyleProp<ViewStyle>;
  children: React.ReactNode;
};

/**
 * One region of a screen. Renders as a UIA Pane with a spoken name, which is
 * the closest Fabric gets to a landmark.
 */
export function Pane({id, name, order, style, children}: PaneProps) {
  const ref = useRef<React.ComponentRef<typeof View>>(null);

  useEffect(
    () => FocusManager.registerPane({id, name, order, entry: ref.current}),
    [id, name, order],
  );

  // Keep the entry target fresh across re-renders; the first render registers
  // with a null ref because refs attach after the effect's dependencies are
  // captured.
  useEffect(() => {
    FocusManager.setPaneEntry(id, ref.current);
  });

  const value = useMemo<PaneContextValue>(
    () => ({
      paneId: id,
      noteFocus: target => FocusManager.noteFocusWithin(id, target),
    }),
    [id],
  );

  return (
    <PaneContext.Provider value={value}>
      <View
        ref={ref}
        style={style}
        accessible={false}
        accessibilityRole={PANE_ROLE}
        accessibilityLabel={name}
        // Focusable so the pane itself can receive focus when it has no
        // focusable children yet (an empty list, a loading state). Excluded
        // from the Tab order, because F6 is how you reach a pane.
        focusable
        tabIndex={-1}
        onFocus={() => FocusManager.noteFocusWithin(id, ref.current)}>
        {children}
      </View>
    </PaneContext.Provider>
  );
}

/**
 * The application status line. A single polite live region, mounted once, that
 * `Announcer.setStatus()` writes into.
 *
 * One live region for the whole app is deliberate: multiple simultaneous
 * regions race each other in the speech queue and the user hears fragments.
 */
export function StatusLine({style}: {style?: StyleProp<ViewStyle>}) {
  const [status, setStatus] = React.useState(() => Announcer.getStatus());

  useEffect(
    () => Announcer.subscribeToStatus((text, urgency) => setStatus({text, urgency})),
    [],
  );

  return (
    <View
      style={style}
      // `role="status"` rather than accessibilityRole: the two go through
      // different native paths, and only the role enum maps to
      // UIA_StatusBarControlTypeId. accessibilityRole has no "statusbar" case
      // and would silently fall back to a Group.
      role="status"
      accessibilityLabel="Status"
      accessibilityLiveRegion={status.text ? status.urgency : 'none'}>
      <Text>{status.text}</Text>
    </View>
  );
}
