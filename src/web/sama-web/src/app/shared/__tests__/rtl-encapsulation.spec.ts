import { describe, expect, it } from 'vitest';

/**
 * Angular's emulated encapsulation rewrites every selector in a component's styles
 * to carry that component's content attribute. `[dir='rtl'] .thing` becomes
 * `[dir='rtl'][_ngcontent-x] .thing[_ngcontent-x]`, and since `dir` lives on
 * <html> — which never carries a component attribute — the rule silently never
 * matches.
 *
 * This shipped once: Arabic department headings rendered in Archivo, a Latin-only
 * face, carrying the negative letter-spacing meant for Latin display type, which
 * visibly damages joined Arabic script. Nothing threw and nothing logged. The fix
 * is `:host-context([dir='rtl'])`, and this test is what stops it coming back.
 *
 * The sources are pulled in as raw strings through Vite's glob import, because the
 * unit tests build for the browser and have no filesystem to read.
 */
// `import.meta.glob` is a Vite build-time transform. TypeScript only knows about
// it when vite/client types are loaded, which this project's spec tsconfig does
// not do, so the call is reached through a cast rather than by widening the whole
// project's ambient types for one test.
const sources = (import.meta as unknown as {
  glob: (pattern: string, options: Record<string, unknown>) => Record<string, string>;
}).glob('../../**/*.{scss,ts}', { query: '?raw', import: 'default', eager: true });

const BARE_RTL = /\[dir=['"]rtl['"]\]/;
const WRAPPED = /:host-context\(\s*\[dir=['"]rtl['"]\]\s*\)/;

describe('RTL overrides inside component styles', () => {
  it('reads enough sources to be meaningful', () => {
    // A glob that silently matches nothing would make this suite pass forever.
    expect(Object.keys(sources).length).toBeGreaterThan(20);
  });

  it('use :host-context so they can match the dir attribute on <html>', () => {
    const offenders: string[] = [];

    for (const [path, source] of Object.entries(sources)) {
      source.split('\n').forEach((line: string, index: number) => {
        if (BARE_RTL.test(line) && !WRAPPED.test(line)) {
          offenders.push(`${path}:${index + 1}  ${line.trim()}`);
        }
      });
    }

    expect(offenders).toEqual([]);
  });
});
