import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleService, type TranslationKey } from '../../core/i18n';

export type ChipTone = 'neutral' | 'good' | 'warn' | 'bad' | 'info';

export interface ChipSpec {
  /** A translation key, or the raw wire value when the status is unrecognised. */
  readonly label: TranslationKey | string;
  readonly tone: ChipTone;
  /** False when `label` is an untranslated wire value being shown verbatim. */
  readonly translated: boolean;
}

/**
 * A small labelled state marker, used for order, payment and product status.
 *
 * The chip resolves its own label from a translation key rather than being handed
 * finished text, so a status reads in Arabic on an Arabic page. An unrecognised
 * status falls through to the raw wire value: showing `Refunding` is honest,
 * whereas showing a missing-key placeholder would hide that the API has a state
 * the front end does not know about yet.
 */
@Component({
  selector: 'app-status-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="chip chip--{{ spec().tone }}">{{ text() }}</span>`,
  styles: `
    .chip {
      display: inline-block;
      padding: 0.2rem 0.6rem;
      border-radius: var(--sama-radius-pill);
      font-size: 0.78rem;
      font-weight: 600;
      white-space: nowrap;
    }

    .chip--neutral { background: var(--surface-2); color: var(--text-soft); }
    .chip--good { background: var(--positive-wash); color: var(--positive); }
    .chip--warn { background: var(--caution-wash); color: var(--caution); }
    .chip--bad { background: var(--critical-wash); color: var(--critical); }
    .chip--info { background: var(--accent-wash); color: var(--accent); }
  `,
})
export class StatusChip {
  private readonly locale = inject(LocaleService);

  readonly spec = input.required<ChipSpec>();

  protected readonly text = computed(() => {
    const spec = this.spec();
    return spec.translated ? this.locale.t(spec.label as TranslationKey) : spec.label;
  });
}

const unknown = (status: string): ChipSpec => ({ label: status, tone: 'neutral', translated: false });

const known = (label: TranslationKey, tone: ChipTone): ChipSpec => ({ label, tone, translated: true });

/** Maps a wire order status onto customer-facing wording and a tone. */
export function orderStatusChip(status: string): ChipSpec {
  switch (status) {
    case 'Pending': return known('status.order.pending', 'info');
    case 'AwaitingPayment': return known('status.order.awaitingPayment', 'warn');
    case 'Confirmed': return known('status.order.confirmed', 'good');
    case 'Shipped': return known('status.order.shipped', 'good');
    case 'Cancelled': return known('status.order.cancelled', 'bad');
    default: return unknown(status);
  }
}

export function productStatusChip(status: string): ChipSpec {
  switch (status) {
    case 'Published': return known('status.product.published', 'good');
    case 'Draft': return known('status.product.draft', 'neutral');
    case 'Archived': return known('status.product.archived', 'bad');
    default: return unknown(status);
  }
}

export function paymentStatusChip(status: string): ChipSpec {
  switch (status) {
    case 'Succeeded': return known('status.payment.succeeded', 'good');
    case 'Pending': return known('status.payment.pending', 'warn');
    case 'Failed': return known('status.payment.failed', 'bad');
    case 'Refunded': return known('status.payment.refunded', 'info');
    default: return unknown(status);
  }
}
