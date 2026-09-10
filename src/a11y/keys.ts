/**
 * Keyboard chord matching for React Native Windows.
 *
 * RNW delivers key events with a physical `code` (KeyA, F6, ArrowDown) plus
 * modifier booleans. It also accepts a declarative `keyDownEvents` array naming
 * the chords a view intends to handle, which stops those keys bubbling to the
 * default focus machinery. The two have to agree: a chord we act on in
 * `onKeyDown` but omit from `keyDownEvents` will also be handled by RNW, and
 * Tab or arrow keys will move focus twice.
 *
 * Every module here therefore derives both from one chord list.
 */

import type {
  IHandledKeyboardEvent,
  IKeyboardEvent,
} from 'react-native';

/** RNW's EventPhase enum, inlined so callers need not import from deep paths. */
export const EventPhase = {
  None: 0,
  Capturing: 1,
  AtTarget: 2,
  Bubbling: 3,
} as const;

export type Chord = {
  /** Physical key code, e.g. 'F6', 'Tab', 'ArrowDown', 'KeyA', 'Escape'. */
  code: string;
  ctrl?: boolean;
  alt?: boolean;
  shift?: boolean;
  meta?: boolean;
};

/**
 * Parse a chord written in the notation we use throughout the app and in the
 * keymap documentation: 'Ctrl+Shift+F6', 'Alt', 'ArrowDown', 'Shift+F10'.
 *
 * Modifier names are case-insensitive; the key code is not, because it is a
 * physical code and 'keya' is not a valid one.
 */
export function chord(spec: string): Chord {
  const parts = spec.split('+').map(p => p.trim()).filter(Boolean);
  const key = parts.pop();

  if (!key) {
    throw new Error(`Invalid chord: "${spec}"`);
  }

  const result: Chord = {code: key};

  for (const part of parts) {
    switch (part.toLowerCase()) {
      case 'ctrl':
      case 'control':
        result.ctrl = true;
        break;
      case 'alt':
        result.alt = true;
        break;
      case 'shift':
        result.shift = true;
        break;
      case 'meta':
      case 'win':
        result.meta = true;
        break;
      default:
        throw new Error(`Unknown modifier "${part}" in chord "${spec}"`);
    }
  }

  return result;
}

/**
 * True when the event matches the chord exactly, including the absence of
 * modifiers the chord does not name. Exactness matters: without it, Ctrl+F6
 * would also trigger the plain F6 handler and pane cycling would fight tab
 * switching.
 */
export function matches(event: IKeyboardEvent, target: Chord): boolean {
  const {nativeEvent} = event;

  return (
    nativeEvent.code === target.code &&
    nativeEvent.ctrlKey === !!target.ctrl &&
    nativeEvent.altKey === !!target.alt &&
    nativeEvent.shiftKey === !!target.shift &&
    nativeEvent.metaKey === !!target.meta
  );
}

/**
 * Convert chords into the `keyDownEvents` array RNW expects, so the same list
 * that drives our handlers also suppresses the platform default.
 */
export function handledKeys(chords: readonly Chord[]): IHandledKeyboardEvent[] {
  return chords.map(c => ({
    code: c.code,
    ctrlKey: !!c.ctrl,
    altKey: !!c.alt,
    shiftKey: !!c.shift,
    metaKey: !!c.meta,
    handledEventPhase: EventPhase.Bubbling,
  }));
}

export type Binding<T extends string = string> = {
  id: T;
  chords: Chord[];
  /** Shown in the keyboard help screen and in menu item accelerators. */
  label: string;
};

/**
 * A small dispatch table. Screens build one, spread `bindings.handledKeys` onto
 * the view's `keyDownEvents`, and call `bindings.dispatch` from `onKeyDown`.
 */
export class KeyMap<T extends string> {
  private readonly bindings: Binding<T>[];

  constructor(bindings: Binding<T>[]) {
    this.bindings = bindings;
  }

  /** Every chord in the map, ready for the `keyDownEvents` prop. */
  get handledKeys(): IHandledKeyboardEvent[] {
    return handledKeys(this.bindings.flatMap(b => b.chords));
  }

  /** Returns the id of the matching binding, or null when nothing matches. */
  resolve(event: IKeyboardEvent): T | null {
    for (const binding of this.bindings) {
      for (const c of binding.chords) {
        if (matches(event, c)) {
          return binding.id;
        }
      }
    }
    return null;
  }

  /**
   * Resolve and invoke in one step. Returns true when a handler ran, so callers
   * can decide whether to let the event continue.
   */
  dispatch(event: IKeyboardEvent, handlers: Partial<Record<T, () => void>>): boolean {
    const id = this.resolve(event);
    if (id === null) {
      return false;
    }

    const handler = handlers[id];
    if (!handler) {
      return false;
    }

    handler();
    return true;
  }

  /** For the keyboard help screen. */
  describe(): ReadonlyArray<{id: T; label: string; chords: string[]}> {
    return this.bindings.map(b => ({
      id: b.id,
      label: b.label,
      chords: b.chords.map(formatChord),
    }));
  }
}

/** Render a chord the way Windows documentation writes it. */
export function formatChord(c: Chord): string {
  const parts: string[] = [];
  if (c.ctrl) parts.push('Ctrl');
  if (c.alt) parts.push('Alt');
  if (c.shift) parts.push('Shift');
  if (c.meta) parts.push('Win');
  parts.push(friendlyKeyName(c.code));
  return parts.join('+');
}

function friendlyKeyName(code: string): string {
  if (code.startsWith('Key')) return code.slice(3);
  if (code.startsWith('Digit')) return code.slice(5);

  switch (code) {
    case 'ArrowUp': return 'Up Arrow';
    case 'ArrowDown': return 'Down Arrow';
    case 'ArrowLeft': return 'Left Arrow';
    case 'ArrowRight': return 'Right Arrow';
    case 'Escape': return 'Esc';
    case 'ContextMenu': return 'Applications';
    case 'PageUp': return 'Page Up';
    case 'PageDown': return 'Page Down';
    default: return code;
  }
}
