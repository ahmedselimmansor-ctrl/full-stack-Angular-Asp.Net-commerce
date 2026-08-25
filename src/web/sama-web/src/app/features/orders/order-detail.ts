import { DatePipe } from '@angular/common';
import { Component, OnDestroy, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrderService } from '../../core/api.service';
import type { OrderDetail } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { MoneyPipe, TranslatePipe } from '../../core/i18n';
import { OrderStatusChip } from './order-status-chip';

@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, DatePipe, MoneyPipe, OrderStatusChip, TranslatePipe],
  template: `
    <div class="page">
      @if (loading()) {
        <p class="muted" aria-busy="true">{{ 'common.loading' | t }}</p>
      } @else if (order(); as detail) {
        <nav class="crumbs" [attr.aria-label]="'nav.breadcrumb' | t">
          <a routerLink="/orders">{{ 'orders.title' | t }}</a> <span aria-hidden="true">/</span> <span>{{ detail.orderNumber }}</span>
        </nav>

        <header class="head">
          <div>
            <h1>{{ detail.orderNumber }}</h1>
            <p class="muted">Placed {{ detail.createdAt | date: 'medium' }}</p>
          </div>
          <app-order-status-chip [status]="detail.status" />
        </header>

        @if (isSettling()) {
          <p class="settling" role="status">
            We’re confirming stock and payment. This page updates on its own.
          </p>
        }

        @if (detail.status === 'Cancelled' && detail.cancellationReason) {
          <div class="card cancelled">
            <strong>Cancelled</strong>
            <p class="muted">{{ detail.cancellationReason }}</p>
          </div>
        }

        <div class="layout">
          <section class="card">
            <h2>{{ 'orders.items' | t }}</h2>
            <ul class="lines">
              @for (line of detail.lines; track line.productId) {
                <li>
                  <span class="lines__name">{{ line.quantity }} × {{ line.name }}</span>
                  <span class="muted lines__sku">{{ line.sku }}</span>
                  <span class="lines__total">{{ line.lineTotal | money: detail.currency }}</span>
                </li>
              }
            </ul>
            <div class="total">
              <span>{{ 'orders.total' | t }}</span><span class="tabular">{{ detail.total | money: detail.currency }}</span>
            </div>
          </section>

          <aside class="card">
            <h2>{{ 'orders.delivery' | t }}</h2>
            <address>
              {{ detail.shippingAddress.line1 }}<br />
              {{ detail.shippingAddress.city }}<br />
              {{ detail.shippingAddress.postcode }}<br />
              {{ detail.shippingAddress.country }}
            </address>

            @if (canCancel()) {
              <button type="button" class="btn btn--danger full"
                      [disabled]="cancelling()" (click)="cancel(detail)">
                {{ cancelling() ? 'Cancelling…' : 'Cancel order' }}
              </button>
            }
          </aside>
        </div>
      } @else {
        <div class="card missing">
          <h1>{{ 'orders.notFound.title' | t }}</h1>
          <a class="btn" routerLink="/orders">{{ 'orders.backToOrders' | t }}</a>
        </div>
      }
    </div>
  `,
  styles: `
    .crumbs { font-size: 0.85rem; color: var(--text-soft); margin-bottom: 1rem; display: flex; gap: 0.4rem; }
    .crumbs a { text-decoration: none; }
    .head { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: 1.25rem; }
    .head p { margin: 0; }
    .settling {
      background: var(--accent-wash); color: var(--accent);
      padding: 0.7rem 1rem; border-radius: var(--sama-radius-sm); font-size: 0.9rem; margin: 0 0 1.25rem;
    }
    .cancelled { border-left: 3px solid var(--critical); margin-bottom: 1.25rem; }
    .cancelled p { margin: 0.25rem 0 0; }
    .layout { display: grid; grid-template-columns: 1fr 300px; gap: 1.5rem; align-items: start; }
    .lines { list-style: none; margin: 0 0 1rem; padding: 0; }
    .lines li { display: grid; grid-template-columns: 1fr auto auto; gap: 1rem; padding: 0.5rem 0; border-bottom: 1px solid var(--line); }
    .lines__sku { font-size: 0.8rem; }
    .lines__total { font-weight: 600; }
    .total { display: flex; justify-content: space-between; font-weight: 650; font-size: 1.05rem; }
    address { font-style: normal; line-height: 1.7; margin-bottom: 1rem; }
    .full { width: 100%; }
    .missing { text-align: center; padding: 3rem; }
    .missing .btn { margin-top: 1rem; }
    @media (max-width: 820px) { .layout { grid-template-columns: 1fr; } }
  `,
})
export class OrderDetailPage implements OnDestroy {
  private readonly orders = inject(OrderService);
  private readonly notifications = inject(NotificationService);

  readonly id = input.required<string>();

  protected readonly order = signal<OrderDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly cancelling = signal(false);

  /** True while the saga is still deciding the order's fate. */
  protected readonly isSettling = computed(() => {
    const status = this.order()?.status;
    return status === 'Pending' || status === 'AwaitingPayment';
  });

  protected readonly canCancel = computed(() => {
    const status = this.order()?.status;
    return status === 'Pending' || status === 'AwaitingPayment' || status === 'Confirmed';
  });

  private poller: ReturnType<typeof setInterval> | null = null;

  constructor() {
    queueMicrotask(() => void this.load());
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  protected async cancel(detail: OrderDetail): Promise<void> {
    this.cancelling.set(true);
    try {
      this.order.set(await this.orders.cancel(detail.id, 'Cancelled by the customer.'));
      this.notifications.success('Your order has been cancelled.');
    } catch {
      // Reported by the error interceptor.
    } finally {
      this.cancelling.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.order.set(await this.orders.byId(this.id()));
    } catch {
      this.order.set(null);
    } finally {
      this.loading.set(false);
    }

    // Checkout returns 202 while stock and payment are still resolving, so the first view of a
    // new order is almost always mid-saga. Poll until it settles, then stop — a fixed-interval
    // poll that never terminates is a slow leak of both battery and backend capacity.
    if (this.isSettling()) {
      this.startPolling();
    }
  }

  private startPolling(): void {
    this.stopPolling();

    this.poller = setInterval(async () => {
      try {
        const latest = await this.orders.byId(this.id());
        this.order.set(latest);

        if (!this.isSettling()) {
          this.stopPolling();
        }
      } catch {
        this.stopPolling();
      }
    }, 2000);

    // Hard stop after two minutes. If the saga has not settled by then something is wrong,
    // and polling forever would not fix it.
    setTimeout(() => this.stopPolling(), 120_000);
  }

  private stopPolling(): void {
    if (this.poller) {
      clearInterval(this.poller);
      this.poller = null;
    }
  }
}
