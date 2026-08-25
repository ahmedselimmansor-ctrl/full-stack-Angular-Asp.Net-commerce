import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import type { ManagedProduct, Paged, ProductStatus } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { MoneyPipe } from '../../shared/money.pipe';
import { ProductImage } from '../../shared/product-image';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatusChip, productStatusChip } from '../../shared/ui/status-chip';

@Component({
  selector: 'app-admin-products',
  imports: [
    RouterLink, FormsModule, MoneyPipe, ProductImage,
    PageHeader, Skeleton, EmptyState, StatusChip, ConfirmDialog,
  ],
  template: `
    <app-page-header title="Products" description="Everything in the catalogue, published or not.">
      <a class="sama-button" routerLink="new">New product</a>
    </app-page-header>

    <div class="filters">
      <label class="sama-field">
        <span>Search</span>
        <input type="search" [(ngModel)]="query" (input)="onSearch()" placeholder="Name or SKU" />
      </label>

      <label class="sama-field">
        <span>Status</span>
        <select [(ngModel)]="status" (change)="reload()">
          <option [ngValue]="null">All</option>
          <option value="Draft">Draft</option>
          <option value="Published">Published</option>
          <option value="Archived">Archived</option>
        </select>
      </label>
    </div>

    @if (loading()) {
      <app-skeleton [count]="5" [height]="64" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state
        icon="❑"
        title="No products yet"
        description="Create your first product to start selling.">
        <a class="sama-button" routerLink="new">New product</a>
      </app-empty-state>
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">Products in the catalogue</caption>
          <thead>
            <tr>
              <th scope="col" class="col-image"><span class="sr-only">Image</span></th>
              <th scope="col">Product</th>
              <th scope="col">SKU</th>
              <th scope="col">Status</th>
              <th scope="col" class="numeric">Price</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (product of page()!.items; track product.id) {
              <tr>
                <td class="col-image">
                  <div class="thumb">
                    <app-product-image [source]="product.primaryImageUrl" [alt]="product.name" />
                  </div>
                </td>
                <td>
                  <a class="name" [routerLink]="[product.id]">{{ product.name }}</a>
                  @if (product.brand) { <span class="brand sama-muted">{{ product.brand }}</span> }
                </td>
                <td class="mono">{{ product.sku }}</td>
                <td>
                  <app-status-chip [label]="chip(product).label" [tone]="chip(product).tone" />
                </td>
                <td class="numeric">{{ product.price | money: product.currency }}</td>
                <td class="actions">
                  @if (product.status === 'Draft') {
                    <button type="button" class="link" (click)="publish(product)">Publish</button>
                  }
                  <a class="link" [routerLink]="[product.id]">Edit</a>
                  <button type="button" class="link link--danger" (click)="askToDelete(product)">Delete</button>
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

    <app-confirm-dialog
      [open]="pendingDelete() !== null"
      title="Delete this product?"
      [message]="deleteMessage()"
      confirmLabel="Delete"
      [destructive]="true"
      (confirmed)="confirmDelete()"
      (cancelled)="pendingDelete.set(null)" />
  `,
  styleUrl: './admin-tables.scss',
})
export class AdminProductTable {
  private readonly admin = inject(AdminService);
  private readonly notifications = inject(NotificationService);

  protected readonly page = signal<Paged<ManagedProduct> | null>(null);
  protected readonly loading = signal(true);
  protected readonly pendingDelete = signal<ManagedProduct | null>(null);

  protected query = '';
  protected status: ProductStatus | null = null;
  private currentPage = 1;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Maps a product's status onto its chip label and tone. */
  protected chip(product: ManagedProduct): ReturnType<typeof productStatusChip> {
    return productStatusChip(product.status);
  }

  constructor() {
    void this.load();
  }

  protected onSearch(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.reload(), 300);
  }

  protected reload(): void {
    this.currentPage = 1;
    void this.load();
  }

  protected goTo(page: number): void {
    this.currentPage = page;
    void this.load();
  }

  protected async publish(product: ManagedProduct): Promise<void> {
    try {
      await this.admin.publishProduct(product.id);
      this.notifications.success(`${product.name} is now live.`);
      await this.load();
    } catch {
      // A product with no image cannot be published; the interceptor has said so.
    }
  }

  protected askToDelete(product: ManagedProduct): void {
    this.pendingDelete.set(product);
  }

  protected deleteMessage(): string {
    const product = this.pendingDelete();
    return product
      ? `“${product.name}” will be withdrawn from sale. Existing orders that reference it are unaffected.`
      : '';
  }

  protected async confirmDelete(): Promise<void> {
    const product = this.pendingDelete();
    this.pendingDelete.set(null);
    if (!product) return;

    try {
      await this.admin.deleteProduct(product.id);
      this.notifications.success(`${product.name} was withdrawn.`);
      await this.load();
    } catch {
      // Reported by the interceptor.
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.page.set(
        await this.admin.products({
          page: this.currentPage,
          q: this.query.trim() || null,
          status: this.status,
        }),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
