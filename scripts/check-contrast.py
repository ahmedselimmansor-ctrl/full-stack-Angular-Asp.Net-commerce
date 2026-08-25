#!/usr/bin/env python3
"""
Checks the design tokens against WCAG 2.1 AA contrast minima.

Colour is the one part of a design system that can be verified rather than
argued about, and it is also the part that quietly fails: a muted grey that
looks fine on the designer's monitor can sit at 2.6:1 and be unreadable to
someone with low vision, in sunlight, or on a cheap panel.

Only pairings the interface actually renders are tested. Checking every colour
against every other would bury the real failures under combinations that never
appear on screen.

Minima applied:
  4.5:1  normal-size text (WCAG 1.4.3)
  3.0:1  UI component boundaries and graphical objects (WCAG 1.4.11)

Run with --verbose to print passing pairings too.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
STYLES = ROOT / "src/web/sama-web/src/styles.scss"


def luminance(hex_colour: str) -> float:
    def channel(value: int) -> float:
        c = value / 255
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = (int(hex_colour[i:i + 2], 16) for i in (1, 3, 5))
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)


def contrast(a: str, b: str) -> float:
    la, lb = luminance(a), luminance(b)
    lighter, darker = max(la, lb), min(la, lb)
    return (lighter + 0.05) / (darker + 0.05)


def parse_blocks(css: str) -> tuple[dict, dict, dict]:
    """Brand constants, the light palette, and the explicit dark override."""
    declaration = re.compile(r"(--[\w-]+):\s*(#[0-9A-Fa-f]{6}|var\(--[\w-]+\))\s*;")

    light: dict[str, str] = {}
    for match in re.finditer(r":root\s*\{(.*?)\n\}", css, re.S):
        light.update(dict(declaration.findall(match.group(1))))

    dark: dict[str, str] = {}
    match = re.search(r":root\[data-theme='dark'\]\s*\{(.*?)\n\}", css, re.S)
    if match:
        dark = dict(declaration.findall(match.group(1)))

    return light, dark, light


def resolve(name: str, theme: dict, base: dict) -> str | None:
    """Follows var() indirection to a literal colour."""
    seen: set[str] = set()

    while True:
        value = theme.get(name) or base.get(name)
        if value is None or name in seen:
            return None
        if value.startswith("#"):
            return value

        seen.add(name)
        name = re.match(r"var\((--[\w-]+)\)", value).group(1)


TEXT, UI = 4.5, 3.0

# foreground, background, minimum, what renders this way
PAIRINGS = [
    ("--text", "--bg", TEXT, "body text on the page"),
    ("--text", "--surface", TEXT, "body text on a card"),
    ("--text", "--surface-2", TEXT, "body text on a raised surface"),
    ("--text-soft", "--bg", TEXT, "secondary text on the page"),
    ("--text-soft", "--surface", TEXT, "secondary text on a card"),
    ("--text-soft", "--surface-2", TEXT, "secondary text on a raised surface"),
    ("--text-faint", "--bg", TEXT, "eyebrows and hints on the page"),
    ("--text-faint", "--surface", TEXT, "eyebrows and hints on a card"),
    ("--text-faint", "--surface-2", TEXT, "toggle labels on a raised surface"),
    ("--accent", "--bg", UI, "accent rule and focus ring on the page"),
    ("--accent", "--surface", TEXT, "accent link text on a card"),
    ("--accent", "--accent-wash", TEXT, "accent chip text on its wash"),
    ("--accent-ink", "--accent", TEXT, "button label on the accent"),
    ("--positive", "--positive-wash", TEXT, "positive chip"),
    ("--positive", "--surface", TEXT, "positive text on a card"),
    ("--caution", "--caution-wash", TEXT, "caution chip"),
    ("--critical", "--critical-wash", TEXT, "critical chip"),
    ("--critical", "--surface", TEXT, "error text on a card"),
    ("--line-strong", "--bg", UI, "control border on the page"),
    ("--line-strong", "--surface", UI, "control border on a card"),
    ("--line-strong", "--surface-2", UI, "control border on a raised surface"),
    ("--on-field", "--sama-field", TEXT, "text on the dusk brand field"),
    ("--on-field-soft", "--sama-field", TEXT, "muted text on the dusk field"),
    ("--sama-glaze-bright", "--sama-field", UI, "glaze accent on the dusk field"),
]


def main() -> int:
    verbose = "--verbose" in sys.argv
    css = STYLES.read_text()
    light, dark, base = parse_blocks(css)

    failures: list[str] = []

    for theme_name, theme in (("light", light), ("dark", dark)):
        if verbose:
            print(f"\n=== {theme_name} ===")

        for foreground, background, minimum, label in PAIRINGS:
            fg = resolve(foreground, theme, base)
            bg = resolve(background, theme, base)

            if fg is None or bg is None:
                failures.append(f"{theme_name}: could not resolve {foreground} or {background}")
                continue

            ratio = contrast(fg, bg)

            if ratio < minimum:
                failures.append(
                    f"{theme_name}: {label} is {ratio:.2f}:1, needs {minimum}:1  [{fg} on {bg}]"
                )
            elif verbose:
                print(f"  PASS {ratio:5.2f} (min {minimum})  {label}")

    if failures:
        print("\nContrast failures:\n")
        for failure in failures:
            print(f"  {failure}")
        print(f"\n{len(failures)} failing pairing(s).")
        return 1

    print(f"All {len(PAIRINGS) * 2} pairings meet WCAG AA.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
