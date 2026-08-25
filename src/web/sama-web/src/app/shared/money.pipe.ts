import { Pipe, type PipeTransform } from '@angular/core';

/**
 * Formats a minor-unit-aware amount with its currency.
 *
 * Uses `Intl.NumberFormat`, which knows that JOD has three decimal places and JPY has none —
 * hard-coding two would misprint prices in several of the markets Sama supports.
 */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  private static readonly cache = new Map<string, Intl.NumberFormat>();

  transform(value: number | null | undefined, currency = 'USD', locale = 'en'): string {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '';
    }

    const key = `${locale}:${currency}`;
    let formatter = MoneyPipe.cache.get(key);

    if (!formatter) {
      // Constructing one of these is comparatively expensive, and a product grid formats
      // dozens of prices per render.
      formatter = new Intl.NumberFormat(locale, { style: 'currency', currency });
      MoneyPipe.cache.set(key, formatter);
    }

    return formatter.format(value);
  }
}
