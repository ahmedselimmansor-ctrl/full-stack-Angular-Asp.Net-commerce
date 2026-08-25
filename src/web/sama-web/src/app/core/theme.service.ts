import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

const STORAGE_KEY = 'sama.theme';

/**
 * Three-state theme control: light, dark, or follow the operating system.
 *
 * "System" has to stay a real, distinct state rather than a starting value that
 * collapses into light or dark on first paint. Someone whose laptop switches at
 * sunset expects the shop to switch with it, and that only works if we keep
 * listening instead of freezing whatever the preference happened to be at load.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly systemPrefersDark = signal(false);

  readonly preference = signal<ThemePreference>(this.readStoredPreference());

  readonly resolved = computed<ResolvedTheme>(() => {
    const preference = this.preference();
    if (preference !== 'system') return preference;

    return this.systemPrefersDark() ? 'dark' : 'light';
  });

  constructor() {
    const view = this.document.defaultView;

    // Guarding `defaultView` alone is not enough: jsdom and some prerender hosts
    // provide a window without matchMedia, and calling it there throws inside a
    // root-provided constructor, which fails injector construction and takes the
    // whole application down rather than degrading to the light default.
    const query =
      typeof view?.matchMedia === 'function'
        ? view.matchMedia('(prefers-color-scheme: dark)')
        : null;

    if (query) {
      this.systemPrefersDark.set(query.matches);

      // Safari below 14 only has the deprecated addListener form.
      if (typeof query.addEventListener === 'function') {
        query.addEventListener('change', (event) => this.systemPrefersDark.set(event.matches));
      }
    }

    effect(() => this.apply(this.resolved(), this.preference()));
  }

  set(preference: ThemePreference): void {
    this.preference.set(preference);

    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, preference);
    } catch {
      // Private browsing denies localStorage; the choice still applies for this visit.
    }
  }

  /** Steps light to dark to system, so the toggle can be a single control. */
  cycle(): void {
    const order: ThemePreference[] = ['light', 'dark', 'system'];
    this.set(order[(order.indexOf(this.preference()) + 1) % order.length]);
  }

  private apply(resolved: ResolvedTheme, preference: ThemePreference): void {
    const root = this.document.documentElement;

    // Only an explicit choice is stamped. Leaving the attribute off under
    // "system" lets the prefers-color-scheme media query in styles.scss stay in
    // charge, which is what keeps the sunset switch working.
    if (preference === 'system') {
      root.removeAttribute('data-theme');
    } else {
      root.setAttribute('data-theme', preference);
    }

    const themeColor = this.document.querySelector('meta[name="theme-color"]');
    themeColor?.setAttribute('content', resolved === 'dark' ? '#08131A' : '#EBEEEC');
  }

  private readStoredPreference(): ThemePreference {
    try {
      const stored = this.document.defaultView?.localStorage.getItem(STORAGE_KEY);
      if (stored === 'light' || stored === 'dark' || stored === 'system') return stored;
    } catch {
      // Ignore and fall through to the system default.
    }

    return 'system';
  }
}
