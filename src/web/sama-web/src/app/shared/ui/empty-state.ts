import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * What a list shows when it has nothing in it.
 *
 * An empty table with headers and no rows reads as a fault. Saying plainly that
 * there is nothing yet — and offering the action that would change that — is the
 * difference between "broken" and "new".
 *
 * The icon is SVG path data rather than a glyph. The typographic symbols this
 * used before (⛃, ✇, ⌸) are outside the ranges most UI fonts cover, so they
 * rendered as tofu boxes on the machines that lacked them — turning the one
 * decoration on a reassuring screen into evidence that something is broken.
 */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty">
      <svg class="empty__icon" viewBox="0 0 20 20" aria-hidden="true" [innerHTML]="icon()"></svg>
      <h2>{{ title() }}</h2>
      @if (description()) {
        <p class="muted">{{ description() }}</p>
      }
      <div class="empty__action">
        <ng-content />
      </div>
    </div>
  `,
  styles: `
    .empty {
      text-align: center;
      padding: clamp(2.5rem, 6vw, 3.5rem) 1.5rem;
      background: var(--surface);
      border: 1px dashed var(--line);
      border-radius: var(--sama-radius);
    }

    .empty__icon {
      inline-size: 30px;
      block-size: 30px;
      margin: 0 auto 0.85rem;
      fill: none;
      stroke: var(--text-faint);
      stroke-width: 1.3;
      stroke-linecap: round;
      stroke-linejoin: round;
    }

    .empty h2 { font-size: 1.1rem; margin-block-end: 0.25rem; }
    .empty p { margin: 0 auto; max-inline-size: 42ch; }
    .empty__action:not(:empty) { margin-block-start: 1.25rem; }
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly description = input<string>('');

  /** SVG path data drawn on a 20x20 viewBox. See `EmptyIcons` for the shared set. */
  readonly icon = input<string>(
    '<circle cx="10" cy="10" r="7"/><path d="M7.4 10h5.2"/>',
  );
}

/** The icons the back-office sections use, matching the shapes in the side rail. */
export const EmptyIcons = {
  product: '<path d="M10 2.5l7 3.7v7.6l-7 3.7-7-3.7V6.2z"/><path d="M3 6.2l7 3.8 7-3.8M10 10v7.5"/>',
  category: '<path d="M3 5.5h5l1.5 2H17v7a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 3 14.5z"/>',
  stock: '<ellipse cx="10" cy="5" rx="6.5" ry="2.5"/><path d="M3.5 5v10c0 1.4 2.9 2.5 6.5 2.5s6.5-1.1 6.5-2.5V5M3.5 10c0 1.4 2.9 2.5 6.5 2.5s6.5-1.1 6.5-2.5"/>',
  order: '<path d="M4 3.5h9l3 3v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-12a1 1 0 0 1 1-1z"/><path d="M12.5 3.5v3.5H16M6 10h8M6 13h5"/>',
  payment: '<rect x="2.5" y="4.5" width="15" height="11" rx="1.6"/><path d="M2.5 8.5h15M5.5 12.5h3"/>',
  user: '<circle cx="10" cy="7" r="3"/><path d="M4 16.5a6 6 0 0 1 12 0"/>',
} as const;
