/**
 * The list contract. These assertions encode the three things Fabric will not
 * do for us, and that a refactor can silently undo.
 */

import React from 'react';
import {Text} from 'react-native';
import TestRenderer from 'react-test-renderer';

import {List} from '../../src/a11y';
import {
  accessibilityTree,
  byRole,
  expectSingleTabStop,
  unnamedInteractive,
} from './accessibilityTree';

type Row = {id: string; name: string};

const rows: Row[] = [
  {id: 'a', name: 'alpha'},
  {id: 'b', name: 'bravo'},
  {id: 'c', name: 'charlie'},
];

function renderList(props: Partial<React.ComponentProps<typeof List<Row>>> = {}) {
  let tree!: TestRenderer.ReactTestRenderer;

  TestRenderer.act(() => {
    tree = TestRenderer.create(
      <List
        label="Repositories"
        items={rows}
        keyExtractor={r => r.id}
        labelExtractor={r => r.name}
        renderItem={r => <Text>{r.name}</Text>}
        {...props}
      />,
    );
  });

  return accessibilityTree(tree.toJSON());
}

describe('List accessibility contract', () => {
  it('exposes the list and its items with the right roles', () => {
    const nodes = renderList();

    expect(byRole(nodes, 'list')).toHaveLength(1);
    expect(byRole(nodes, 'listitem')).toHaveLength(3);
  });

  it('names every row, because Fabric has no grid patterns to fall back on', () => {
    const nodes = renderList();

    expect(byRole(nodes, 'listitem').map(n => n.label)).toEqual([
      'alpha',
      'bravo',
      'charlie',
    ]);
    expect(unnamedInteractive(nodes)).toEqual([]);
  });

  it('is a single tab stop, so Tab still leaves the widget', () => {
    expectSingleTabStop(renderList());
  });

  it('numbers items from one, not zero', () => {
    const nodes = renderList();

    expect(byRole(nodes, 'listitem').map(n => n.posInSet)).toEqual([1, 2, 3]);
  });

  it('reports set size from the whole dataset, not the rendered window', () => {
    // The regression this guards: a paged list that tells the user
    // "item 1 of 3" while scrolling through four thousand commits. There is no
    // VirtualizedItem pattern on Fabric to make that self-correcting.
    const nodes = renderList({totalCount: 4000, offset: 120});

    const items = byRole(nodes, 'listitem');
    expect(items.map(n => n.setSize)).toEqual([4000, 4000, 4000]);
    expect(items.map(n => n.posInSet)).toEqual([121, 122, 123]);
  });

  it('announces an empty list as empty rather than rendering nothing', () => {
    const nodes = renderList({items: []});

    expect(byRole(nodes, 'list')[0]?.label).toBe('Repositories, empty');
  });

  it('matches the committed accessibility tree', () => {
    expect(renderList()).toMatchSnapshot();
  });
});
