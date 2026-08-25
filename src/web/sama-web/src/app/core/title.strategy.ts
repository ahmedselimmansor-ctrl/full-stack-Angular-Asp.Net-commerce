import { Injectable, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, type ActivatedRouteSnapshot, type RouterStateSnapshot } from '@angular/router';
import { LocaleService, type TranslationKey } from './i18n';

/**
 * Sets the document title from a translation key, so the browser tab reads in the
 * same language as the page.
 *
 * A route's static `title` string cannot do this: it is resolved once, in English,
 * and never revisited when the language changes. Routes carry a `titleKey` in
 * their data instead, and this strategy resolves it.
 *
 * The last snapshot is retained so a language switch can retitle an already-open
 * tab without waiting for the next navigation. It is kept as a field rather than
 * read back off the Router, because injecting the Router here would close a
 * dependency cycle — the Router constructs the TitleStrategy — and Angular
 * refuses to build the injector at all, taking the whole application down.
 */
@Injectable()
export class SamaTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly locale = inject(LocaleService);

  private lastSnapshot: RouterStateSnapshot | null = null;

  constructor() {
    super();

    effect(() => {
      this.locale.locale();
      if (this.lastSnapshot) this.apply(this.lastSnapshot);
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.lastSnapshot = snapshot;
    this.apply(snapshot);
  }

  private apply(snapshot: RouterStateSnapshot): void {
    const brand = this.locale.t('brand.name');
    const { key, literal } = this.deepestTitle(snapshot.root);

    if (key) {
      this.title.setTitle(`${this.locale.t(key)} — ${brand}`);
    } else if (literal) {
      this.title.setTitle(literal);
    } else {
      this.title.setTitle(`${brand} — ${this.locale.t('brand.tagline')}`);
    }
  }

  /** The deepest route that names a title wins, so a child page beats its section. */
  private deepestTitle(root: ActivatedRouteSnapshot): {
    key?: TranslationKey;
    literal?: string;
  } {
    let key: TranslationKey | undefined;
    let literal: string | undefined;

    for (let route: ActivatedRouteSnapshot | null = root; route; route = route.firstChild) {
      if (route.data['titleKey']) key = route.data['titleKey'] as TranslationKey;
      if (route.title) literal = route.title;
    }

    return { key, literal };
  }
}
