import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { AdminService } from '../../core/admin.service';
import type { Paged, PaymentView } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { MoneyPipe, TranslatePipe } from '../../core/i18n';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyIcons, EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatusChip, paymentStatusChip } from '../../shared/ui/status-chip';
import { HasPermissionDirective } from '../../core/has-permission.directive';

@Component({
  selector: 'app-admin-payments',
  imports: [
    DatePipe, MoneyPipe, PageHeader, Skeleton, EmptyState, StatusChip, ConfirmDialog,
    HasPermissionDirective,
  TranslatePipe,
  ],
  template: `
    <app-page-header [title]="'admin.nav.payments' | t" [description]="'admin.payments.lede' | t" />

    @if (loading()) {
      <app-skeleton [count]="5" [height]="56" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state [icon]="EmptyIcons.payment" [title]="'admin.payments.empty' | t" [description]="'admin.payments.emptyCopy' | t" />
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">{{ 'admin.nav.payments' | t }}</caption>
          <thead>
            <tr>
              <th scope="col">Taken</th>
              <th scope="col" class="numeric">{{ 'field.amount' | t }}</th>
              <th scope="col">{{ 'field.status' | t }}</th>
              <th scope="col">{{ 'field.detail' | t }}</th>
              <th scope="col"><span class="sr-only">{{ 'common.actions' | t }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (payment of page()!.items; track payment.id) {
              <tr>
                <td>{{ payment.createdAt | date: 'medium' }}</td>
                <td class="numeric">{{ payment.amount | money: payment.currency }}</td>
                <td>
                  <app-status-chip [spec]="chip(payment.status)" />
                </td>
                <td class="muted detail">{{ payment.failureReason ?? '—' }}</td>
                <td class="actions">
                  @if (payment.status === 'Succeeded') {
                    <button *samaHasPermission="refundPermission" type="button"
                            class="link link--danger" (click)="askToRefund(payment)">{{ 'admin.payments.refund' | t }}</button>
                  }
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

    <app-confirm-dialog
      [open]="pendingRefund() !== null"
      [title]="'admin.payments.refundConfirm' | t"
      message="The amount is returned to the customer. This cannot be undone from here."
      [confirmLabel]="'admin.payments.refund' | t"
      [destructive]="true"
      (confirmed)="confirmRefund()"
      (cancelled)="pendingRefund.set(null)" />
  `,
  styleUrl: './admin-tables.scss',
  styles: `.detail { max-width: 32ch; font-size: 0.85rem; }`,
})
export class AdminPaymentTable {
  protected readonly EmptyIcons = EmptyIcons;
  private readonly admin = inject(AdminService);
  private readonly notifications = inject(NotificationService);

  protected readonly page = signal<Paged<PaymentView> | null>(null);
  protected readonly loading = signal(true);
  protected readonly pendingRefund = signal<PaymentView | null>(null);
  protected readonly chip = paymentStatusChip;
  protected readonly refundPermission = ['payment:payment:refund'];

  private currentPage = 1;

  constructor() {
    void this.load();
  }

  protected goTo(page: number): void {
    this.currentPage = page;
    void this.load();
  }

  protected askToRefund(payment: PaymentView): void {
    this.pendingRefund.set(payment);
  }

  protected async confirmRefund(): Promise<void> {
    const payment = this.pendingRefund();
    this.pendingRefund.set(null);
    if (!payment) return;

    try {
      await this.admin.refundPayment(payment.id);
      this.notifications.success('Refund recorded.');
      await this.load();
    } catch {
      // Reported by the interceptor.
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.page.set(await this.admin.payments(this.currentPage));
    } finally {
      this.loading.set(false);
    }
  }
}
