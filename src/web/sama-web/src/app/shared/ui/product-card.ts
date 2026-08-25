import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProductImage } from '../product-image';
import { LocaleService, LocalisedPipe, MoneyPipe, TranslatePipe, pickLocalised } from '../../core/i18n';
import type { ProductSummary } from '../../core/models';

/**
 * One product tile, used by the home rails, the department pages and search.
 *
 * "Add" lives on the card so the common case — you already know you want it —
 * does not cost a page load. The button stays visible rather than appearing on
 * hover, because a hover-only control is unreachable on a touchscreen.
 */
@Component({
  selector: 'app-product-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ProductImage, MoneyPipe, TranslatePipe, LocalisedPipe],
  template: `
    <article class="tile">
      <a class="tile__media" [routerLink]="['/products', product().slug]" tabindex="-1" aria-hidden="true">
        <app-product-image
          [source]="product().primaryImageUrl"
          [alt]="displayName()"
          [eager]="eager()"
        />
      </a>

      <div class="tile__body">
        @if (product().brand) {
          <p class="tile__brand">{{ product().brand }}</p>
        }

        <h3 class="tile__name">
          <a [routerLink]="['/products', product().slug]">
            {{ product().name | localised: product().nameAr }}
          </a>
        </h3>

        <p class="tile__price">{{ product().price | money: product().currency }}</p>
      </div>

      <button
        type="button"
        class="btn btn--sm tile__add"
        [disabled]="busy()"
        (click)="add.emit(product())"
      >
        {{ (busy() ? 'product.adding' : 'product.addToBasket') | t }}
      </button>
    </article>
  `,
  styleUrl: './product-card.scss',
})
export class ProductCard {
  private readonly locale = inject(LocaleService);

  readonly product = input.required<ProductSummary>();
  readonly busy = input(false);
  readonly eager = input(false);

  readonly add = output<ProductSummary>();

  /** Alt text has to read in the page's language too, not just the visible title. */
  protected readonly displayName = computed(() =>
    pickLocalised(this.locale, this.product().name, this.product().nameAr),
  );
}
