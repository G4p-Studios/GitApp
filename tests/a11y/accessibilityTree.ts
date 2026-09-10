/**
 * Accessibility tree extraction for tests.
 *
 * Scope, stated plainly: this walks the rendered React tree and reports the
 * accessibility props we *declare*. It is not a UIA tree dump — that requires
 * the app running on Windows with a real automation client, and belongs in the
 * E2E suite.
 *
 * What it does catch is the whole class of regression that actually happens in
 * practice: a refactor that drops accessibilityLabel from a row, a list whose
 * set size silently switches to the rendered window length, a control that
 * loses its role. Those are the bugs that ship, because nothing else in the
 * toolchain complains about them.
 *
 * See docs/ARCHITECTURE.md section 3.7.
 */

import type {ReactTestInstance, ReactTestRendererJSON} from 'react-test-renderer';

export type A11yNode = {
  role?: string;
  label?: string;
  description?: string;
  hint?: string;
  /** UIA PositionInSet. */
  posInSet?: number;
  /** UIA SizeOfSet. */
  setSize?: number;
  /** UIA Level, for tree depth and heading level. */
  level?: number;
  liveRegion?: string;
  accessKey?: string;
  state?: Record<string, unknown>;
  value?: Record<string, unknown>;
  focusable?: boolean;
  tabIndex?: number;
  children: A11yNode[];
};

const A11Y_PROPS = [
  'accessibilityRole',
  'role',
  'accessibilityLabel',
  'accessibilityDescription',
  'accessibilityHint',
  'accessibilityPosInSet',
  'accessibilitySetSize',
  'accessibilityLevel',
  'accessibilityLiveRegion',
  'accessibilityAccessKey',
  'accessibilityState',
  'accessibilityValue',
  'accessible',
] as const;

function isAccessible(props: Record<string, unknown>): boolean {
  return A11Y_PROPS.some(p => props[p] !== undefined) || props.focusable === true;
}

/** Strip undefined-valued keys; return undefined when nothing is left. */
function compact(input: unknown): Record<string, unknown> | undefined {
  if (!input || typeof input !== 'object') {
    return undefined;
  }

  const entries = Object.entries(input as Record<string, unknown>).filter(
    ([, v]) => v !== undefined,
  );

  return entries.length > 0 ? Object.fromEntries(entries) : undefined;
}

function toNode(props: Record<string, unknown>, children: A11yNode[]): A11yNode {
  const node: A11yNode = {children};

  const role = props.accessibilityRole ?? props.role;
  if (role !== undefined) node.role = String(role);
  if (props.accessibilityLabel !== undefined) node.label = String(props.accessibilityLabel);
  if (props.accessibilityDescription !== undefined)
    node.description = String(props.accessibilityDescription);
  if (props.accessibilityHint !== undefined) node.hint = String(props.accessibilityHint);
  if (props.accessibilityPosInSet !== undefined)
    node.posInSet = Number(props.accessibilityPosInSet);
  if (props.accessibilitySetSize !== undefined)
    node.setSize = Number(props.accessibilitySetSize);
  if (props.accessibilityLevel !== undefined) node.level = Number(props.accessibilityLevel);
  if (props.accessibilityLiveRegion !== undefined)
    node.liveRegion = String(props.accessibilityLiveRegion);
  if (props.accessibilityAccessKey !== undefined)
    node.accessKey = String(props.accessibilityAccessKey);
  // Pressable fills these objects with undefined keys. Dropping them keeps the
  // snapshot readable, which matters: a snapshot nobody can read is a snapshot
  // nobody reviews, and the diff is the whole point.
  const state = compact(props.accessibilityState);
  if (state) node.state = state;

  const value = compact(props.accessibilityValue);
  if (value) node.value = value;
  if (props.focusable !== undefined) node.focusable = Boolean(props.focusable);
  if (props.tabIndex !== undefined) node.tabIndex = Number(props.tabIndex);

  return node;
}

/**
 * Reduce a react-test-renderer JSON tree to only its accessible nodes,
 * preserving nesting. Presentational wrappers collapse away, so the snapshot
 * reads like what a screen reader traverses rather than like the DOM.
 */
export function accessibilityTree(
  json: ReactTestRendererJSON | ReactTestRendererJSON[] | null,
): A11yNode[] {
  if (!json) {
    return [];
  }

  const roots = Array.isArray(json) ? json : [json];

  return roots.flatMap(node => {
    const props = (node.props ?? {}) as Record<string, unknown>;
    const childJson = (node.children ?? []).filter(
      (c): c is ReactTestRendererJSON => typeof c === 'object' && c !== null,
    );
    const children = accessibilityTree(childJson);

    return isAccessible(props) ? [toNode(props, children)] : children;
  });
}

/** Flatten to a list, for assertions that do not care about nesting. */
export function flatten(nodes: A11yNode[]): A11yNode[] {
  return nodes.flatMap(n => [n, ...flatten(n.children)]);
}

/** Every node carrying a given role, at any depth. */
export function byRole(nodes: A11yNode[], role: string): A11yNode[] {
  return flatten(nodes).filter(n => n.role === role);
}

/**
 * Assert the invariant that trips people up most: exactly one element in a
 * composite widget is in the Tab order. More than one and Tab stops working
 * as a way to leave the widget; none and the widget is unreachable.
 */
export function expectSingleTabStop(nodes: A11yNode[]): void {
  const stops = flatten(nodes).filter(n => n.tabIndex === 0);
  if (stops.length !== 1) {
    throw new Error(
      `Expected exactly one tab stop, found ${stops.length}: ` +
        JSON.stringify(stops.map(s => s.label ?? s.role)),
    );
  }
}

/** Find focusable nodes that a screen reader would announce as unnamed. */
export function unnamedInteractive(nodes: A11yNode[]): A11yNode[] {
  return flatten(nodes).filter(
    n => n.focusable === true && !n.label && n.role !== 'pane',
  );
}

/** Convenience for tests that only need the instance tree. */
export function fromInstance(instance: ReactTestInstance): A11yNode[] {
  const props = instance.props as Record<string, unknown>;
  const children = instance.children
    .filter((c): c is ReactTestInstance => typeof c !== 'string')
    .flatMap(fromInstance);

  return isAccessible(props) ? [toNode(props, children)] : children;
}
