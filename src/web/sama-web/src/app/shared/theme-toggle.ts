import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ThemeService, type ThemePreference } from '../core/theme.service';
import { LocaleService } from '../core/i18n';

/**
 * A three-segment control rather than a cycling button.
 *
 * "Match system" is a real state, not a default that collapses into light or
 * dark, so it needs a visible position of its own. A two-state toggle would
 * silently strand anyone whose laptop switches at sunset.
 */
@Component({
  selector: 'app-theme-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <fieldset class="seg">
      <legend class="sr-only">{{ locale.t('theme.label') }}</legend>

      @for (option of options; track option.value) {
        <button
          type="button"
          class="seg__btn"
          [class.is-on]="theme.preference() === option.value"
          [attr.aria-pressed]="theme.preference() === option.value"
          [attr.title]="locale.t(option.label)"
          (click)="theme.set(option.value)"
        >
          <span class="sr-only">{{ locale.t(option.label) }}</span>
          <svg viewBox="0 0 20 20" aria-hidden="true" [innerHTML]="option.icon"></svg>
        </button>
      }
    </fieldset>
  `,
  styles: `
    .seg {
      display: inline-flex;
      gap: 2px;
      margin: 0;
      padding: 3px;
      border: 1px solid var(--line-strong);
      border-radius: var(--sama-radius-pill);
      background: var(--surface-2);
    }

    .seg__btn {
      display: grid;
      place-items: center;
      inline-size: 30px;
      block-size: 26px;
      padding: 0;
      border: 0;
      border-radius: var(--sama-radius-pill);
      background: transparent;
      color: var(--text-faint);
      cursor: pointer;
      transition: background-color 140ms var(--sama-ease), color 140ms var(--sama-ease);
    }

    .seg__btn:hover { color: var(--text); }

    .seg__btn.is-on {
      background: var(--surface);
      color: var(--accent);
      box-shadow: var(--sama-shadow-sm);
    }

    svg {
      inline-size: 15px;
      block-size: 15px;
      fill: none;
      stroke: currentColor;
      stroke-width: 1.6;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
  `,
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeService);
  protected readonly locale = inject(LocaleService);

  protected readonly options: ReadonlyArray<{
    value: ThemePreference;
    label: 'theme.light' | 'theme.dark' | 'theme.system';
    icon: string;
  }> = [
    {
      value: 'light',
      label: 'theme.light',
      icon: `<circle cx="10" cy="10" r="3.4"/><path d="M10 2.2v1.6M10 16.2v1.6M17.8 10h-1.6M3.8 10H2.2M15.5 4.5l-1.1 1.1M5.6 14.4l-1.1 1.1M15.5 15.5l-1.1-1.1M5.6 5.6L4.5 4.5"/>`,
    },
    {
      value: 'system',
      label: 'theme.system',
      icon: `<rect x="2.5" y="4" width="15" height="10" rx="1.6"/><path d="M7 17h6M10 14v3"/>`,
    },
    {
      value: 'dark',
      label: 'theme.dark',
      icon: `<path d="M16.2 11.8A6.8 6.8 0 0 1 8.2 3.8a6.8 6.8 0 1 0 8 8Z"/>`,
    },
  ];
}
