import { describe, expect, it } from 'vitest';

/**
 * The layout mirrors for Arabic through CSS logical properties. A physical
 * property in the inline direction — `margin-left`, `border-right`,
 * `text-align: right`, a bare `left:` offset — does not mirror, so it silently
 * pins an element to the wrong edge once `dir` flips.
 *
 * Nothing throws when this happens. It shows up as a badge sitting over its own
 * icon, a toast stack hugging the wrong corner, or a numeric column aligned to
 * the start of the row instead of its end.
 *
 * Block-direction properties (`margin-top`, `padding-bottom`, `border-top`) are
 * unaffected by `dir` and are deliberately not flagged.
 */
const sources = (import.meta as unknown as {
  glob: (pattern: string, options: Record<string, unknown>) => Record<string, string>;
}).glob('../../**/*.{scss,ts}', { query: '?raw', import: 'default', eager: true });

/** Inline-direction properties that will not mirror. */
const PHYSICAL = new RegExp(
  [
    String.raw`\b(?:margin|padding|border)-(?:left|right)\s*:`,
    String.raw`\btext-align\s*:\s*(?:left|right)\b`,
    // A bare offset, e.g. `right: 1rem`. Excludes inset-inline-* and anything
    // hyphenated into another identifier.
    String.raw`(?<![-\w])(?:left|right)\s*:\s*(?:-?[\d.]|auto|calc|var)`,
  ].join('|'),
);

/**
 * Deliberate exceptions, each justified in a comment at the site.
 *
 * The dialog centres itself with `left: 50%` and `translate(-50%, -50%)`. That is
 * symmetric, so it lands in the same place either way; swapping in
 * `inset-inline-start` would pair a logical offset with a physical transform and
 * push the dialog off-centre in RTL.
 */
const ALLOWED = [/confirm-dialog\.ts$/];

describe('inline-direction CSS', () => {
  it('reads enough sources to be meaningful', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(20);
  });

  it('uses logical properties so the layout mirrors for Arabic', () => {
    const offenders: string[] = [];

    for (const [path, source] of Object.entries(sources)) {
      if (ALLOWED.some((allow) => allow.test(path))) continue;

      source.split('\n').forEach((line: string, index: number) => {
        if (PHYSICAL.test(line)) {
          offenders.push(`${path}:${index + 1}  ${line.trim()}`);
        }
      });
    }

    expect(offenders).toEqual([]);
  });
});
