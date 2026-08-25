import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { BasketStore } from '../../core/basket-store';
import type { ProductDetail } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { MoneyPipe } from '../../shared/money.pipe';
import { ProductImage } from '../../shared/product-image';

@Component({
  selector: 'app-product-detail',
  imports: [RouterLink, MoneyPipe, ProductImage],
  template: `
    <div class="sama-page">
      @if (loading()) {
        <p class="sama-muted" aria-busy="true">Loading…</p>
      } @else if (product(); as item) {
        <nav class="crumbs" aria-label="Breadcrumb">
          <a routerLink="/">Shop</a> <span aria-hidden="true">/</span> <span>{{ item.name }}</span>
        </nav>

        <div class="detail">
          <div class="detail__media">
            <div class="detail__frame">
              <app-product-image
                [source]="item.images.length > 0 ? item.images[selected()].url : null"
                [alt]="item.images.length > 0 ? (item.images[selected()].altText ?? item.name) : item.name"
                [eager]="true" />
            </div>

            @if (item.images.length > 1) {
              <div class="thumbs" role="group" aria-label="Product images">
                @for (image of item.images; track image.url; let i = $index) {
                  <button type="button" class="thumb" [class.is-selected]="i === selected()"
                          (click)="selected.set(i)" [attr.aria-label]="'View image ' + (i + 1)">
                    <app-product-image [source]="image.url" [alt]="image.altText ?? ''" />
                  </button>
                }
              </div>
            }
          </div>

          <div class="detail__info">
            @if (item.brand) { <p class="brand">{{ item.brand }}</p> }
            <h1>{{ item.name }}</h1>
            <p class="price">{{ item.price | money: item.currency }}</p>
            <p class="sku sama-muted">SKU {{ item.sku }}</p>
            <p class="description">{{ item.description }}</p>

            <div class="buy">
              <label class="sama-field qty">
                <span>Quantity</span>
                <select [value]="quantity()" (change)="onQuantityChange($event)">
                  @for (n of quantities; track n) { <option [value]="n">{{ n }}</option> }
                </select>
              </label>

              <button type="button" class="sama-button" [disabled]="adding()" (click)="addToBasket(item)">
                {{ adding() ? 'Adding…' : 'Add to basket' }}
              </button>
            </div>
          </div>
        </div>
      } @else {
        <div class="sama-card missing">
          <h1>Product not found</h1>
          <p class="sama-muted">It may have been withdrawn from sale.</p>
          <a class="sama-button" routerLink="/">Back to the shop</a>
        </div>
      }
    </div>
  `,
  styles: `
    .crumbs { font-size: 0.85rem; color: var(--sama-ink-soft); margin-bottom: 1.25rem; display: flex; gap: 0.4rem; }
    .crumbs a { text-decoration: none; }
    .crumbs a:hover { text-decoration: underline; }

    .detail { display: grid; grid-template-columns: 1.05fr 1fr; gap: 2.5rem; align-items: start; }

    .detail__frame {
      aspect-ratio: 4 / 3; overflow: hidden;
      border-radius: var(--sama-radius); border: 1px solid var(--sama-border);
      background: var(--sama-surface-alt);
    }

    .thumbs { display: flex; gap: 0.5rem; margin-top: 0.75rem; }
    .thumb {
      width: 64px; height: 64px; padding: 0; overflow: hidden; cursor: pointer;
      border: 1px solid var(--sama-border); border-radius: var(--sama-radius-sm); background: none;
    }
    .thumb.is-selected { border-color: var(--sama-accent); border-width: 2px; }
    .thumb img { width: 100%; height: 100%; object-fit: cover; display: block; }

    .brand { margin: 0 0 0.25rem; font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.07em; color: var(--sama-ink-soft); }
    .price { font-size: 1.5rem; font-weight: 650; margin: 0.5rem 0 0.15rem; }
    .sku { font-size: 0.8rem; margin: 0 0 1.25rem; }
    .description { margin: 0 0 1.75rem; }

    .buy { display: flex; align-items: flex-end; gap: 1rem; }
    .qty { margin-bottom: 0; width: 100px; }

    .missing { text-align: center; padding: 3rem; }
    .missing .sama-button { margin-top: 1rem; }

    @media (max-width: 820px) { .detail { grid-template-columns: 1fr; gap: 1.5rem; } }
  `,
})
export class ProductDetailPage {
  private readonly catalog = inject(CatalogService);
  private readonly basket = inject(BasketStore);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  /** Bound from the route by withComponentInputBinding(). */
  readonly slug = input.required<string>();

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly selected = signal(0);
  protected readonly adding = signal(false);
  protected readonly quantity = signal(1);
  protected readonly quantities = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

  constructor() {
    queueMicrotask(() => void this.load());
  }

  protected onQuantityChange(event: Event): void {
    this.quantity.set(Number((event.target as HTMLSelectElement).value));
  }

  protected async addToBasket(item: ProductDetail): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.notifications.info('Sign in to start a basket.');
      return;
    }

    this.adding.set(true);
    try {
      await this.basket.add(item.id, this.quantity());
      this.notifications.success(`${item.name} added to your basket.`);
    } catch {
      // Already reported by the error interceptor.
    } finally {
      this.adding.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.product.set(await this.catalog.bySlug(this.slug()));
    } catch {
      this.product.set(null);
    } finally {
      this.loading.set(false);
    }
  }
}
