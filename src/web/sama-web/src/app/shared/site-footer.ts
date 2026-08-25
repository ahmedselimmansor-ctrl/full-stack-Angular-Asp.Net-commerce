import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleService, TranslatePipe } from '../core/i18n';
import { ThemeToggle } from './theme-toggle';
import { LocaleToggle } from './locale-toggle';

@Component({
  selector: 'app-site-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe, ThemeToggle, LocaleToggle],
  template: `
    <footer class="foot">
      <div class="foot__inner page">
        <div class="foot__brand">
          <!-- The mark closes the page the way the header opened it: both
               scripts, the active one leading. -->
          <p class="foot__mark">
            <span class="foot__mark-primary">{{ 'brand.name' | t }}</span>
            <span class="foot__mark-ghost" aria-hidden="true">{{
              locale.locale() === 'ar' ? 'Sama' : 'سما'
            }}</span>
          </p>
          <p class="foot__tagline">{{ 'footer.tagline' | t }}</p>

          <div class="foot__prefs">
            <app-locale-toggle />
            <app-theme-toggle />
          </div>
        </div>

        <nav class="foot__cols" [attr.aria-label]="'footer.shop' | t">
          @for (column of columns; track column.heading) {
            <div class="foot__col">
              <h2 class="foot__heading">{{ column.heading | t }}</h2>
              <ul class="foot__list">
                @for (link of column.links; track link.label) {
                  <li><a [routerLink]="link.path">{{ link.label | t }}</a></li>
                }
              </ul>
            </div>
          }
        </nav>
      </div>

      <div class="foot__base page">
        <p>{{ 'footer.rights' | t: { year: year } }}</p>
        <p class="foot__origin">{{ 'footer.builtIn' | t }}</p>
      </div>
    </footer>
  `,
  styleUrl: './site-footer.scss',
})
export class SiteFooter {
  protected readonly locale = inject(LocaleService);
  protected readonly year = new Date().getFullYear();

  protected readonly columns = [
    {
      heading: 'footer.shop',
      links: [
        { path: '/clothing', label: 'nav.clothing' },
        { path: '/shoes', label: 'nav.shoes' },
        { path: '/electronics', label: 'nav.electronics' },
        { path: '/shop', label: 'nav.shop' },
      ],
    },
    {
      heading: 'footer.help',
      links: [
        { path: '/help/delivery', label: 'footer.delivery' },
        { path: '/help/sizing', label: 'footer.sizing' },
        { path: '/help/warranty', label: 'footer.warranty' },
        { path: '/help/contact', label: 'footer.contact' },
      ],
    },
    {
      heading: 'footer.company',
      links: [
        { path: '/about', label: 'footer.about' },
        { path: '/about/careers', label: 'footer.careers' },
        { path: '/about/press', label: 'footer.press' },
        { path: '/legal/terms', label: 'footer.terms' },
      ],
    },
  ] as const;
}
