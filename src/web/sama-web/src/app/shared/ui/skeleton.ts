import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { LocaleService } from '../../core/i18n';

/**
 * Placeholder shown while data loads.
 *
 * Preferred to a spinner for list and detail views because it reserves the space
 * the content will occupy, so the page does not jump when the data lands.
 */
@Component({
  selector: 'app-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (row of rows(); track $index) {
      <div class="bar" [style.height.px]="height()" aria-hidden="true"></div>
    }
    <span class="sr-only">{{ locale.t('common.loading') }}</span>
  `,
  styles: `
    :host { display: block; }

    .bar {
      border-radius: var(--sama-radius-sm);
      margin-block-end: 0.6rem;
      background: linear-gradient(90deg, var(--surface) 25%, var(--surface-2) 50%, var(--surface) 75%);
      background-size: 200% 100%;
      animation: shimmer 1.4s infinite linear;
    }

    /* The shimmer sweeps the way the language reads, so it does not run backwards
       against Arabic text. */
    @keyframes shimmer {
      from { background-position: 200% 0; }
      to { background-position: -200% 0; }
    }

    :host-context([dir='rtl']) .bar { animation-direction: reverse; }
  `,
})
export class Skeleton {
  protected readonly locale = inject(LocaleService);

  readonly count = input(3);
  readonly height = input(56);

  protected rows(): readonly number[] {
    return Array.from({ length: this.count() }, (_, i) => i);
  }
}
