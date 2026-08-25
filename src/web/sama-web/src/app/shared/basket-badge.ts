import { Component, effect, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { BasketStore } from '../core/basket-store';

@Component({
  selector: 'app-basket-badge',
  imports: [RouterLink],
  template: `
    @if (auth.isAuthenticated()) {
      <a class="basket" routerLink="/basket" [attr.aria-label]="label()">
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" fill="none"
             stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
          <path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z" />
          <path d="M3 6h18M16 10a4 4 0 0 1-8 0" />
        </svg>
        @if (basket.itemCount() > 0) {
          <span class="basket__count">{{ basket.itemCount() }}</span>
        }
      </a>
    }
  `,
  styles: `
    .basket {
      position: relative;
      display: grid;
      place-items: center;
      width: 36px;
      height: 36px;
      border-radius: 8px;
      color: var(--sama-ink);
      text-decoration: none;
    }
    .basket:hover { background: var(--sama-surface-alt); }
    .basket__count {
      position: absolute;
      top: 0; right: 0;
      min-width: 17px;
      padding: 0 4px;
      border-radius: 9px;
      background: var(--sama-accent);
      color: var(--sama-accent-ink);
      font-size: 0.68rem;
      font-weight: 700;
      line-height: 17px;
      text-align: center;
    }
  `,
})
export class BasketBadge {
  protected readonly auth = inject(AuthService);
  protected readonly basket = inject(BasketStore);

  constructor() {
    // Loads the basket on sign-in and drops it on sign-out, without either flow having to
    // remember to tell the badge.
    effect(() => {
      if (this.auth.isAuthenticated()) {
        void this.basket.refresh();
      } else {
        this.basket.markStale();
      }
    });
  }

  protected label(): string {
    const count = this.basket.itemCount();
    return count === 0 ? 'Basket, empty' : `Basket, ${count} item${count === 1 ? '' : 's'}`;
  }
}
