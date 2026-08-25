import { describe, expect, it } from 'vitest';
import { MoneyPipe } from './money.pipe';

describe('MoneyPipe', () => {
  const pipe = new MoneyPipe();

  it('formats an amount with its currency symbol', () => {
    expect(pipe.transform(19.99, 'USD', 'en-US')).toBe('$19.99');
  });

  it('respects each currency’s minor units', () => {
    // Intl knows JOD has three decimal places and JPY has none. Hard-coding two would
    // misprint prices in several of the markets Sama serves.
    expect(pipe.transform(1.5, 'JOD', 'en-US')).toContain('1.500');
    expect(pipe.transform(1500, 'JPY', 'en-US')).not.toContain('.');
  });

  it('returns an empty string rather than NaN for absent values', () => {
    // A missing price should leave a blank cell, not print "NaN" on the storefront.
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
    expect(pipe.transform(Number.NaN)).toBe('');
  });

  it('formats zero rather than treating it as absent', () => {
    expect(pipe.transform(0, 'USD', 'en-US')).toBe('$0.00');
  });
});
