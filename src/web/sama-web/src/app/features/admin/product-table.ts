import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import type { ManagedProduct, Paged, ProductStatus } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { LocaleService, MoneyPipe, TranslatePipe } from '../../core/i18n';
import { ProductImage } from '../../shared/product-image';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog';
import { EmptyIcons, EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';
import { StatusChip, productStatusChip } from '../../shared/ui/status-chip';

@Component({
  selector: 'app-admin-products',
  imports: [
    RouterLink, FormsModule, MoneyPipe, ProductImage,
    PageHeader, Skeleton, EmptyState, StatusChip, ConfirmDialog,
  TranslatePipe,
  ],
  template: `
    <app-page-header [title]="'admin.products.title' | t" [description]="'admin.products.lede' | t">
      <a class="btn" routerLink="new">{{ 'admin.products.new' | t }}</a>
    </app-page-header>

    <div class="filters">
      <label class="field">
        <span>{{ 'common.search' | t }}</span>
        <input type="search" [(ngModel)]="query" (input)="onSearch()" [placeholder]="'admin.products.searchHint' | t" />
      </label>

      <label class="field">
        <span>{{ 'field.status' | t }}</span>
        <select [(ngModel)]="status" (change)="reload()">
          <option [ngValue]="null">{{ 'common.all' | t }}</option>
          <option value="Draft">Draft</option>
          <option value="Published">{{ 'status.product.published' | t }}</option>
          <option value="Archived">{{ 'status.product.archived' | t }}</option>
        </select>
      </label>
    </div>

    @if (loading()) {
      <app-skeleton [count]="5" [height]="64" />
    } @else if ((page()?.totalCount ?? 0) === 0) {
      <app-empty-state
        [icon]="EmptyIcons.product"
        [title]="'admin.products.empty' | t"
        [description]="'admin.products.emptyCopy' | t">
        <a class="btn" routerLink="new">{{ 'admin.products.new' | t }}</a>
      </app-empty-state>
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">{{ 'admin.products.caption' | t }}</caption>
          <thead>
            <tr>
              <th scope="col" class="col-image"><span class="sr-only">{{ 'field.image' | t }}</span></th>
              <th scope="col">{{ 'field.product' | t }}</th>
              <th scope="col">{{ 'product.sku' | t }}</th>
              <th scope="col">{{ 'field.status' | t }}</th>
              <th scope="col" class="numeric">{{ 'field.price' | t }}</th>
              <th scope="col"><span class="sr-only">{{ 'common.actions' | t }}</span></th>
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

                  <!-- The Arabic name is shown beside the English one, and its
                       absence is marked. Without this, finding which of the
                       catalogue still needs translating means opening every
                       product in turn. -->
                  @if (product.nameAr) {
                    <span class="name-ar" dir="rtl" lang="ar">{{ product.nameAr }}</span>
                  } @else {
                    <span class="untranslated" [attr.title]="'admin.products.noArabicHint' | t">
                      {{ 'admin.products.noArabic' | t }}
                    </span>
                  }

                  @if (product.brand) { <span class="brand muted">{{ product.brand }}</span> }
                </td>
                <td class="mono">{{ product.sku }}</td>
                <td>
                  <app-status-chip [spec]="chip(product)" />
                </td>
                <td class="numeric">{{ product.price | money: product.currency }}</td>
                <td class="actions">
                  @if (product.status === 'Draft') {
                    <button type="button" class="link" (click)="publish(product)">{{ 'common.publish' | t }}</button>
                  }
                  <a class="link" [routerLink]="[product.id]">{{ 'common.edit' | t }}</a>
                  <button type="button" class="link link--danger" (click)="askToDelete(product)">{{ 'common.delete' | t }}</button>
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
      [open]="pendingDelete() !== null"
      [title]="'admin.products.deleteConfirm' | t"
      [message]="deleteMessage()"
      [confirmLabel]="'common.delete' | t"
      [destructive]="true"
      (confirmed)="confirmDelete()"
      (cancelled)="pendingDelete.set(null)" />
  `,
  styleUrl: './admin-tables.scss',
  styles: `
    .name-ar {
      display: block;
      font-size: 0.84rem;
      color: var(--text-soft);
      font-family: var(--sama-body);
    }

    .untranslated {
      display: inline-block;
      margin-block-start: 0.15rem;
      padding: 0.05rem 0.4rem;
      border-radius: var(--sama-radius-pill);
      background: var(--caution-wash);
      color: var(--caution);
      font-size: 0.7rem;
      font-weight: 600;
    }
  `,
})
export class AdminProductTable {
  protected readonly locale = inject(LocaleService);
  protected readonly EmptyIcons = EmptyIcons;
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
      this.notifications.success(this.locale.t('notify.productLive', { name: product.name }));
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
      this.notifications.success(this.locale.t('notify.productWithdrawn', { name: product.name }));
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
