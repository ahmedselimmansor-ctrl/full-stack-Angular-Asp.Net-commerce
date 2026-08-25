import { TestBed } from '@angular/core/testing';
import { describe, expect, it, beforeEach } from 'vitest';
import { LocaleService } from './locale.service';
import { pickLocalised } from './localised';

describe('pickLocalised', () => {
  let locale: LocaleService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    locale = TestBed.inject(LocaleService);
  });

  it('shows the base text in English even when a translation exists', () => {
    locale.set('en');
    expect(pickLocalised(locale, 'Faded linen shirt', 'قميص كتان مغسول')).toBe('Faded linen shirt');
  });

  it('shows the translation in Arabic', () => {
    locale.set('ar');
    expect(pickLocalised(locale, 'Faded linen shirt', 'قميص كتان مغسول')).toBe('قميص كتان مغسول');
  });

  it('falls back to the base text when there is no translation', () => {
    // A product written in one language and not yet translated must still be
    // legible and sellable, so a missing Arabic name shows the English one rather
    // than an empty heading.
    locale.set('ar');
    expect(pickLocalised(locale, 'Faded linen shirt', null)).toBe('Faded linen shirt');
  });

  it('falls back when the translation is an empty string', () => {
    // The API stores blank translations as null, but a client must not render an
    // empty title if one ever slips through.
    locale.set('ar');
    expect(pickLocalised(locale, 'Faded linen shirt', '')).toBe('Faded linen shirt');
  });

  it('follows the locale when it changes, without new data', () => {
    // Both languages travel in every payload, so switching re-renders from data
    // already in memory rather than refetching.
    locale.set('en');
    expect(pickLocalised(locale, 'Wool overcoat', 'معطف صوف')).toBe('Wool overcoat');

    locale.set('ar');
    expect(pickLocalised(locale, 'Wool overcoat', 'معطف صوف')).toBe('معطف صوف');
  });
});
