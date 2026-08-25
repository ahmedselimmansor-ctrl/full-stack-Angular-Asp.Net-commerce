import { Injectable, computed, inject, signal } from '@angular/core';
import { BasketService } from './api.service';
import { AuthService } from './auth.service';
import type { Basket } from './models';

/**
 * The basket, held once for the whole application.
 *
 * Every mutation returns the server's view and replaces local state with it, rather than the
 * client patching its own copy. The server clamps quantities, re-prices lines and enforces
 * limits, so any locally-computed basket would drift from what checkout will actually charge.
 */
@Injectable({ providedIn: 'root' })
export class BasketStore {
  private readonly api = inject(BasketService);
  private readonly auth = inject(AuthService);

  private readonly state = signal<Basket | null>(null);
  private readonly busy = signal(false);

  readonly basket = this.state.asReadonly();
  readonly isBusy = this.busy.asReadonly();

  readonly itemCount = computed(() => this.state()?.totalQuantity ?? 0);
  readonly subtotal = computed(() => this.state()?.subtotal ?? 0);
  readonly currency = computed(() => this.state()?.currency ?? 'USD');
  readonly isEmpty = computed(() => (this.state()?.items.length ?? 0) === 0);

  async refresh(): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.state.set(null);
      return;
    }

    await this.run(() => this.api.get());
  }

  async add(productId: string, quantity = 1): Promise<void> {
    await this.run(() => this.api.addItem(productId, quantity));
  }

  async setQuantity(productId: string, quantity: number): Promise<void> {
    await this.run(() => this.api.setQuantity(productId, quantity));
  }

  async remove(productId: string): Promise<void> {
    await this.run(() => this.api.removeItem(productId));
  }

  async clear(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.clear();
      await this.api.get().then((b) => this.state.set(b));
    } finally {
      this.busy.set(false);
    }
  }

  /** Called after checkout, where the server empties the basket on order confirmation. */
  markStale(): void {
    this.state.set(null);
  }

  private async run(operation: () => Promise<Basket>): Promise<void> {
    this.busy.set(true);
    try {
      this.state.set(await operation());
    } finally {
      // Always released: a failed request must not leave the UI permanently disabled.
      this.busy.set(false);
    }
  }
}
