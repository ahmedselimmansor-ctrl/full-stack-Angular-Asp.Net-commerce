import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleService } from '../../../core/i18n';

export interface TrendPoint {
  readonly label: string;
  readonly iso: string;
  readonly value: number;
}

/**
 * A daily trend, drawn as bars in inline SVG.
 *
 * Hand-drawn rather than pulled from a charting library: this needs one shape,
 * and a library would add far more weight to the back-office bundle than the
 * forty lines it replaces, while making it harder to have the bars inherit the
 * theme tokens and flip with the language.
 *
 * The figures are also published as a real table behind `.sr-only`. A chart is a
 * picture of numbers, and a screen reader cannot read a picture.
 */
@Component({
  selector: 'app-trend-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="chart">
      <figcaption class="chart__caption">
        <span>{{ caption() }}</span>
        <strong class="chart__peak">{{ formattedPeak() }}</strong>
      </figcaption>

      <!--
        Pinned to ltr: the horizontal axis is time, and mirroring it would put the
        most recent day on the left in Arabic while the numbers beside it still
        read left to right. Time flowing one way is the stronger convention here.
      -->
      <div class="chart__plot" dir="ltr">
        <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="none" aria-hidden="true">
          @for (bar of bars(); track bar.iso) {
            <rect
              class="chart__bar"
              [class.is-zero]="bar.value === 0"
              [attr.x]="bar.x"
              [attr.y]="bar.y"
              [attr.width]="bar.width"
              [attr.height]="bar.height"
              rx="2"
            >
              <title>{{ bar.label }}: {{ format(bar.value) }}</title>
            </rect>
          }
        </svg>

        <div class="chart__axis">
          @for (bar of bars(); track bar.iso) {
            <span class="chart__tick">{{ bar.tick }}</span>
          }
        </div>
      </div>

      <table class="sr-only">
        <caption>{{ tableCaption() || caption() }}</caption>
        <tbody>
          @for (point of points(); track point.iso) {
            <tr>
              <th scope="row">{{ point.label }}</th>
              <td>{{ format(point.value) }}</td>
            </tr>
          }
        </tbody>
      </table>
    </figure>
  `,
  styleUrl: './trend-chart.scss',
})
export class TrendChart {
  private readonly locale = inject(LocaleService);

  readonly points = input.required<readonly TrendPoint[]>();
  readonly caption = input('');

  /** Names the data for screen readers; the visible heading already names the chart. */
  readonly tableCaption = input('');
  readonly currency = input<string | null>(null);

  protected readonly width = 100;
  protected readonly height = 40;

  private readonly peak = computed(() => Math.max(0, ...this.points().map((p) => p.value)));

  protected readonly formattedPeak = computed(() => this.format(this.peak()));

  protected readonly bars = computed(() => {
    const points = this.points();
    if (!points.length) return [];

    // A flat run of zeroes would otherwise divide by zero and draw full-height
    // bars, reading as a record week when nothing sold at all.
    const peak = this.peak() || 1;
    const slot = this.width / points.length;
    const barWidth = Math.max(slot * 0.62, 1);

    return points.map((point, index) => {
      // Zero keeps a hairline so the day is still visibly present in the series
      // rather than looking like missing data.
      const height = point.value === 0 ? 0.8 : Math.max((point.value / peak) * this.height, 1.4);

      return {
        ...point,
        x: index * slot + (slot - barWidth) / 2,
        y: this.height - height,
        width: barWidth,
        height,
        // Only every other tick is labelled, or fourteen dates collide on a phone.
        tick: index % 2 === 0 ? point.label : '',
      };
    });
  });

  protected format(value: number): string {
    const currency = this.currency();
    return currency ? this.locale.formatMoney(value, currency) : this.locale.formatNumber(value);
  }
}
