import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BasketStore } from '../../core/basket-store';
import type { BasketLine } from '../../core/models';
import { LocaleService, MoneyPipe, TranslatePipe } from '../../core/i18n';
import { ProductImage } from '../../shared/product-image';

@Component({
  selector: 'app-basket',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MoneyPipe, TranslatePipe, ProductImage],
  template: `
    <div class="page">
      <h1>{{ 'basket.title' | t }}</h1>

      @if (store.basket() === null) {
        <p class="muted" aria-busy="true">{{ 'common.loading' | t }}</p>
      } @else if (store.isEmpty()) {
        <div class="card empty">
          <h2>{{ 'basket.empty.title' | t }}</h2>
          <p class="muted">{{ 'basket.empty.copy' | t }}</p>
          <a class="btn" routerLink="/shop">{{ 'basket.empty.action' | t }}</a>
        </div>
      } @else {
        <div class="layout">
          <ul class="lines">
            @for (line of store.basket()!.items; track line.productId) {
              <li class="line">
                <div class="line__media">
                  <app-product-image [source]="line.imageUrl" [alt]="line.name" />
                </div>

                <div class="line__info">
                  <p class="line__name">{{ line.name }}</p>
                  <p class="line__sku faint">{{ 'product.sku' | t }} <span class="mono">{{ line.sku }}</span></p>
                  <p class="line__unit muted">{{ locale.t('basket.each', { price: locale.formatMoney(line.unitPrice, line.currency) }) }}</p>
                </div>

                <label class="line__qty field">
                  <span>{{ 'basket.qty' | t }}</span>
                  <select [value]="line.quantity" [disabled]="store.isBusy()"
                          (change)="changeQuantity(line, $event)">
                    @for (n of quantities; track n) { <option [value]="n">{{ locale.formatNumber(n) }}</option> }
                  </select>
                </label>

                <p class="line__total">{{ line.lineTotal | money: line.currency }}</p>

                <button type="button" class="remove" [disabled]="store.isBusy()"
                        (click)="remove(line)" [attr.aria-label]="locale.t('basket.removeNamed', { name: line.name })">×</button>
              </li>
            }
          </ul>

          <aside class="summary card">
            <h2>{{ 'basket.summary' | t }}</h2>
            <div class="summary__row">
              <span>{{ 'basket.itemCount' | t }}</span><span class="tabular">{{ locale.formatNumber(store.itemCount()) }}</span>
            </div>
            <div class="summary__row summary__row--total">
              <span>{{ 'basket.subtotal' | t }}</span><span class="tabular">{{ store.subtotal() | money: store.currency() }}</span>
            </div>
            <p class="muted note">{{ 'basket.shippingNote' | t }}</p>
            <a class="btn full" routerLink="/checkout">{{ 'basket.goToCheckout' | t }}</a>
            <button type="button" class="btn btn--ghost full"
                    [disabled]="store.isBusy()" (click)="store.clear()">{{ 'basket.empty.action2' | t }}</button>
          </aside>
        </div>
      }
    </div>
  `,
  styles: `
    .layout { display: grid; grid-template-columns: 1fr 320px; gap: 2rem; align-items: start; }
    .lines { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.75rem; }

    .line {
      display: grid;
      grid-template-columns: 72px 1fr 90px auto 32px;
      gap: 1rem;
      align-items: center;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--sama-radius);
      padding: 0.85rem;
    }

    .line__media { width: 72px; height: 72px; border-radius: var(--sama-radius-sm); overflow: hidden; }

    .line__info p { margin: 0; }
    .line__name { font-weight: 600; }
    .line__sku, .line__unit { font-size: 0.8rem; }
    .line__qty { margin-bottom: 0; }
    .line__total { margin: 0; font-weight: 600; text-align: right; }

    .remove { background: none; border: 0; cursor: pointer; font-size: 1.3rem; color: var(--text-soft); }
    .remove:hover:not(:disabled) { color: var(--critical); }

    .summary { position: sticky; top: 5rem; }
    .summary__row { display: flex; justify-content: space-between; padding: 0.4rem 0; font-size: 0.95rem; }
    .summary__row--total { font-weight: 650; font-size: 1.05rem; border-top: 1px solid var(--line); margin-top: 0.4rem; padding-top: 0.7rem; }
    .note { font-size: 0.8rem; margin: 0.5rem 0 1rem; }
    .full { width: 100%; margin-bottom: 0.5rem; text-decoration: none; }

    .empty { text-align: center; padding: 3rem; }
    .empty .btn { margin-top: 1rem; }

    @media (max-width: 860px) {
      .layout { grid-template-columns: 1fr; }
      .line { grid-template-columns: 56px 1fr auto; grid-template-areas: 'media info remove' 'media qty total'; }
      .line__media { grid-area: media; }
      .line__info { grid-area: info; }
      .line__qty { grid-area: qty; }
      .line__total { grid-area: total; }
      .remove { grid-area: remove; }
      .line__media { width: 56px; height: 56px; }
    }
  `,
})
export class BasketPage {
  protected readonly locale = inject(LocaleService);
  protected readonly store = inject(BasketStore);
  protected readonly quantities = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20];

  constructor() {
    void this.store.refresh();
  }

  protected async changeQuantity(line: BasketLine, event: Event): Promise<void> {
    const quantity = Number((event.target as HTMLSelectElement).value);
    await this.store.setQuantity(line.productId, quantity);
  }

  protected async remove(line: BasketLine): Promise<void> {
    await this.store.remove(line.productId);
  }
}
