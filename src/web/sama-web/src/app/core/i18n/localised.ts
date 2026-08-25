import { Pipe, PipeTransform, inject } from '@angular/core';
import { LocaleService } from './locale.service';

/**
 * Something the catalogue holds in more than one language.
 *
 * Both languages arrive in every payload, so switching language re-renders from
 * data already in memory rather than refetching.
 */
export interface Localised {
  readonly value: string;
  readonly arabic: string | null;
}

/**
 * Picks the reader's language, falling back to the base text.
 *
 * A product is often written in one language and translated later, so a missing
 * Arabic name is normal rather than exceptional. Falling back to the English name
 * leaves an Arabic page with one Latin product title on it, which is untidy but
 * legible; the alternative — hiding the product, or showing an empty heading —
 * would make an untranslated item unsellable.
 */
export function pickLocalised(locale: LocaleService, value: string, arabic: string | null): string {
  return locale.locale() === 'ar' && arabic ? arabic : value;
}

/**
 * Impure, because the locale is a signal: a pure pipe would cache the first
 * language and never re-run when the reader switches.
 */
@Pipe({ name: 'localised', pure: false })
export class LocalisedPipe implements PipeTransform {
  private readonly locale = inject(LocaleService);

  transform(value: string, arabic: string | null | undefined): string {
    return pickLocalised(this.locale, value, arabic ?? null);
  }
}
