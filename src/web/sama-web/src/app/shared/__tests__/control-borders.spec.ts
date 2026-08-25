import { describe, expect, it } from 'vitest';

/**
 * WCAG 1.4.11 requires the boundary of an interactive control to reach 3:1
 * against its background. The design system carries two border weights for
 * exactly this reason: `--line` is decorative — card edges, table wrappers,
 * dividers — and sits well below 3:1 on purpose, because drawing every card at
 * 3:1 would box the whole interface in heavy rules. `--line-strong` is the
 * control weight and is verified by `scripts/check-contrast.py`.
 *
 * The token being correct is only half of it: a control that reaches for the
 * decorative weight is still unreadable. This caught the header search field,
 * the language and theme toggles, the sort and role selects, the product-image
 * thumbnails, and two admin inputs — all drawing their boundary at about 1.3:1.
 */
const sources = (import.meta as unknown as {
  glob: (pattern: string, options: Record<string, unknown>) => Record<string, string>;
}).glob('../../**/*.{scss,ts}', { query: '?raw', import: 'default', eager: true });

/** Selectors that name an interactive control rather than a surface. */
const CONTROL = /(^|[\s,>])(input|select|textarea|button)\b|\.(search__input|lang|seg|thumb|toolbar__control|grant)\b/;

/** A border drawn with the decorative weight. */
const DECORATIVE_BORDER = /border(?:-color)?:\s*(?:[\d.]+px\s+\w+\s+)?var\(--line\)\s*;/;

describe('control borders', () => {
  it('reads enough sources to be meaningful', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(20);
  });

  it('use the control weight, not the decorative one', () => {
    const offenders: string[] = [];

    for (const [path, source] of Object.entries(sources)) {
      const lines = source.split('\n');
      let selector = '';

      lines.forEach((line: string, index: number) => {
        // Remember the most recent selector so a declaration can be attributed.
        if (line.includes('{')) selector = line.slice(0, line.indexOf('{')).trim();

        if (DECORATIVE_BORDER.test(line) && CONTROL.test(selector)) {
          offenders.push(`${path}:${index + 1}  ${selector} { ${line.trim()} }`);
        }
      });
    }

    expect(offenders).toEqual([]);
  });
});
