import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../core/admin.service';
import { LocaleService, TranslatePipe } from '../../core/i18n';
import { ApiError } from '../../core/interceptors/error.interceptor';
import type { Paged, StockLevel } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { EmptyIcons, EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { type ChipSpec, StatusChip } from '../../shared/ui/status-chip';

@Component({
  selector: 'app-admin-stock',
  imports: [FormsModule, PageHeader, Skeleton, EmptyState, StatusChip, TranslatePipe],
  template: `
    <app-page-header
      title="Stock"
      description="Available is on hand minus what is reserved against open orders.">
      <label class="toggle">
        <input type="checkbox" [(ngModel)]="lowOnly" (change)="reload()" />
        <span>{{ 'admin.stock.lowOnly' | t }}</span>
      </label>
    </app-page-header>

    @if (loading()) {
      <app-skeleton [count]="5" [height]="52" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state
        [icon]="EmptyIcons.stock"
        [title]="lowOnly ? 'Nothing is running low' : 'No stock records yet'"
        [description]="lowOnly
          ? 'Every line is above its reorder level.'
          : 'A product needs a stock record before it can be ordered.'" />
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">{{ 'admin.stock.caption' | t }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ 'product.sku' | t }}</th>
              <th scope="col" class="numeric">{{ 'admin.stock.onHand' | t }}</th>
              <th scope="col" class="numeric">{{ 'admin.stock.reserved' | t }}</th>
              <th scope="col" class="numeric">{{ 'admin.stock.available' | t }}</th>
              <th scope="col" class="numeric">{{ 'admin.stock.reorderAt' | t }}</th>
              <th scope="col">State</th>
              <th scope="col"><span class="sr-only">{{ 'common.actions' | t }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (line of page()!.items; track line.productId) {
              <tr>
                <td class="mono">{{ line.sku }}</td>
                <td class="numeric">
                  @if (editing() === line.productId) {
                    <input class="inline" type="number" min="0" [(ngModel)]="draftOnHand"
                           [attr.aria-label]="'Stock on hand for ' + line.sku" />
                  } @else {
                    {{ line.onHand }}
                  }
                </td>
                <td class="numeric">{{ line.reserved }}</td>
                <td class="numeric"><strong>{{ line.available }}</strong></td>
                <td class="numeric">
                  @if (editing() === line.productId) {
                    <input class="inline" type="number" min="0" [(ngModel)]="draftReorder"
                           [attr.aria-label]="'Reorder level for ' + line.sku" />
                  } @else {
                    {{ line.reorderLevel }}
                  }
                </td>
                <td>
                  @if (line.belowReorderLevel) {
                    <app-status-chip [spec]="lowChip" />
                  } @else {
                    <app-status-chip [spec]="okChip" />
                  }
                </td>
                <td class="actions">
                  @if (editing() === line.productId) {
                    <button type="button" class="link" (click)="save(line)" [disabled]="saving()">{{ 'common.save' | t }}</button>
                    <button type="button" class="link" (click)="editing.set(null)">{{ 'common.cancel' | t }}</button>
                  } @else {
                    <button type="button" class="link" (click)="edit(line)">{{ 'admin.stock.adjust' | t }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="note muted">
        Stock cannot be set below the reserved quantity — those units are already promised to
        open orders, and the shortfall would only surface at despatch.
      </p>
    }
  `,
  styleUrl: './admin-tables.scss',
  styles: `
    .toggle { display: flex; align-items: center; gap: 0.4rem; font-size: 0.9rem; }
    .inline {
      width: 80px; padding: 0.3rem 0.4rem; font: inherit; text-align: end;
      border: 1px solid var(--accent); border-radius: var(--sama-radius-sm);
      background: var(--surface); color: var(--text);
    }
    .note { font-size: 0.82rem; margin-block-start: 1rem; }
  `,
})
export class AdminStockTable {
  protected readonly locale = inject(LocaleService);
  protected readonly EmptyIcons = EmptyIcons;
  protected readonly lowChip: ChipSpec = { label: 'status.stock.low', tone: 'warn', translated: true };
  protected readonly okChip: ChipSpec = { label: 'status.stock.ok', tone: 'good', translated: true };

  private readonly admin = inject(AdminService);
  private readonly notifications = inject(NotificationService);

  protected readonly page = signal<Paged<StockLevel> | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly editing = signal<string | null>(null);

  protected lowOnly = false;
  protected draftOnHand = 0;
  protected draftReorder = 0;

  constructor() {
    void this.load();
  }

  protected reload(): void {
    void this.load();
  }

  protected edit(line: StockLevel): void {
    this.draftOnHand = line.onHand;
    this.draftReorder = line.reorderLevel;
    this.editing.set(line.productId);
  }

  protected async save(line: StockLevel): Promise<void> {
    this.saving.set(true);

    try {
      await this.admin.adjustStock(line.productId, {
        quantityOnHand: Number(this.draftOnHand),
        reorderLevel: Number(this.draftReorder),
      });

      this.notifications.success(this.locale.t('notify.stockUpdated', { sku: line.sku }));
      this.editing.set(null);
      await this.load();
    } catch (caught: unknown) {
      // The most likely refusal is setting stock below what is already reserved, and the
      // API's message says exactly that.
      if (!(caught instanceof ApiError)) {
        this.notifications.error(this.locale.t('notify.stockFailed'));
      }
    } finally {
      this.saving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.page.set(await this.admin.stock({ lowOnly: this.lowOnly, pageSize: 50 }));
    } finally {
      this.loading.set(false);
    }
  }
}
