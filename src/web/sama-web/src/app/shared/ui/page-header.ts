import { Component, input } from '@angular/core';

/**
 * Consistent page heading with an optional description and an action slot.
 *
 * Every page having its own heading markup is how a UI drifts: one page ends up with a
 * different heading size, another loses its description, and the whole thing stops feeling
 * like one product.
 */
@Component({
  selector: 'app-page-header',
  template: `
    <header class="header">
      <div class="header__text">
        <h1>{{ title() }}</h1>
        @if (description()) {
          <p class="sama-muted">{{ description() }}</p>
        }
      </div>
      <div class="header__actions">
        <ng-content />
      </div>
    </header>
  `,
  styles: `
    .header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 1.5rem;
      flex-wrap: wrap;
      margin-bottom: 1.75rem;
    }
    .header__text p { margin: 0.25rem 0 0; }
    .header__actions { display: flex; gap: 0.5rem; flex-shrink: 0; }
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly description = input<string>('');
}
