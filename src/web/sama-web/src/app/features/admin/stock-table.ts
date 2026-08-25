import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../core/admin.service';
import { ApiError } from '../../core/interceptors/error.interceptor';
import type { Paged, StockLevel } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatusChip } from '../../shared/ui/status-chip';

@Component({
  selector: 'app-admin-stock',
  imports: [FormsModule, PageHeader, Skeleton, EmptyState, StatusChip],
  template: `
    <app-page-header
      title="Stock"
      description="Available is on hand minus what is reserved against open orders.">
      <label class="toggle">
        <input type="checkbox" [(ngModel)]="lowOnly" (change)="reload()" />
        <span>Low stock only</span>
      </label>
    </app-page-header>

    @if (loading()) {
      <app-skeleton [count]="5" [height]="52" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state
        icon="⛁"
        [title]="lowOnly ? 'Nothing is running low' : 'No stock records yet'"
        [description]="lowOnly
          ? 'Every line is above its reorder level.'
          : 'A product needs a stock record before it can be ordered.'" />
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">Stock levels by product</caption>
          <thead>
            <tr>
              <th scope="col">SKU</th>
              <th scope="col" class="numeric">On hand</th>
              <th scope="col" class="numeric">Reserved</th>
              <th scope="col" class="numeric">Available</th>
              <th scope="col" class="numeric">Reorder at</th>
              <th scope="col">State</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
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
                    <app-status-chip label="Low" tone="warn" />
                  } @else {
                    <app-status-chip label="In stock" tone="good" />
                  }
                </td>
                <td class="actions">
                  @if (editing() === line.productId) {
                    <button type="button" class="link" (click)="save(line)" [disabled]="saving()">Save</button>
                    <button type="button" class="link" (click)="editing.set(null)">Cancel</button>
                  } @else {
                    <button type="button" class="link" (click)="edit(line)">Adjust</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="note sama-muted">
        Stock cannot be set below the reserved quantity — those units are already promised to
        open orders, and the shortfall would only surface at despatch.
      </p>
    }
  `,
  styleUrl: './admin-tables.scss',
  styles: `
    .toggle { display: flex; align-items: center; gap: 0.4rem; font-size: 0.9rem; }
    .inline {
      width: 80px; padding: 0.3rem 0.4rem; font: inherit; text-align: right;
      border: 1px solid var(--sama-accent); border-radius: var(--sama-radius-sm);
      background: var(--sama-surface); color: var(--sama-ink);
    }
    .note { font-size: 0.82rem; margin-top: 1rem; }
  `,
})
export class AdminStockTable {
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

      this.notifications.success(`${line.sku} updated.`);
      this.editing.set(null);
      await this.load();
    } catch (caught: unknown) {
      // The most likely refusal is setting stock below what is already reserved, and the
      // API's message says exactly that.
      if (!(caught instanceof ApiError)) {
        this.notifications.error('The adjustment could not be saved.');
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
