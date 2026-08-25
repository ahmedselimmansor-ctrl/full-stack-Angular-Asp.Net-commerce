import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BasketStore } from '../../core/basket-store';
import type { BasketLine } from '../../core/models';
import { MoneyPipe } from '../../shared/money.pipe';
import { ProductImage } from '../../shared/product-image';

@Component({
  selector: 'app-basket',
  imports: [RouterLink, MoneyPipe, ProductImage],
  template: `
    <div class="sama-page">
      <h1>Your basket</h1>

      @if (store.basket() === null) {
        <p class="sama-muted" aria-busy="true">Loading…</p>
      } @else if (store.isEmpty()) {
        <div class="sama-card empty">
          <h2>Nothing here yet</h2>
          <p class="sama-muted">Browse the shop and add something you like.</p>
          <a class="sama-button" routerLink="/">Start shopping</a>
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
                  <p class="line__sku sama-muted">SKU {{ line.sku }}</p>
                  <p class="line__unit sama-muted">{{ line.unitPrice | money: line.currency }} each</p>
                </div>

                <label class="line__qty sama-field">
                  <span>Qty</span>
                  <select [value]="line.quantity" [disabled]="store.isBusy()"
                          (change)="changeQuantity(line, $event)">
                    @for (n of quantities; track n) { <option [value]="n">{{ n }}</option> }
                  </select>
                </label>

                <p class="line__total">{{ line.lineTotal | money: line.currency }}</p>

                <button type="button" class="remove" [disabled]="store.isBusy()"
                        (click)="remove(line)" [attr.aria-label]="'Remove ' + line.name">×</button>
              </li>
            }
          </ul>

          <aside class="summary sama-card">
            <h2>Summary</h2>
            <div class="summary__row">
              <span>Items</span><span>{{ store.itemCount() }}</span>
            </div>
            <div class="summary__row summary__row--total">
              <span>Subtotal</span><span>{{ store.subtotal() | money: store.currency() }}</span>
            </div>
            <p class="sama-muted note">Shipping and tax are calculated at checkout.</p>
            <a class="sama-button full" routerLink="/checkout">Proceed to checkout</a>
            <button type="button" class="sama-button sama-button--ghost full"
                    [disabled]="store.isBusy()" (click)="store.clear()">Empty basket</button>
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
      background: var(--sama-surface);
      border: 1px solid var(--sama-border);
      border-radius: var(--sama-radius);
      padding: 0.85rem;
    }

    .line__media { width: 72px; height: 72px; border-radius: var(--sama-radius-sm); overflow: hidden; }

    .line__info p { margin: 0; }
    .line__name { font-weight: 600; }
    .line__sku, .line__unit { font-size: 0.8rem; }
    .line__qty { margin-bottom: 0; }
    .line__total { margin: 0; font-weight: 600; text-align: right; }

    .remove { background: none; border: 0; cursor: pointer; font-size: 1.3rem; color: var(--sama-ink-soft); }
    .remove:hover:not(:disabled) { color: var(--sama-danger); }

    .summary { position: sticky; top: 5rem; }
    .summary__row { display: flex; justify-content: space-between; padding: 0.4rem 0; font-size: 0.95rem; }
    .summary__row--total { font-weight: 650; font-size: 1.05rem; border-top: 1px solid var(--sama-border); margin-top: 0.4rem; padding-top: 0.7rem; }
    .note { font-size: 0.8rem; margin: 0.5rem 0 1rem; }
    .full { width: 100%; margin-bottom: 0.5rem; text-decoration: none; }

    .empty { text-align: center; padding: 3rem; }
    .empty .sama-button { margin-top: 1rem; }

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
