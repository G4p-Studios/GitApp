/**
 * The list primitive.
 *
 * This carries more accessibility weight than anything else in the app,
 * because Fabric gives us no list behaviour whatsoever:
 *
 *   - No ItemContainer or VirtualizedItem pattern, so set counts must come
 *     from the real dataset rather than the rendered window, or the user is
 *     told "item 3 of 20" while scrolling through 4000 commits.
 *   - No roving focus, so a naive list puts every row in the Tab order and
 *     Tab becomes useless.
 *   - No type-to-select, which is the main way keyboard users move through a
 *     long list in a Windows app.
 *
 * See docs/ARCHITECTURE.md sections 2.3 and 3.2.
 */

import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import {
  FlatList,
  Pressable,
  Text,
  View,
  type StyleProp,
  type ViewStyle,
} from 'react-native';

import {usePane} from '../PaneHost';
import {KeyMap, chord} from '../keys';

/** Type-to-select buffer resets after this long without a keystroke. */
const TYPEAHEAD_RESET_MS = 1000;

const listKeys = new KeyMap<
  'first' | 'last' | 'next' | 'prev' | 'pageDown' | 'pageUp' | 'activate' | 'contextMenu'
>([
  {id: 'next', chords: [chord('ArrowDown')], label: 'Next item'},
  {id: 'prev', chords: [chord('ArrowUp')], label: 'Previous item'},
  {id: 'first', chords: [chord('Home')], label: 'First item'},
  {id: 'last', chords: [chord('End')], label: 'Last item'},
  {id: 'pageDown', chords: [chord('PageDown')], label: 'Down one page'},
  {id: 'pageUp', chords: [chord('PageUp')], label: 'Up one page'},
  {id: 'activate', chords: [chord('Enter')], label: 'Open item'},
  {
    id: 'contextMenu',
    chords: [chord('ContextMenu'), chord('Shift+F10')],
    label: 'Item actions',
  },
]);

const PAGE_SIZE = 10;

export type ListProps<T> = {
  /** Spoken name of the list itself, e.g. "Repositories". */
  label: string;
  items: readonly T[];
  keyExtractor: (item: T, index: number) => string;
  /**
   * The row's whole content as one string, in reading order. Fabric has no
   * grid patterns, so a row is announced as a single label; this is where the
   * columns get flattened. See docs/ARCHITECTURE.md section 2.3.
   */
  labelExtractor: (item: T, index: number) => string;
  /** Supplementary per-row context, mapped to UIA FullDescription. */
  descriptionExtractor?: (item: T, index: number) => string | undefined;
  renderItem: (item: T, index: number, selected: boolean) => React.ReactNode;
  onActivate?: (item: T, index: number) => void;
  onContextMenu?: (item: T, index: number) => void;
  /**
   * Total size of the underlying dataset when `items` is a loaded page of a
   * larger whole. Set counts are reported against this, so the user hears the
   * truth rather than the size of the current window.
   */
  totalCount?: number;
  /** Index offset of `items` within the full dataset, for the same reason. */
  offset?: number;
  emptyMessage?: string;
  style?: StyleProp<ViewStyle>;
};

