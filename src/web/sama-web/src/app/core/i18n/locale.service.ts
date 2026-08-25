import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';
import { en, type TranslationKey, type Translations } from './en';
import { ar } from './ar';

export type Locale = 'en' | 'ar';
export type Direction = 'ltr' | 'rtl';

const STORAGE_KEY = 'sama.locale';

const DICTIONARIES: Record<Locale, Translations> = { en, ar };

const DIRECTIONS: Record<Locale, Direction> = { en: 'ltr', ar: 'rtl' };

/**
 * Intl tags, not the bare locale. `ar-EG` defaults to Arabic-Indic digits, which
 * would put ١٢٣ next to a price while the rest of the shop shows 123. Forcing
 * the Latin numbering system keeps figures scannable and consistent, which is
 * what commerce in Egypt and the Gulf actually uses.
 */
const INTL_TAGS: Record<Locale, string> = { en: 'en-GB', ar: 'ar-EG-u-nu-latn' };

@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly document = inject(DOCUMENT);

  readonly locale = signal<Locale>(this.readStoredLocale());
  readonly direction = computed<Direction>(() => DIRECTIONS[this.locale()]);
  readonly isRtl = computed(() => this.direction() === 'rtl');

  private readonly dictionary = computed(() => DICTIONARIES[this.locale()]);

  constructor() {
    effect(() => this.apply(this.locale(), this.direction()));
  }

  /**
   * Looks up a key and fills `{name}` placeholders.
   *
   * A missing key returns the key itself rather than an empty string: a visible
   * `catalog.title` on the page is a bug someone reports, whereas a blank
   * heading looks like an intentionally empty section and ships unnoticed.
   */
  t(key: TranslationKey, params?: Record<string, string | number>): string {
    const template = this.dictionary()[key] ?? en[key] ?? key;
    if (!params) return template;

    return template.replace(/\{(\w+)\}/g, (match, name: string) =>
      Object.hasOwn(params, name) ? String(params[name]) : match,
    );
  }

  set(locale: Locale): void {
    this.locale.set(locale);

    try {
      this.document.defaultView?.localStorage.setItem(STORAGE_KEY, locale);
    } catch {
      // Private browsing denies localStorage; the choice still holds for this visit.
    }
  }

  toggle(): void {
    this.set(this.locale() === 'en' ? 'ar' : 'en');
  }

  formatMoney(amount: number, currency: string): string {
    return new Intl.NumberFormat(INTL_TAGS[this.locale()], {
      style: 'currency',
      currency,
      currencyDisplay: 'narrowSymbol',
    }).format(amount);
  }

  formatNumber(value: number): string {
    return new Intl.NumberFormat(INTL_TAGS[this.locale()]).format(value);
  }

  formatDate(value: string | Date): string {
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return '';

    return new Intl.DateTimeFormat(INTL_TAGS[this.locale()], {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    }).format(date);
  }

  private apply(locale: Locale, direction: Direction): void {
    const root = this.document.documentElement;
    root.setAttribute('lang', locale);
    root.setAttribute('dir', direction);
  }

  private readStoredLocale(): Locale {
    try {
      const stored = this.document.defaultView?.localStorage.getItem(STORAGE_KEY);
      if (stored === 'en' || stored === 'ar') return stored;
    } catch {
      // Fall through to the browser's own languages.
    }

    const languages = this.document.defaultView?.navigator.languages ?? [];
    return languages.some((tag) => tag.toLowerCase().startsWith('ar')) ? 'ar' : 'en';
  }
}
