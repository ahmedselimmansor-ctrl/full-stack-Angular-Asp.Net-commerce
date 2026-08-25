import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import type { OrderSummary, Paged } from '../../core/models';
import { MoneyPipe, TranslatePipe } from '../../core/i18n';
import { EmptyIcons, EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatusChip, orderStatusChip } from '../../shared/ui/status-chip';

@Component({
  selector: 'app-admin-orders',
  imports: [RouterLink, DatePipe, MoneyPipe, PageHeader, Skeleton, EmptyState, StatusChip, TranslatePipe],
  template: `
    <app-page-header title="Orders" [description]="'admin.orders.lede' | t" />

    @if (unsettled().length > 0) {
      <!-- An order that has not settled long after it was placed means the saga stalled.
           Surfacing it here beats waiting for a customer to ask where their order is. -->
      <div class="alert">
        <strong>{{ unsettled().length }} order(s) are still settling.</strong>
        <span class="muted">
          Orders normally reach Confirmed or Cancelled within seconds. Ones that linger usually
          mean the outbox is not draining — see the operations runbook.
        </span>
      </div>
    }

    @if (loading()) {
      <app-skeleton [count]="6" [height]="56" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state [icon]="EmptyIcons.order" [title]="'admin.orders.empty' | t" [description]="'admin.orders.emptyCopy' | t" />
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">{{ 'admin.orders.lede' | t }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ 'field.order' | t }}</th>
              <th scope="col">{{ 'orders.placed' | t }}</th>
              <th scope="col" class="numeric">{{ 'orders.items' | t }}</th>
              <th scope="col">{{ 'field.status' | t }}</th>
              <th scope="col" class="numeric">{{ 'orders.total' | t }}</th>
              <th scope="col"><span class="sr-only">{{ 'common.actions' | t }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (order of page()!.items; track order.id) {
              <tr>
                <td class="mono">{{ order.orderNumber }}</td>
                <td>{{ order.createdAt | date: 'medium' }}</td>
                <td class="numeric">{{ order.lineCount }}</td>
                <td>
                  <app-status-chip [spec]="chip(order.status)" />
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
          <button type="button" class="btn btn--ghost"
                  [disabled]="!page()!.hasPrevious" (click)="goTo(page()!.page - 1)">{{ 'common.previous' | t }}</button>
          <span class="muted">Page {{ page()!.page }} of {{ page()!.totalPages }}</span>
          <button type="button" class="btn btn--ghost"
                  [disabled]="!page()!.hasNext" (click)="goTo(page()!.page + 1)">{{ 'common.next' | t }}</button>
        </nav>
      }
    }
  `,
  styleUrl: './admin-tables.scss',
  styles: `
    .alert {
      display: flex; flex-direction: column; gap: 0.15rem;
      background: color-mix(in srgb, #b8860b 12%, transparent);
      /* Token, not a raw hex: this stripe has to change with the theme like every
         other caution surface, and it has to move to the leading edge in RTL. */
      border-inline-start: 3px solid var(--caution);
      border-radius: var(--sama-radius-sm);
      padding: 0.8rem 1rem;
      margin-block-end: 1.25rem;
      font-size: 0.9rem;
    }
  `,
})
export class AdminOrderTable {
  protected readonly EmptyIcons = EmptyIcons;
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
