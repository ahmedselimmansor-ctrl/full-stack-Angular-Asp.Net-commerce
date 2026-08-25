import { Pipe, PipeTransform, inject } from '@angular/core';
import { LocaleService } from './locale.service';
import type { TranslationKey } from './en';

/**
 * Impure by necessity: the locale is a signal that can change at any moment, and
 * a pure pipe would cache the first translation and never re-run. The cost is
 * bounded because every call is a dictionary lookup over a fixed key set.
 */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly locale = inject(LocaleService);

  transform(key: TranslationKey, params?: Record<string, string | number>): string {
    return this.locale.t(key, params);
  }
}

@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly locale = inject(LocaleService);

  transform(amount: number | null | undefined, currency = 'EGP'): string {
    if (amount === null || amount === undefined) return '';
    return this.locale.formatMoney(amount, currency);
  }
}

@Pipe({ name: 'localDate', pure: false })
export class LocalDatePipe implements PipeTransform {
  private readonly locale = inject(LocaleService);

  transform(value: string | Date | null | undefined): string {
    if (!value) return '';
    return this.locale.formatDate(value);
  }
}
