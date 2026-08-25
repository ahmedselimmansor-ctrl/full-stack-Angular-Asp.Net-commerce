import { Component, input } from '@angular/core';

/**
 * Placeholder shown while data loads.
 *
 * Preferred to a spinner for list and detail views because it reserves the space the content
 * will occupy, so the page does not jump when the data lands.
 */
@Component({
  selector: 'app-skeleton',
  template: `
    @for (row of rows(); track $index) {
      <div class="bar" [style.height.px]="height()" aria-hidden="true"></div>
    }
    <span class="sr-only">Loading…</span>
  `,
  styles: `
    :host { display: block; }
    .bar {
      border-radius: var(--sama-radius-sm);
      margin-bottom: 0.6rem;
      background: linear-gradient(90deg,
        var(--sama-surface) 25%, var(--sama-surface-alt) 50%, var(--sama-surface) 75%);
      background-size: 200% 100%;
      animation: shimmer 1.4s infinite linear;
    }
    @keyframes shimmer {
      from { background-position: 200% 0; }
      to { background-position: -200% 0; }
    }
    .sr-only {
      position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px;
      overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; border: 0;
    }
  `,
})
export class Skeleton {
  readonly count = input(3);
  readonly height = input(56);

  protected rows(): readonly number[] {
    return Array.from({ length: this.count() }, (_, i) => i);
  }
}
