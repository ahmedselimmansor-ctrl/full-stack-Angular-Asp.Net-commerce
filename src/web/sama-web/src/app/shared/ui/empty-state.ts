import { Component, input } from '@angular/core';

/**
 * What a list shows when it has nothing in it.
 *
 * An empty table with headers and no rows reads as a fault. Saying plainly that there is
 * nothing yet — and offering the action that would change that — is the difference between
 * "broken" and "new".
 */
@Component({
  selector: 'app-empty-state',
  template: `
    <div class="empty">
      <div class="empty__icon" aria-hidden="true">{{ icon() }}</div>
      <h2>{{ title() }}</h2>
      @if (description()) {
        <p class="sama-muted">{{ description() }}</p>
      }
      <div class="empty__action">
        <ng-content />
      </div>
    </div>
  `,
  styles: `
    .empty {
      text-align: center;
      padding: 3.5rem 1.5rem;
      background: var(--sama-surface);
      border: 1px dashed var(--sama-border);
      border-radius: var(--sama-radius);
    }
    .empty__icon { font-size: 2rem; opacity: 0.55; margin-bottom: 0.5rem; }
    .empty h2 { font-size: 1.1rem; margin-bottom: 0.25rem; }
    .empty p { margin: 0 auto; max-width: 42ch; }
    .empty__action:not(:empty) { margin-top: 1.25rem; }
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly description = input<string>('');
  readonly icon = input<string>('◍');
}
