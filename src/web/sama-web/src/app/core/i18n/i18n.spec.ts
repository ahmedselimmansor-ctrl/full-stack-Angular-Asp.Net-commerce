import { TestBed } from '@angular/core/testing';
import { describe, expect, it, beforeEach } from 'vitest';
import { LocaleService } from './locale.service';
import { en } from './en';
import { ar } from './ar';

describe('translations', () => {
  it('covers every English key in Arabic', () => {
    // Typing catches this at build time, but only for keys that exist in both
    // objects. This also catches an Arabic entry left holding the English string.
    const missing = (Object.keys(en) as Array<keyof typeof en>).filter((key) => !ar[key]);
    expect(missing).toEqual([]);
  });

  it('has no Arabic entry left as its English source string', () => {
    const untranslated = (Object.keys(en) as Array<keyof typeof en>).filter(
      (key) =>
        ar[key] === en[key] &&
        // These are deliberately identical: a brand name, a language endonym, and
        // city names that are already written the same way in the copy.
        !['locale.english', 'locale.arabic'].includes(key),
    );

    expect(untranslated).toEqual([]);
  });

  it('does not mix Arabic-Indic and Western digits', () => {
    // Prices format with Western digits, so copy using ٠-٩ would put two numbering
    // systems on the same page.
    const arabicIndic = Object.entries(ar).filter(([, value]) => /[٠-٩]/.test(value));
    expect(arabicIndic).toEqual([]);
  });
});

describe('LocaleService', () => {
  let locale: LocaleService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    locale = TestBed.inject(LocaleService);
    locale.set('en');
  });

  it('fills named placeholders', () => {
    expect(locale.t('home.departments.browse', { department: 'Shoes' })).toBe('Browse Shoes');
  });

  it('leaves an unsupplied placeholder visible rather than blanking it', () => {
    // A silently emptied slot reads as finished copy; "{department}" is reportable.
    expect(locale.t('home.departments.browse')).toContain('{department}');
  });

  it('sets the document language and direction', () => {
    locale.set('ar');
    TestBed.tick();
    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');

    locale.set('en');
    TestBed.tick();
    expect(document.documentElement.dir).toBe('ltr');
  });

  it('translates through the active locale', () => {
    locale.set('ar');
    expect(locale.t('nav.clothing')).toBe('ملابس');
  });

  it('formats money in Western digits for Arabic', () => {
    locale.set('ar');
    const formatted = locale.formatMoney(1450, 'EGP');

    expect(formatted).toMatch(/1[,.]450/);
    expect(formatted).not.toMatch(/[٠-٩]/);
  });

  it('respects each currency’s minor units', () => {
    // JOD has three decimal places; hard-coding two would misprice a whole market.
    expect(locale.formatMoney(12.5, 'JOD')).toMatch(/12\.500/);
    expect(locale.formatMoney(12.5, 'EGP')).toMatch(/12\.50/);
  });

  it('persists the choice across service instances', () => {
    locale.set('ar');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    expect(TestBed.inject(LocaleService).locale()).toBe('ar');
  });
});
