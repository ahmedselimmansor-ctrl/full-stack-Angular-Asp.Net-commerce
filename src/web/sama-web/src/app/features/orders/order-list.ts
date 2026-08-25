import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrderService } from '../../core/api.service';
import type { OrderSummary, Paged } from '../../core/models';
import { MoneyPipe, TranslatePipe } from '../../core/i18n';
import { OrderStatusChip } from './order-status-chip';

@Component({
  selector: 'app-order-list',
  imports: [RouterLink, DatePipe, MoneyPipe, OrderStatusChip, TranslatePipe],
  template: `
    <div class="page">
      <h1>{{ 'orders.title' | t }}</h1>

      @if (loading()) {
        <p class="muted" aria-busy="true">{{ 'common.loading' | t }}</p>
      } @else if ((page()?.totalCount ?? 0) === 0) {
        <div class="card empty">
          <h2>{{ 'orders.empty.title' | t }}</h2>
          <p class="muted">{{ 'orders.empty.copy' | t }}</p>
          <a class="btn" routerLink="/shop">{{ 'basket.empty.action' | t }}</a>
        </div>
      } @else {
        <ul class="orders">
          @for (order of page()!.items; track order.id) {
            <li>
              <a class="order" [routerLink]="['/orders', order.id]">
                <div>
                  <p class="order__number">{{ order.orderNumber }}</p>
                  <p class="order__meta muted">
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
            <button type="button" class="btn btn--ghost"
                    [disabled]="!page()!.hasPrevious" (click)="go(page()!.page - 1)">{{ 'common.previous' | t }}</button>
            <span class="muted">Page {{ page()!.page }} of {{ page()!.totalPages }}</span>
            <button type="button" class="btn btn--ghost"
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
      background: var(--surface); border: 1px solid var(--line); border-radius: var(--sama-radius);
    }
    .order:hover { border-color: var(--accent); }
    .order p { margin: 0; }
    .order__number { font-weight: 600; font-variant-numeric: tabular-nums; }
    .order__meta { font-size: 0.82rem; }
    .order__total { font-weight: 600; }
    .empty { text-align: center; padding: 3rem; }
    .empty .btn { margin-top: 1rem; }
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
