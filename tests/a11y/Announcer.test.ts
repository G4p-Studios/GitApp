/**
 * Announcement rate limiting.
 *
 * The failure this prevents is not cosmetic. NVDA has no way to skip ahead in
 * its speech queue, so an unthrottled progress announcement buries the message
 * the user needs behind thirty stale ones, and the app appears to hang while
 * it reads them out.
 */

import {AccessibilityInfo} from 'react-native';

import {Announcer} from '../../src/a11y';

// Spy on the one method rather than replacing the whole module. A module mock
// here silently strips everything else react-native exports, and the theme
// layer needs PlatformColor at import time.
let spoken: jest.SpyInstance;

describe('Announcer', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    spoken = jest
      .spyOn(AccessibilityInfo, 'announceForAccessibility')
      .mockImplementation(() => {});
    Announcer.reset();
  });

  afterEach(() => {
    spoken.mockRestore();
    jest.useRealTimers();
  });

  it('speaks the first message immediately', () => {
    Announcer.announce('Fetched origin');

    expect(spoken).toHaveBeenCalledWith('Fetched origin');
  });

  it('drops an identical repeat, which is almost always a re-render', () => {
    Announcer.announce('Fetched origin');
    Announcer.announce('Fetched origin');

    expect(spoken).toHaveBeenCalledTimes(1);
  });

  it('coalesces polite messages inside the throttle window to the latest', () => {
    Announcer.announce('Receiving objects: 10%');
    Announcer.announce('Receiving objects: 40%');
    Announcer.announce('Receiving objects: 90%');

    // Only the first went out immediately; the rest collapse into one.
    expect(spoken).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(500);

    expect(spoken).toHaveBeenCalledTimes(2);
    expect(spoken).toHaveBeenLastCalledWith('Receiving objects: 90%');
  });

  it('lets an assertive error jump the queue', () => {
    Announcer.announce('Receiving objects: 10%');
    Announcer.announce('Clone failed: authentication required', 'assertive');

    expect(spoken).toHaveBeenLastCalledWith('Clone failed: authentication required');
  });

  it('pairs the start and end of a long operation', () => {
    const done = Announcer.operation('Fetching origin');
    expect(spoken).toHaveBeenLastCalledWith('Fetching origin');

    jest.advanceTimersByTime(500);
    done('Fetched origin, up to date');
    jest.advanceTimersByTime(500);

    expect(spoken).toHaveBeenLastCalledWith('Fetched origin, up to date');
  });

  it('ignores a second completion, so a retry cannot double-announce', () => {
    const done = Announcer.operation('Pushing');
    jest.advanceTimersByTime(500);

    done('Pushed 3 commits');
    jest.advanceTimersByTime(500);
    const after = spoken.mock.calls.length;

    done('Pushed 3 commits');
    jest.advanceTimersByTime(500);

    expect(spoken).toHaveBeenCalledTimes(after);
  });

  it('notifies status subscribers without going through the speech queue', () => {
    const listener = jest.fn();
    const unsubscribe = Announcer.subscribeToStatus(listener);

    Announcer.setStatus('Receiving objects: 40%');

    expect(listener).toHaveBeenCalledWith('Receiving objects: 40%', 'polite');
    expect(spoken).not.toHaveBeenCalled();

    unsubscribe();
  });
});
