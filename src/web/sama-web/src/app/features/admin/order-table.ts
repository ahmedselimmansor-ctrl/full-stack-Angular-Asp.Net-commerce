import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import type { OrderSummary, Paged } from '../../core/models';
import { MoneyPipe } from '../../shared/money.pipe';
import { EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatusChip, orderStatusChip } from '../../shared/ui/status-chip';

@Component({
  selector: 'app-admin-orders',
  imports: [RouterLink, DatePipe, MoneyPipe, PageHeader, Skeleton, EmptyState, StatusChip],
  template: `
    <app-page-header title="Orders" description="Every order across the storefront." />

    @if (unsettled().length > 0) {
      <!-- An order that has not settled long after it was placed means the saga stalled.
           Surfacing it here beats waiting for a customer to ask where their order is. -->
      <div class="alert">
        <strong>{{ unsettled().length }} order(s) are still settling.</strong>
        <span class="sama-muted">
          Orders normally reach Confirmed or Cancelled within seconds. Ones that linger usually
          mean the outbox is not draining — see the operations runbook.
        </span>
      </div>
    }

    @if (loading()) {
      <app-skeleton [count]="6" [height]="56" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state icon="✇" title="No orders yet" description="Orders will appear here as customers check out." />
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">All orders</caption>
          <thead>
            <tr>
              <th scope="col">Order</th>
              <th scope="col">Placed</th>
              <th scope="col" class="numeric">Items</th>
              <th scope="col">Status</th>
              <th scope="col" class="numeric">Total</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (order of page()!.items; track order.id) {
              <tr>
                <td class="mono">{{ order.orderNumber }}</td>
                <td>{{ order.createdAt | date: 'medium' }}</td>
                <td class="numeric">{{ order.lineCount }}</td>
                <td>
                  <app-status-chip [label]="chip(order.status).label" [tone]="chip(order.status).tone" />
                </td>
                <td class="numeric">{{ order.total | money: order.currency }}</td>
                <td class="actions">
                  <a class="link" [routerLink]="['/orders', order.id]">View</a>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      @if (page()!.totalPages > 1) {
        <nav class="pager" aria-label="Pagination">
          <button type="button" class="sama-button sama-button--ghost"
                  [disabled]="!page()!.hasPrevious" (click)="goTo(page()!.page - 1)">Previous</button>
          <span class="sama-muted">Page {{ page()!.page }} of {{ page()!.totalPages }}</span>
          <button type="button" class="sama-button sama-button--ghost"
                  [disabled]="!page()!.hasNext" (click)="goTo(page()!.page + 1)">Next</button>
        </nav>
      }
    }
  `,
  styleUrl: './admin-tables.scss',
  styles: `
    .alert {
      display: flex; flex-direction: column; gap: 0.15rem;
      background: color-mix(in srgb, #b8860b 12%, transparent);
      border-left: 3px solid #b8860b;
      border-radius: var(--sama-radius-sm);
      padding: 0.8rem 1rem;
      margin-bottom: 1.25rem;
      font-size: 0.9rem;
    }
  `,
})
export class AdminOrderTable {
  private readonly admin = inject(AdminService);

  protected readonly page = signal<Paged<OrderSummary> | null>(null);
  protected readonly loading = signal(true);
  protected readonly chip = orderStatusChip;

  private currentPage = 1;

  constructor() {
    void this.load();
  }

  protected unsettled(): readonly OrderSummary[] {
    return (this.page()?.items ?? []).filter(
      o => o.status === 'Pending' || o.status === 'AwaitingPayment',
    );
  }

  protected goTo(page: number): void {
    this.currentPage = page;
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.page.set(await this.admin.orders(this.currentPage));
    } finally {
      this.loading.set(false);
    }
  }
}
