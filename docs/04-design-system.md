# The Sama design system

Sama sells clothing, shoes and electronics to customers in Cairo, Dubai, Riyadh
and Amman. Half of them read Arabic. That single fact decides most of what
follows: bilingualism is not a feature bolted onto a finished English shop, it is
the constraint the interface is built around.

## Contents

- [Direction](#direction)
- [Colour](#colour)
- [Type](#type)
- [Theming](#theming)
- [Localisation and direction](#localisation-and-direction)
- [The pitfall that cost the most](#the-pitfall-that-cost-the-most)
- [Layout and components](#layout-and-components)

## Direction

*Sama* (سما) means sky. The palette is taken from a city at dusk: a deep
indigo-teal ground, the turquoise of Fatimid and Iznik ceramic glaze, and the
amber of sodium street lighting.

The one loud gesture is the **dual-script masthead**. Both scripts are always
present; the active language is the wordmark and the other sits behind it,
ghosted. Switching language trades their places and mirrors the whole page.

```mermaid
flowchart LR
    subgraph EN["English — dir=ltr"]
        E1["<b>Sama</b><br/><i>Archivo, 800, expanded</i>"] --- E2["سما<br/><i>Reem Kufi, ghosted</i>"]
    end

    subgraph AR["العربية — dir=rtl"]
        A1["<b>سما</b><br/><i>Reem Kufi, display size</i>"] --- A2["Sama<br/><i>Archivo, ghosted</i>"]
    end

    EN -->|"switch language"| AR
    AR -->|"switch language"| EN
```

Everything around it stays quiet. The departments are the hero of the home page,
not a slogan: the page's single job is to get someone into one of three rooms.

## Colour

The brand field is constant across both themes, so the identity does not invert
when someone flips the lights. Only the chrome adapts.

| Token | Light | Dark | Used for |
| --- | --- | --- | --- |
| `--sama-field` | `#0B1D28` | `#0B1D28` | Hero and footer ground, both themes |
| `--accent` | `#0E8A80` | `#2FD4C6` | Primary actions, active state, chart bars |
| `--warm` | `#B8761A` | `#E9A13B` | Caution, sale, the one warm note |
| `--bg` | `#EBEEEC` | `#08131A` | Page ground — pale plaster, not cream |
| `--surface` | `#F7F9F7` | `#0F212B` | Cards, tables, menus |
| `--text` | `#0E1A1F` | `#E6EEF0` | Body copy |

Semantic pairs (`--positive`, `--caution`, `--critical`) each carry a matching
`-wash` for chip backgrounds, so a status never relies on hue alone at small
sizes.

## Type

Three faces, each with a job:

| Role | Face | Why |
| --- | --- | --- |
| Latin display | **Archivo** (variable, to 125% width) | Wide and editorial without reaching for the serif every other shop uses |
| Arabic display | **Reem Kufi** | Geometric Kufic, from the same tradition as the glaze colour |
| Body, both scripts | **IBM Plex Sans Arabic** | One family covering Latin *and* Arabic, so line height and colour stay even when the script changes mid-page |
| Figures, codes | **IBM Plex Mono** | SKUs and order numbers, where character shapes must not be ambiguous |

Latin display type carries tight negative letter-spacing. Arabic display type
carries none — Arabic is a joined script and negative tracking damages it.

## Theming

Three states, not two. "Match system" is a real position, because someone whose
laptop switches at sunset expects the shop to follow.

```mermaid
stateDiagram-v2
    [*] --> System

    System --> Light: choose Light
    System --> Dark: choose Dark
    Light --> Dark: choose Dark
    Dark --> Light: choose Light
    Light --> System: choose Match system
    Dark --> System: choose Match system

    note right of System
        No data-theme attribute is stamped.
        prefers-color-scheme stays in charge,
        so the OS can still switch it later.
    end note

    note right of Dark
        data-theme="dark" on the root element
        wins over the media query in both
        directions.
    end note
```

The choice is applied twice: once by an inline script in `index.html` before
first paint, so a dark-mode visitor never sees a white flash while Angular boots,
and again by `ThemeService` once it loads. That inline script is the reason the
production CSP pins a SHA-256 hash instead of allowing `unsafe-inline`;
`scripts/check-csp-hash.sh` fails the build if the two drift apart.

## Localisation and direction

Translation happens at runtime, not build time, so the language can be switched
without reloading and without shipping two bundles.

```mermaid
flowchart TD
    Start["Visitor arrives"] --> Stored{"Saved choice in<br/>localStorage?"}
    Stored -->|yes| Use["Use it"]
    Stored -->|no| Browser{"navigator.languages<br/>contains ar?"}
    Browser -->|yes| Arabic["ar"]
    Browser -->|no| English["en"]

    Use --> Apply
    Arabic --> Apply
    English --> Apply

    Apply["Set lang and dir on &lt;html&gt;"] --> Layout["Logical properties mirror<br/>the whole layout"]
    Apply --> Fonts["Display face swaps<br/>via :host-context"]
    Apply --> Numbers["Intl formats money and dates<br/>with ar-EG-u-nu-latn"]
```

Three decisions worth stating:

**English is the source of truth for the key set.** `TranslationKey` is derived
from the English dictionary, so a missing Arabic string is a compile error rather
than a raw key leaking onto a page. Tests additionally assert that no Arabic
entry is still holding its English source string.

**Western digits in both languages.** `ar-EG` defaults to Arabic-Indic numerals,
which would put ١٢٣ beside a price while the rest of the shop shows 123. The
locale tag forces `-u-nu-latn`; a test fails if any Arabic copy reintroduces
`٠-٩`.

**Logical properties everywhere.** `padding-inline`, `margin-inline-start`,
`border-inline-start`, `inset-inline` — never `left` or `right`. This is what
makes RTL a one-attribute change rather than a parallel stylesheet.

## The pitfall that cost the most

Angular's emulated view encapsulation rewrites every selector in a component's
styles to carry that component's content attribute:

```scss
/* What you write, inside a component's styles */
[dir='rtl'] .dept__title { font-family: var(--sama-display-ar); }

/* What Angular emits */
[dir='rtl'][_ngcontent-abc] .dept__title[_ngcontent-abc] { ... }
```

`dir` lives on `<html>`, which never carries a component's content attribute, so
**the rule silently never matches**. Nothing throws and nothing logs.

The visible result was Arabic department headings rendering in Archivo — a
Latin-only face with no Arabic coverage — carrying the negative letter-spacing
meant for Latin display type, which visibly damages joined Arabic script. It was
exactly the failure the whole direction was meant to avoid, and it was invisible
until someone actually looked at the page in Arabic.

The fix is `:host-context()`, which is exempt from the rewrite:

```scss
:host-context([dir='rtl']) .dept__title { font-family: var(--sama-display-ar); }
```

Nineteen selectors were affected. `rtl-encapsulation.spec.ts` now reads every
component stylesheet and fails on a bare `[dir='rtl']`, so it cannot come back.
Global `styles.scss` is unencapsulated and is exempt.

A related trap: `backdrop-filter` creates a stacking context. The header bar uses
it, which trapped the account menu's `z-index` inside the bar and let the dismiss
scrim swallow clicks on the menu — including Sign out. The bar now carries its
own `position` and `z-index` to sit above the scrim.

## Layout and components

Shared primitives keep screens from drifting apart:

| Component | Job |
| --- | --- |
| `app-site-header` | Promise bar, department nav, search, theme and language toggles, account menu, mobile drawer |
| `app-site-footer` | The same brand field as the hero, bracketing the page |
| `app-product-card` | One product tile, with Add always visible rather than on hover — a hover-only control is unreachable on a touchscreen |
| `app-status-chip` | Order, payment, product, stock and user status; resolves its own translation key so a status reads in Arabic on an Arabic page |
| `app-empty-state` | What a list shows when it has nothing in it, with an SVG icon rather than a typographic glyph that renders as tofu where the font lacks it |
| `app-trend-chart` | Daily revenue as inline SVG, with the figures repeated as a screen-reader table because a chart is a picture of numbers |
| `app-breakdown-bar` | Proportions as one stacked bar; lengths are easier to compare than angles, and five order statuses become unreadable slivers in a doughnut |

The quality floor, applied everywhere rather than announced: responsive to 320px,
visible keyboard focus, `prefers-reduced-motion` respected, and every interactive
control reachable without a pointer.