export function List<T>({
  label,
  items,
  keyExtractor,
  labelExtractor,
  descriptionExtractor,
  renderItem,
  onActivate,
  onContextMenu,
  totalCount,
  offset = 0,
  emptyMessage = 'No items',
  style,
}: ListProps<T>) {
  const [activeIndex, setActiveIndex] = useState(0);
  const rowRefs = useRef(new Map<number, {focus: () => void}>());
  const listRef = useRef<FlatList<T>>(null);
  const typeahead = useRef({buffer: '', at: 0});
  const pane = usePane();

  const setSize = totalCount ?? items.length;

  // Keep the active index inside the data when the list shrinks under us
  // (a filter narrowed it, a repo was removed). Without this the roving
  // tabindex points at nothing and Tab into the list lands on the window.
  useEffect(() => {
    if (activeIndex > items.length - 1) {
      setActiveIndex(Math.max(0, items.length - 1));
    }
  }, [items.length, activeIndex]);

  const moveTo = useCallback(
    (index: number) => {
      const clamped = Math.max(0, Math.min(index, items.length - 1));
      setActiveIndex(clamped);
      listRef.current?.scrollToIndex({index: clamped, viewPosition: 0.5});
      rowRefs.current.get(clamped)?.focus();
    },
    [items.length],
  );

  const typeToSelect = useCallback(
    (character: string) => {
      const now = Date.now();
      const state = typeahead.current;
      state.buffer = now - state.at > TYPEAHEAD_RESET_MS ? character : state.buffer + character;
      state.at = now;

      const needle = state.buffer.toLowerCase();

      // Search forward from the item after the current one, wrapping, so
      // repeated presses of the same letter cycle through matches the way
      // Windows list views do.
      for (let step = 1; step <= items.length; step++) {
        const index = (activeIndex + step) % items.length;
        if (labelExtractor(items[index], index).toLowerCase().startsWith(needle)) {
          moveTo(index);
          return;
        }
      }
    },
    [activeIndex, items, labelExtractor, moveTo],
  );

  const onKeyDown = useCallback(
    (event: Parameters<typeof listKeys.resolve>[0]) => {
      const handled = listKeys.dispatch(event, {
        next: () => moveTo(activeIndex + 1),
        prev: () => moveTo(activeIndex - 1),
        first: () => moveTo(0),
        last: () => moveTo(items.length - 1),
        pageDown: () => moveTo(activeIndex + PAGE_SIZE),
        pageUp: () => moveTo(activeIndex - PAGE_SIZE),
        activate: () => onActivate?.(items[activeIndex], activeIndex),
        contextMenu: () => onContextMenu?.(items[activeIndex], activeIndex),
      });

      if (handled) {
        return;
      }

      // Printable single characters drive type-to-select. Modifier chords are
      // left alone so they can reach the menu bar and global shortcuts.
      const {key, ctrlKey, altKey, metaKey} = event.nativeEvent;
      if (key.length === 1 && !ctrlKey && !altKey && !metaKey) {
        typeToSelect(key);
      }
    },
    [activeIndex, items, moveTo, onActivate, onContextMenu, typeToSelect],
  );

  const handledKeys = useMemo(
    () => listKeys.handledKeys,
    [],
  );

  if (items.length === 0) {
    return (
      <View
        style={style}
        accessible
        accessibilityRole="list"
        accessibilityLabel={`${label}, empty`}>
        <Text>{emptyMessage}</Text>
      </View>
    );
  }

  return (
    <FlatList
      ref={listRef}
      style={style}
      data={items as T[]}
      keyExtractor={keyExtractor}
      accessibilityRole="list"
      accessibilityLabel={label}
      // Generous window: with no VirtualizedItem pattern, rows outside the
      // rendered range are invisible to a screen reader, so we trade memory
      // for reachability.
      initialNumToRender={40}
      windowSize={21}
      onScrollToIndexFailed={info => {
        // Row not yet realized. Nudge the list, then retry once it is.
        listRef.current?.scrollToOffset({
          offset: info.averageItemLength * info.index,
          animated: false,
        });
      }}
      renderItem={({item, index}) => {
        const isActive = index === activeIndex;
        return (
          <Pressable
            ref={node => {
              if (node) {
                rowRefs.current.set(index, node);
              } else {
                rowRefs.current.delete(index);
              }
            }}
            onPress={() => {
              setActiveIndex(index);
              onActivate?.(item, index);
            }}
            onFocus={() => {
              setActiveIndex(index);
              pane?.noteFocus(rowRefs.current.get(index) ?? null);
            }}
            onKeyDown={onKeyDown}
            keyDownEvents={handledKeys}
            // Roving tabindex: exactly one row is in the Tab order, so the
            // list is a single tab stop and arrows move within it.
            focusable
            tabIndex={isActive ? 0 : -1}
            accessible
            accessibilityRole="listitem"
            accessibilityLabel={labelExtractor(item, index)}
            accessibilityDescription={descriptionExtractor?.(item, index)}
            accessibilityPosInSet={offset + index + 1}
            accessibilitySetSize={setSize}
            accessibilityState={{selected: isActive}}>
            {renderItem(item, index, isActive)}
          </Pressable>
        );
      }}
    />
  );
}
