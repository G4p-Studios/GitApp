/**
 * Milestone 1 shell.
 *
 * This screen exists to prove the accessibility substrate before any feature
 * depends on it: three panes cycled with F6, a list with roving focus and
 * honest set counts, announcements, and a single status live region.
 *
 * The data is placeholder. The interaction model is not.
 */

import React, {useCallback, useState} from 'react';
import {StyleSheet, Text, View} from 'react-native';

import {Announcer, Button, List, Pane, PaneHost, StatusLine} from './a11y';

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
              renderItem={(repo, _index, isActive) => (
                <View style={[styles.row, isActive && styles.rowActive]}>
                  <Text style={styles.rowName}>{repo.name}</Text>
                  <Text style={styles.rowMeta}>
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
                style={styles.button}
              />
              <Button
                label="Pull"
                onPress={() => Announcer.announce('Pull is not implemented yet')}
                accessKey="P"
                style={styles.button}
              />
              <Button
                label="Push"
                onPress={() => Announcer.announce('Push is not implemented yet')}
                accessKey="U"
                style={styles.button}
              />
            </View>
          </Pane>
        </View>

        <StatusLine style={styles.status} />
      </View>
    </PaneHost>
  );
}

const styles = StyleSheet.create({
  window: {flex: 1, backgroundColor: '#ffffff'},
  body: {flex: 1, flexDirection: 'row'},
  sidebar: {
    width: 280,
    borderRightWidth: 1,
    borderRightColor: '#d0d7de',
    padding: 12,
  },
  main: {flex: 1, padding: 20},
  paneHeading: {fontSize: 13, fontWeight: '600', marginBottom: 8, color: '#57606a'},
  heading: {fontSize: 24, fontWeight: '600', marginBottom: 4},
  meta: {fontSize: 14, color: '#57606a', marginBottom: 20},
  row: {paddingVertical: 6, paddingHorizontal: 8, borderRadius: 4},
  rowActive: {backgroundColor: '#ddf4ff'},
  rowName: {fontSize: 14, fontWeight: '500'},
  rowMeta: {fontSize: 12, color: '#57606a'},
  actions: {flexDirection: 'row', gap: 8},
  button: {
    paddingVertical: 6,
    paddingHorizontal: 14,
    borderWidth: 1,
    borderColor: '#d0d7de',
    borderRadius: 6,
    backgroundColor: '#f6f8fa',
  },
  status: {
    borderTopWidth: 1,
    borderTopColor: '#d0d7de',
    paddingVertical: 4,
    paddingHorizontal: 12,
    minHeight: 24,
  },
});
