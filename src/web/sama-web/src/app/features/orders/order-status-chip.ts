import { Component, computed, input } from '@angular/core';
import type { OrderStatus } from '../../core/models';

/**
 * Renders an order's saga state in language a customer understands.
 *
 * "AwaitingPayment" is an accurate description of the saga's internal step and a confusing
 * thing to show a shopper, so the wire value is mapped to plain wording here rather than
 * leaking the state machine into the UI.
 */
@Component({
  selector: 'app-order-status-chip',
  template: `<span class="chip chip--{{ tone() }}">{{ label() }}</span>`,
  styles: `
    .chip {
      display: inline-block; padding: 0.2rem 0.6rem; border-radius: 999px;
      font-size: 0.78rem; font-weight: 600; white-space: nowrap;
    }
    .chip--pending { background: var(--sama-surface-alt); color: var(--sama-ink-soft); }
    .chip--good { background: color-mix(in srgb, var(--sama-success) 16%, transparent); color: var(--sama-success); }
    .chip--bad { background: color-mix(in srgb, var(--sama-danger) 14%, transparent); color: var(--sama-danger); }
  `,
})
export class OrderStatusChip {
  readonly status = input.required<OrderStatus>();

  protected readonly label = computed(() => {
    switch (this.status()) {
      case 'Pending': return 'Processing';
      case 'AwaitingPayment': return 'Taking payment';
      case 'Confirmed': return 'Confirmed';
      case 'Shipped': return 'Shipped';
      case 'Cancelled': return 'Cancelled';
      default: return this.status();
    }
  });

  protected readonly tone = computed(() => {
    switch (this.status()) {
      case 'Confirmed':
      case 'Shipped': return 'good';
      case 'Cancelled': return 'bad';
      default: return 'pending';
    }
  });
}
