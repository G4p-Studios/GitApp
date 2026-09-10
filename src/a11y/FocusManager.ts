/**
 * Focus ownership.
 *
 * Losing focus to the window root is the single most common way a screen
 * reader app becomes unusable, and RNW will do it silently whenever the
 * focused view unmounts. Nothing in the framework restores it for us.
 *
 * Two responsibilities live here:
 *
 *   1. A restore stack for transient surfaces. Anything that steals focus
 *      (menu, dialog, flyout) pushes where focus came from, and gives it back
 *      on close.
 *   2. The pane registry behind F6 cycling, which is our substitute for the
 *      landmark navigation Fabric does not expose.
 *
 * See docs/ARCHITECTURE.md sections 3.4 and 2.3.
 */

import type {HostInstance} from 'react-native';

/** Anything we can imperatively focus. RNW exposes focus() on host views. */
export type Focusable = Pick<HostInstance, 'focus'> | null | undefined;

export type PaneId = string;

type PaneRecord = {
  id: PaneId;
  /** Spoken on entry, e.g. "Repositories". */
  name: string;
  /** Cycle position. Panes are visited in ascending order. */
  order: number;
  /** Focus target used when the pane has not been visited yet. */
  entry: Focusable;
  /** Where focus was when the user last left this pane. */
  lastFocused: Focusable;
};

type PaneChangeListener = (pane: PaneRecord) => void;

class FocusManagerImpl {
  private readonly restoreStack: Focusable[] = [];
  private readonly panes = new Map<PaneId, PaneRecord>();
  private readonly paneListeners = new Set<PaneChangeListener>();
  private activePane: PaneId | null = null;

  // ---------------------------------------------------------------------
  // Restore stack
  // ---------------------------------------------------------------------

  /**
   * Record where focus should return to, before opening something that takes
   * it. Call this *before* the transient surface mounts, while the outgoing
   * element still exists.
   */
  pushRestoreTarget(target: Focusable): void {
    this.restoreStack.push(target ?? null);
  }

  /**
   * Return focus to the most recently pushed target. Safe to call when the
   * target has since unmounted: focus falls back to the active pane rather
   * than to the window root.
   */
  popAndRestore(): void {
    const target = this.restoreStack.pop();

    if (tryFocus(target)) {
      return;
    }

    // The opener is gone (its row was filtered away, the repo was closed).
    // Land somewhere meaningful instead of nowhere.
    this.focusActivePane();
  }

  /** Discard a pushed target without restoring, e.g. when navigating away. */
  dropRestoreTarget(): void {
    this.restoreStack.pop();
  }

  get restoreDepth(): number {
    return this.restoreStack.length;
  }

  // ---------------------------------------------------------------------
  // Panes
  // ---------------------------------------------------------------------

  registerPane(pane: Omit<PaneRecord, 'lastFocused'>): () => void {
    this.panes.set(pane.id, {...pane, lastFocused: null});

    if (this.activePane === null) {
      this.activePane = pane.id;
    }

    return () => {
      this.panes.delete(pane.id);
      if (this.activePane === pane.id) {
        this.activePane = this.orderedPanes()[0]?.id ?? null;
      }
    };
  }

  /** Update a pane's entry target once its first focusable child mounts. */
  setPaneEntry(id: PaneId, entry: Focusable): void {
    const pane = this.panes.get(id);
    if (pane) {
      pane.entry = entry;
    }
  }

  /**
   * Remember where focus sits inside a pane, so returning by F6 lands where
   * the user left off rather than at the top. This is what makes pane cycling
   * feel like a Windows app instead of a reset button.
   */
  noteFocusWithin(id: PaneId, target: Focusable): void {
    const pane = this.panes.get(id);
    if (pane) {
      pane.lastFocused = target;
    }
    this.activePane = id;
  }

  getActivePane(): PaneRecord | null {
    return this.activePane ? this.panes.get(this.activePane) ?? null : null;
  }

  /**
   * Move to the next or previous pane and focus it. Returns the pane entered,
   * or null when there is nowhere to go.
   *
   * The caller is responsible for announcing; PaneHost does it, so that a
   * screen driving focus programmatically can choose to stay silent.
   */
  cyclePane(direction: 1 | -1): PaneRecord | null {
    const ordered = this.orderedPanes();
    if (ordered.length < 2) {
      return null;
    }

    const currentIndex = ordered.findIndex(p => p.id === this.activePane);
    const from = currentIndex === -1 ? 0 : currentIndex;
    const nextIndex = (from + direction + ordered.length) % ordered.length;
    const next = ordered[nextIndex];

    this.enterPane(next);
    return next;
  }

  /** Jump directly to a pane by id, e.g. from a menu command. */
  focusPane(id: PaneId): PaneRecord | null {
    const pane = this.panes.get(id);
    if (!pane) {
      return null;
    }
    this.enterPane(pane);
    return pane;
  }

  subscribeToPaneChange(listener: PaneChangeListener): () => void {
    this.paneListeners.add(listener);
    return () => {
      this.paneListeners.delete(listener);
    };
  }

  /** Test seam. */
  reset(): void {
    this.restoreStack.length = 0;
    this.panes.clear();
    this.paneListeners.clear();
    this.activePane = null;
  }

  private enterPane(pane: PaneRecord): void {
    this.activePane = pane.id;

    // Prefer where the user last was; fall back to the pane's entry point.
    if (!tryFocus(pane.lastFocused)) {
      tryFocus(pane.entry);
    }

    for (const listener of this.paneListeners) {
      listener(pane);
    }
  }

  private focusActivePane(): void {
    const pane = this.getActivePane();
    if (pane && !tryFocus(pane.lastFocused)) {
      tryFocus(pane.entry);
    }
  }

  private orderedPanes(): PaneRecord[] {
    return [...this.panes.values()].sort((a, b) => a.order - b.order);
  }
}

function tryFocus(target: Focusable): boolean {
  if (!target || typeof target.focus !== 'function') {
    return false;
  }

  try {
    target.focus();
    return true;
  } catch {
    // The view unmounted between capture and restore. Not exceptional.
    return false;
  }
}

export const FocusManager = new FocusManagerImpl();
