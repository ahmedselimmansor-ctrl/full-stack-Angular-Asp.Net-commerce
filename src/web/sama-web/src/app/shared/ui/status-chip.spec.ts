import { describe, expect, it } from 'vitest';
import { orderStatusChip, paymentStatusChip, productStatusChip } from './status-chip';

describe('status chips', () => {
  it('maps order statuses to translation keys, not finished English', () => {
    // The chip must resolve its own text so an Arabic page does not show "Confirmed".
    expect(orderStatusChip('AwaitingPayment')).toEqual({
      label: 'status.order.awaitingPayment',
      tone: 'warn',
      translated: true,
    });
    expect(orderStatusChip('Pending').label).toBe('status.order.pending');
  });

  it('tones confirmed and shipped as positive, cancelled as critical', () => {
    expect(orderStatusChip('Confirmed').tone).toBe('good');
    expect(orderStatusChip('Shipped').tone).toBe('good');
    expect(orderStatusChip('Cancelled').tone).toBe('bad');
  });

  it('shows an unrecognised status verbatim rather than hiding it', () => {
    // A status the API has and the front end does not should be visible, not
    // silently rendered as a missing translation key.
    const chip = orderStatusChip('Refunding');
    expect(chip).toEqual({ label: 'Refunding', tone: 'neutral', translated: false });
  });

  it('maps product statuses', () => {
    expect(productStatusChip('Draft').tone).toBe('neutral');
    expect(productStatusChip('Published').label).toBe('status.product.published');
    expect(productStatusChip('Archived').tone).toBe('bad');
  });

  it('maps payment statuses', () => {
    expect(paymentStatusChip('Succeeded').label).toBe('status.payment.succeeded');
    expect(paymentStatusChip('Failed').tone).toBe('bad');
    expect(paymentStatusChip('Refunded').tone).toBe('info');
  });
});
