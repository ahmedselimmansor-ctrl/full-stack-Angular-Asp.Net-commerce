import { Component, computed, input } from '@angular/core';

export type ChipTone = 'neutral' | 'good' | 'warn' | 'bad' | 'info';

/** A small labelled state marker, used for order, payment and product status. */
@Component({
  selector: 'app-status-chip',
  template: `<span class="chip chip--{{ tone() }}">{{ label() }}</span>`,
  styles: `
    .chip {
      display: inline-block;
      padding: 0.2rem 0.6rem;
      border-radius: 999px;
      font-size: 0.78rem;
      font-weight: 600;
      white-space: nowrap;
    }
    .chip--neutral { background: var(--sama-surface-alt); color: var(--sama-ink-soft); }
    .chip--good { background: color-mix(in srgb, var(--sama-success) 16%, transparent); color: var(--sama-success); }
    .chip--warn { background: color-mix(in srgb, #b8860b 18%, transparent); color: #8a6508; }
    .chip--bad  { background: color-mix(in srgb, var(--sama-danger) 14%, transparent); color: var(--sama-danger); }
    .chip--info { background: var(--sama-accent-soft); color: var(--sama-accent); }
  `,
})
export class StatusChip {
  readonly label = input.required<string>();
  readonly tone = input<ChipTone>('neutral');
}

/** Maps a wire status onto customer-facing wording and a tone. */
export function orderStatusChip(status: string): { label: string; tone: ChipTone } {
  switch (status) {
    case 'Pending': return { label: 'Processing', tone: 'info' };
    case 'AwaitingPayment': return { label: 'Taking payment', tone: 'warn' };
    case 'Confirmed': return { label: 'Confirmed', tone: 'good' };
    case 'Shipped': return { label: 'Shipped', tone: 'good' };
    case 'Cancelled': return { label: 'Cancelled', tone: 'bad' };
    default: return { label: status, tone: 'neutral' };
  }
}

export function productStatusChip(status: string): { label: string; tone: ChipTone } {
  switch (status) {
    case 'Published': return { label: 'Published', tone: 'good' };
    case 'Draft': return { label: 'Draft', tone: 'neutral' };
    case 'Archived': return { label: 'Archived', tone: 'bad' };
    default: return { label: status, tone: 'neutral' };
  }
}

export function paymentStatusChip(status: string): { label: string; tone: ChipTone } {
  switch (status) {
    case 'Succeeded': return { label: 'Paid', tone: 'good' };
    case 'Pending': return { label: 'Pending', tone: 'warn' };
    case 'Failed': return { label: 'Failed', tone: 'bad' };
    case 'Refunded': return { label: 'Refunded', tone: 'info' };
    default: return { label: status, tone: 'neutral' };
  }
}
