import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleService } from '../../../core/i18n';

export interface BreakdownSlice {
  readonly label: string;
  readonly value: number;
  readonly tone: 'accent' | 'good' | 'warn' | 'bad' | 'neutral';
}

/**
 * Proportions of a whole, as one stacked bar with a legend.
 *
 * A bar rather than a doughnut: comparing angles is harder than comparing
 * lengths, and with five order statuses the small slices of a doughnut become
 * unreadable slivers. The legend carries the counts, so nobody has to estimate a
 * value off the picture.
 */
@Component({
  selector: 'app-breakdown-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (total() === 0) {
      <p class="empty muted">{{ emptyLabel() }}</p>
    } @else {
      <!-- Decorative: the legend below states every label and count in text,
           so announcing the bar as well would just repeat the whole list. -->
      <div class="bar" aria-hidden="true">
        @for (slice of slices(); track slice.label) {
          <span
            class="bar__part"
            [class]="'tone-' + slice.tone"
            [style.flex-basis.%]="slice.percent"
            [attr.title]="slice.label + ': ' + slice.readable"
          ></span>
        }
      </div>

      <ul class="legend">
        @for (slice of slices(); track slice.label) {
          <li class="legend__item">
            <span class="legend__swatch" [class]="'tone-' + slice.tone" aria-hidden="true"></span>
            <span class="legend__label">{{ slice.label }}</span>
            <span class="legend__value">{{ slice.readable }}</span>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './breakdown-bar.scss',
})
export class BreakdownBar {
  private readonly locale = inject(LocaleService);

  readonly data = input.required<readonly BreakdownSlice[]>();
  readonly emptyLabel = input('');

  protected readonly total = computed(() => this.data().reduce((sum, slice) => sum + slice.value, 0));

  protected readonly slices = computed(() => {
    const total = this.total() || 1;

    return this.data()
      .filter((slice) => slice.value > 0)
      .map((slice) => ({
        ...slice,
        percent: (slice.value / total) * 100,
        readable: this.locale.formatNumber(slice.value),
      }));
  });
}
