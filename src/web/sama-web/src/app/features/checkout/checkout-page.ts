import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { OrderService } from '../../core/api.service';
import { BasketStore } from '../../core/basket-store';
import { ApiError } from '../../core/interceptors/error.interceptor';
import { LocaleService, MoneyPipe, TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-checkout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, MoneyPipe, TranslatePipe],
  template: `
    <div class="page">
      <h1>{{ 'checkout.title' | t }}</h1>

      @if (store.isEmpty() && store.basket() !== null) {
        <div class="card empty">
          <h2>{{ 'basket.empty.title' | t }}</h2>
          <a class="btn" routerLink="/shop">{{ 'basket.empty.action' | t }}</a>
        </div>
      } @else {
        <div class="layout">
          <form class="card" (submit)="submit($event)" novalidate>
            <h2>{{ 'checkout.address' | t }}</h2>

            <label class="field">
              <span>{{ 'checkout.line1' | t }}</span>
              <input name="line1" [(ngModel)]="line1" required autocomplete="address-line1" [disabled]="busy()" />
              @if (fieldError('ShippingAddress.Line1'); as m) { <span class="error-text">{{ m }}</span> }
            </label>

            <div class="row">
              <label class="field">
                <span>{{ 'checkout.city' | t }}</span>
                <input name="city" [(ngModel)]="city" required autocomplete="address-level2" [disabled]="busy()" />
                @if (fieldError('ShippingAddress.City'); as m) { <span class="error-text">{{ m }}</span> }
              </label>

              <label class="field">
                <span>{{ 'checkout.postcode' | t }}</span>
                <input name="postcode" [(ngModel)]="postcode" required autocomplete="postal-code" [disabled]="busy()" />
                @if (fieldError('ShippingAddress.Postcode'); as m) { <span class="error-text">{{ m }}</span> }
              </label>
            </div>

            <label class="field">
              <span>{{ 'checkout.country' | t }}</span>
              <select name="country" [(ngModel)]="country" [disabled]="busy()">
                @for (option of countries; track option.code) {
                  <option [value]="option.code">{{ option.name }}</option>
                }
              </select>
            </label>

            @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }

            <button type="submit" class="btn full" [disabled]="busy() || store.isEmpty()">
              {{ (busy() ? 'checkout.placing' : 'checkout.placeOrder') | t }}
            </button>

            <p class="muted legal">{{ 'checkout.cardNote' | t }}</p>
          </form>

          <aside class="summary card">
            <h2>{{ 'checkout.summary' | t }}</h2>
            @for (line of store.basket()?.items ?? []; track line.productId) {
              <div class="summary__row">
                <span>{{ locale.formatNumber(line.quantity) }} × {{ line.name }}</span>
                <span>{{ line.lineTotal | money: line.currency }}</span>
              </div>
            }
            <div class="summary__row summary__row--total">
              <span>{{ 'checkout.total' | t }}</span><span class="tabular">{{ store.subtotal() | money: store.currency() }}</span>
            </div>
          </aside>
        </div>
      }
    </div>
  `,
  styles: `
    .layout { display: grid; grid-template-columns: 1fr 320px; gap: 2rem; align-items: start; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
    .full { width: 100%; margin-top: 0.5rem; }
    .legal { font-size: 0.78rem; margin: 0.75rem 0 0; }
    .summary { position: sticky; inset-block-start: 7rem; }
    .summary__row { display: flex; justify-content: space-between; gap: 1rem; padding: 0.35rem 0; font-size: 0.9rem; }
    .summary__row--total { font-weight: 650; font-size: 1.05rem; border-top: 1px solid var(--line); margin-top: 0.4rem; padding-top: 0.7rem; }
    .empty { text-align: center; padding: 3rem; }
    .empty .btn { margin-top: 1rem; }
    @media (max-width: 860px) { .layout { grid-template-columns: 1fr; } .row { grid-template-columns: 1fr; } }
  `,
})
export class CheckoutPage {
  protected readonly locale = inject(LocaleService);
  private readonly orders = inject(OrderService);
  private readonly router = inject(Router);
  protected readonly store = inject(BasketStore);

  protected line1 = '';
  protected city = '';
  protected postcode = '';
  protected country = 'EG';

  protected readonly countries = [
    { code: 'EG', name: 'Egypt' },
    { code: 'AE', name: 'United Arab Emirates' },
    { code: 'SA', name: 'Saudi Arabia' },
    { code: 'JO', name: 'Jordan' },
    { code: 'GB', name: 'United Kingdom' },
    { code: 'US', name: 'United States' },
  ];

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]> | null>(null);

  /**
   * Generated once per checkout attempt and reused for every retry of that attempt.
   *
   * This is what makes a lost response safe. If the network drops after the server has created
   * the order but before the reply arrives, retrying with the same key replays the original
   * outcome rather than placing a second order.
   */
  private idempotencyKey: string | null = null;

  constructor() {
    void this.store.refresh();
  }

  protected fieldError(field: string): string | null {
    return this.fieldErrors()?.[field]?.[0] ?? null;
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(null);

    // Kept across retries; only cleared once an order is actually placed.
    this.idempotencyKey ??= crypto.randomUUID();

    try {
      const accepted = await this.orders.checkout(
        {
          line1: this.line1.trim(),
          city: this.city.trim(),
          postcode: this.postcode.trim(),
          country: this.country,
        },
        this.idempotencyKey,
      );

      this.idempotencyKey = null;

      // The server empties the basket when the order is confirmed, which happens
      // asynchronously; dropping the local copy avoids showing a basket that is about to vanish.
      this.store.markStale();

      await this.router.navigate(['/orders', accepted.id]);
    } catch (caught: unknown) {
      if (caught instanceof ApiError) {
        this.fieldErrors.set(caught.fieldErrors);
        this.error.set(caught.fieldErrors ? null : caught.message);
      } else {
        this.error.set('We could not place your order. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
