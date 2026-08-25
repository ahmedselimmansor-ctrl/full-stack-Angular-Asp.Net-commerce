import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrderService } from '../../core/api.service';
import type { OrderSummary, Paged } from '../../core/models';
import { MoneyPipe } from '../../shared/money.pipe';
import { OrderStatusChip } from './order-status-chip';

@Component({
  selector: 'app-order-list',
  imports: [RouterLink, DatePipe, MoneyPipe, OrderStatusChip],
  template: `
    <div class="sama-page">
      <h1>Your orders</h1>

      @if (loading()) {
        <p class="sama-muted" aria-busy="true">Loading…</p>
      } @else if ((page()?.totalCount ?? 0) === 0) {
        <div class="sama-card empty">
          <h2>No orders yet</h2>
          <p class="sama-muted">When you place an order it will appear here.</p>
          <a class="sama-button" routerLink="/">Start shopping</a>
        </div>
      } @else {
        <ul class="orders">
          @for (order of page()!.items; track order.id) {
            <li>
              <a class="order" [routerLink]="['/orders', order.id]">
                <div>
                  <p class="order__number">{{ order.orderNumber }}</p>
                  <p class="order__meta sama-muted">
                    {{ order.createdAt | date: 'mediumDate' }} · {{ order.lineCount }} item(s)
                  </p>
                </div>
                <app-order-status-chip [status]="order.status" />
                <p class="order__total">{{ order.total | money: order.currency }}</p>
              </a>
            </li>
          }
        </ul>

        @if (page()!.totalPages > 1) {
          <nav class="pager">
            <button type="button" class="sama-button sama-button--ghost"
                    [disabled]="!page()!.hasPrevious" (click)="go(page()!.page - 1)">Previous</button>
            <span class="sama-muted">Page {{ page()!.page }} of {{ page()!.totalPages }}</span>
            <button type="button" class="sama-button sama-button--ghost"
                    [disabled]="!page()!.hasNext" (click)="go(page()!.page + 1)">Next</button>
          </nav>
        }
      }
    </div>
  `,
  styles: `
    .orders { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 0.6rem; }
    .order {
      display: grid; grid-template-columns: 1fr auto auto; gap: 1.25rem; align-items: center;
      padding: 1rem; text-decoration: none;
      background: var(--sama-surface); border: 1px solid var(--sama-border); border-radius: var(--sama-radius);
    }
    .order:hover { border-color: var(--sama-accent); }
    .order p { margin: 0; }
    .order__number { font-weight: 600; font-variant-numeric: tabular-nums; }
    .order__meta { font-size: 0.82rem; }
    .order__total { font-weight: 600; }
    .empty { text-align: center; padding: 3rem; }
    .empty .sama-button { margin-top: 1rem; }
    .pager { display: flex; align-items: center; justify-content: center; gap: 1rem; margin-top: 1.5rem; }
    @media (max-width: 620px) { .order { grid-template-columns: 1fr auto; row-gap: 0.5rem; } }
  `,
})
export class OrderListPage {
  private readonly orders = inject(OrderService);

  protected readonly page = signal<Paged<OrderSummary> | null>(null);
  protected readonly loading = signal(true);

  constructor() {
    void this.load(1);
  }

  protected async go(page: number): Promise<void> {
    await this.load(page);
  }

  private async load(page: number): Promise<void> {
    this.loading.set(true);
    try {
      this.page.set(await this.orders.list(page));
    } finally {
      this.loading.set(false);
    }
  }
}
