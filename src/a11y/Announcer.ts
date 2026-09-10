/**
 * Screen reader announcements.
 *
 * Two channels, deliberately distinct:
 *
 *   announce()   discrete events, spoken once. "Pushed 3 commits to origin/main".
 *   setStatus()  text in a live region that updates in place. Fetch progress.
 *
 * Both are rate limited. An unthrottled announcer is worse than none: a Git
 * operation emitting progress on every object would flood the speech queue and
 * bury the message the user actually needs, and NVDA has no way to skip ahead.
 *
 * See docs/ARCHITECTURE.md section 3.5.
 */

import {AccessibilityInfo} from 'react-native';

/** Minimum gap between two spoken announcements, in milliseconds. */
const THROTTLE_MS = 500;

export type Urgency = 'polite' | 'assertive';

type Pending = {
  message: string;
  urgency: Urgency;
};

type StatusListener = (text: string, urgency: Urgency) => void;

class AnnouncerImpl {
  private lastSpokenAt = 0;
  private lastMessage: string | null = null;
  private pending: Pending | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  private statusText = '';
  private statusUrgency: Urgency = 'polite';
  private readonly statusListeners = new Set<StatusListener>();

  /**
   * Speak a discrete message.
   *
   * Assertive messages jump the queue and are never coalesced away, because
   * they are errors that stop the user's task. Polite messages arriving inside
   * the throttle window replace any other polite message still waiting, which
   * is what makes progress reporting safe: the user hears the latest state
   * rather than a backlog of stale ones.
   */
  announce(message: string, urgency: Urgency = 'polite'): void {
    const text = message.trim();
    if (!text) {
      return;
    }

    // Identical consecutive messages are almost always a re-render, not new
    // information. Dropping them is the difference between a usable app and
    // one that repeats itself.
    if (text === this.lastMessage && urgency !== 'assertive') {
      return;
    }

    if (urgency === 'assertive') {
      this.speakNow(text, urgency);
      return;
    }

    const elapsed = Date.now() - this.lastSpokenAt;
    if (elapsed >= THROTTLE_MS && !this.timer) {
      this.speakNow(text, urgency);
      return;
    }

    this.pending = {message: text, urgency};
    this.scheduleFlush(Math.max(0, THROTTLE_MS - elapsed));
  }

  /**
   * Set the text of the application status live region. Unlike announce(),
   * repeated identical values are a no-op and cost nothing, so callers may set
   * this as often as they like.
   */
  setStatus(text: string, urgency: Urgency = 'polite'): void {
    if (text === this.statusText && urgency === this.statusUrgency) {
      return;
    }

    this.statusText = text;
    this.statusUrgency = urgency;

    for (const listener of this.statusListeners) {
      listener(text, urgency);
    }
  }

  clearStatus(): void {
    this.setStatus('');
  }

  getStatus(): {text: string; urgency: Urgency} {
    return {text: this.statusText, urgency: this.statusUrgency};
  }

  subscribeToStatus(listener: StatusListener): () => void {
    this.statusListeners.add(listener);
    return () => {
      this.statusListeners.delete(listener);
    };
  }

  /**
   * Announce the start of a long operation, and hand back a completion
   * function. Silence during a long Git operation reads as a hang, so this
   * pairing is mandatory for anything that can outlast a second.
   */
  operation(startMessage: string): (endMessage: string, urgency?: Urgency) => void {
    this.announce(startMessage);

    let settled = false;
    return (endMessage: string, urgency: Urgency = 'polite') => {
      if (settled) {
        return;
      }
      settled = true;
      this.clearStatus();
      this.announce(endMessage, urgency);
    };
  }

  /** Test seam: drop queued state between cases. */
  reset(): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    this.pending = null;
    this.lastMessage = null;
    this.lastSpokenAt = 0;
    this.statusText = '';
    this.statusUrgency = 'polite';
  }

  private scheduleFlush(delay: number): void {
    if (this.timer) {
      return;
    }
    this.timer = setTimeout(() => {
      this.timer = null;
      const next = this.pending;
      this.pending = null;
      if (next) {
        this.speakNow(next.message, next.urgency);
      }
    }, delay);
  }

  private speakNow(text: string, _urgency: Urgency): void {
    this.lastSpokenAt = Date.now();
    this.lastMessage = text;
    AccessibilityInfo.announceForAccessibility(text);
  }
}

export const Announcer = new AnnouncerImpl();
