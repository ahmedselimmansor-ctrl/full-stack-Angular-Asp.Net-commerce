import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LocaleService } from '../core/i18n';

/**
 * Each language is labelled in its own script — "العربية", never "Arabic".
 * Someone looking for Arabic is scanning for Arabic letterforms, and a Latin
 * label is invisible to that scan.
 */
@Component({
  selector: 'app-locale-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="lang"
      [attr.aria-label]="locale.t('locale.switchTo')"
      [attr.title]="locale.t('locale.switchTo')"
      (click)="locale.toggle()"
    >
      <span class="lang__active">{{ locale.locale() === 'ar' ? 'ع' : 'EN' }}</span>
      <span class="lang__other" aria-hidden="true">{{ locale.locale() === 'ar' ? 'EN' : 'ع' }}</span>
    </button>
  `,
  styles: `
    .lang {
      display: inline-flex;
      align-items: baseline;
      gap: 0.3rem;
      padding: 0.32rem 0.7rem;
      border: 1px solid var(--line);
      border-radius: var(--sama-radius-pill);
      background: var(--surface-2);
      color: var(--text);
      font: inherit;
      font-size: 0.82rem;
      font-weight: 600;
      line-height: 1.5;
      cursor: pointer;
      transition: border-color 140ms var(--sama-ease), background-color 140ms var(--sama-ease);
    }

    .lang:hover { border-color: var(--line-strong); background: var(--surface); }

    .lang__active { color: var(--accent); }
    .lang__other { color: var(--text-faint); font-size: 0.74rem; }
  `,
})
export class LocaleToggle {
  protected readonly locale = inject(LocaleService);
}
