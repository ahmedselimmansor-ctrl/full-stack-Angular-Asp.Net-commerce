import { describe, expect, it } from 'vitest';
import { orderStatusChip, paymentStatusChip, productStatusChip } from './status-chip';

describe('status chips', () => {
  it('translates the saga’s internal states into customer wording', () => {
    // "AwaitingPayment" is an accurate description of a saga step and a confusing thing to
    // show a shopper.
    expect(orderStatusChip('AwaitingPayment').label).toBe('Taking payment');
    expect(orderStatusChip('Pending').label).toBe('Processing');
  });

  it('tones a completed order positively and a cancelled one negatively', () => {
    expect(orderStatusChip('Confirmed').tone).toBe('good');
    expect(orderStatusChip('Shipped').tone).toBe('good');
    expect(orderStatusChip('Cancelled').tone).toBe('bad');
  });

  it('passes an unrecognised status through rather than hiding it', () => {
    // A status the client has not been taught about should still be visible; showing nothing
    // would make a new server-side state look like a rendering bug.
    expect(orderStatusChip('Refunding').label).toBe('Refunding');
    expect(orderStatusChip('Refunding').tone).toBe('neutral');
  });

  it('marks a draft product as not live', () => {
    expect(productStatusChip('Draft').tone).toBe('neutral');
    expect(productStatusChip('Published').tone).toBe('good');
    expect(productStatusChip('Archived').tone).toBe('bad');
  });

  it('reads a successful payment as “Paid”', () => {
    expect(paymentStatusChip('Succeeded').label).toBe('Paid');
    expect(paymentStatusChip('Failed').tone).toBe('bad');
    expect(paymentStatusChip('Refunded').tone).toBe('info');
  });
});
