import { TestBed } from '@angular/core/testing';
import { describe, expect, it, beforeEach, afterEach } from 'vitest';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    document.documentElement.removeAttribute('data-theme');
  });

  it('starts on system and stamps no attribute', () => {
    // Leaving the attribute off is what keeps the prefers-color-scheme media query
    // in charge, so a laptop that switches at sunset takes the shop with it.
    const theme = TestBed.inject(ThemeService);
    TestBed.tick();

    expect(theme.preference()).toBe('system');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('stamps an explicit choice so it wins in both directions', () => {
    const theme = TestBed.inject(ThemeService);

    theme.set('dark');
    TestBed.tick();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');

    theme.set('light');
    TestBed.tick();
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('returns to system by clearing the attribute again', () => {
    const theme = TestBed.inject(ThemeService);

    theme.set('dark');
    TestBed.tick();
    theme.set('system');
    TestBed.tick();

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('cycles light, dark, system', () => {
    const theme = TestBed.inject(ThemeService);
    theme.set('light');

    theme.cycle();
    expect(theme.preference()).toBe('dark');

    theme.cycle();
    expect(theme.preference()).toBe('system');

    theme.cycle();
    expect(theme.preference()).toBe('light');
  });

  it('persists across instances', () => {
    TestBed.inject(ThemeService).set('dark');
    TestBed.tick();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    expect(TestBed.inject(ThemeService).preference()).toBe('dark');
  });
});
