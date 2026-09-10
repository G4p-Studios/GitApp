/**
 * Milestone 1 shell.
 *
 * This screen exists to prove the accessibility substrate before any feature
 * depends on it: panes cycled with F6, a list with roving focus and honest
 * set counts, announcements, and a single status live region.
 *
 * The data is placeholder. The interaction model is not.
 *
 * Every colour comes from src/theme. See the note there on why the Fluent
 * brush names are unsafe under Fabric.
 */

import React, {useCallback, useMemo, useState} from 'react';
import {StyleSheet, Text, View} from 'react-native';

import {Announcer, Button, List, Pane, PaneHost, StatusLine} from './a11y';
import {metrics, space, type, useTheme, type Palette} from './theme';

type Repo = {
  id: string;
  name: string;
  branch: string;
  ahead: number;
  behind: number;
};

// Placeholder until GitModule lands in milestone 2.
const REPOS: Repo[] = [
  {id: '1', name: 'gitapp', branch: 'main', ahead: 2, behind: 0},
  {id: '2', name: 'react-native-windows', branch: 'main', ahead: 0, behind: 14},
  {id: '3', name: 'nvda', branch: 'master', ahead: 0, behind: 0},
  {id: '4', name: 'dotfiles', branch: 'main', ahead: 1, behind: 3},
];

function describeSync(repo: Repo): string {
  if (repo.ahead === 0 && repo.behind === 0) {
    return 'up to date';
  }

  const parts: string[] = [];
  if (repo.ahead > 0) {
    parts.push(`${repo.ahead} ahead`);
  }
  if (repo.behind > 0) {
    parts.push(`${repo.behind} behind`);
  }
  return parts.join(', ');
}

export default function App() {
  const [selected, setSelected] = useState<Repo>(REPOS[0]);
  const {color} = useTheme();
  const styles = useMemo(() => makeStyles(color), [color]);

  const onFetch = useCallback(() => {
    // The operation() pairing is mandatory for anything that can outlast a
    // second: silence during a long Git operation reads as a hang.
    const done = Announcer.operation(`Fetching ${selected.name}`);
    Announcer.setStatus(`Fetching ${selected.name}...`);

    setTimeout(() => done(`Fetched ${selected.name}, up to date`), 1200);
  }, [selected]);

  return (
    <PaneHost>
      <View style={styles.window}>
        <View style={styles.body}>
          <Pane id="repos" name="Repositories" order={10} style={styles.sidebar}>
            <Text style={styles.paneHeading} accessibilityLevel={2}>
              Repositories
            </Text>
            <List
              label="Repositories"
              items={REPOS}
              keyExtractor={repo => repo.id}
              // The whole row as one string: Fabric has no grid patterns, so
              // this is the only thing the screen reader will announce.
              labelExtractor={repo =>
                `${repo.name}, ${repo.branch}, ${describeSync(repo)}`
              }
              descriptionExtractor={() => 'Name, branch, sync state'}
              onActivate={repo => {
                setSelected(repo);
                Announcer.announce(`Opened ${repo.name}`);
              }}
              // Without flex the list is zero-height inside a column pane and
              // renders nothing at all.
              style={styles.list}
              renderItem={(repo, _index, isActive) => (
                <View style={styles.row}>
                  <Text
                    style={[styles.rowName, isActive && styles.rowTextSelected]}>
                    {repo.name}
                  </Text>
                  <Text
                    style={[styles.rowMeta, isActive && styles.rowTextSelected]}>
                    {repo.branch} &middot; {describeSync(repo)}
                  </Text>
                </View>
              )}
            />
          </Pane>

          <Pane id="detail" name="Repository details" order={20} style={styles.main}>
            <Text style={styles.heading} accessibilityLevel={1}>
              {selected.name}
            </Text>
            <Text style={styles.meta}>
              On branch {selected.branch}, {describeSync(selected)}
            </Text>

            <View style={styles.actions}>
              <Button
                label="Fetch"
                hint="Downloads new commits without changing your working tree"
                onPress={onFetch}
                accessKey="F"
              />
              <Button
                label="Pull"
                onPress={() => Announcer.announce('Pull is not implemented yet')}
                accessKey="P"
              />
              <Button
                label="Push"
                onPress={() => Announcer.announce('Push is not implemented yet')}
                accessKey="U"
              />
            </View>
          </Pane>
        </View>

        <StatusLine style={styles.status} />
      </View>
    </PaneHost>
  );
}

const makeStyles = (color: Palette) =>
  StyleSheet.create({
  window: {flex: 1, backgroundColor: color.background},
  body: {flex: 1, flexDirection: 'row'},
  sidebar: {
    width: metrics.sidebarWidth,
    borderRightWidth: metrics.borderWidth,
    borderRightColor: color.borderSubtle,
    paddingVertical: space.sm,
    paddingHorizontal: space.sm,
  },
  list: {flex: 1},
  main: {flex: 1, padding: space.xxl},
  paneHeading: {
    ...type.caption,
    fontWeight: '600',
    color: color.textSecondary,
    marginBottom: space.xs,
    paddingHorizontal: space.xs,
  },
  heading: {...type.title, color: color.text, marginBottom: space.xs},
  meta: {...type.body, color: color.textSecondary, marginBottom: space.xl},
  row: {
    paddingVertical: space.xs,
    paddingHorizontal: space.sm,
    justifyContent: 'center',
  },
  rowName: {...type.body, color: color.text},
  rowMeta: {...type.caption, color: color.textSecondary},
  // Selected rows use the system Highlight pair. Both halves must change
  // together or high contrast themes produce unreadable rows.
  rowTextSelected: {color: color.selectedText},
  actions: {flexDirection: 'row', gap: space.sm},
  status: {
    borderTopWidth: metrics.borderWidth,
    borderTopColor: color.borderSubtle,
    paddingVertical: space.xs,
    paddingHorizontal: space.md,
    minHeight: 24,
    justifyContent: 'center',
  },
  });
