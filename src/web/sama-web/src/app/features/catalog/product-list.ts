import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { BasketStore } from '../../core/basket-store';
import type { CategoryNode, Paged, ProductSummary } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { MoneyPipe } from '../../shared/money.pipe';
import { ProductImage } from '../../shared/product-image';

@Component({
  selector: 'app-product-list',
  imports: [RouterLink, FormsModule, MoneyPipe, ProductImage],
  templateUrl: './product-list.html',
  styleUrl: './product-list.scss',
})
export class ProductListPage {
  private readonly catalog = inject(CatalogService);
  private readonly basket = inject(BasketStore);
  private readonly notifications = inject(NotificationService);
  protected readonly auth = inject(AuthService);

  protected readonly page = signal<Paged<ProductSummary> | null>(null);
  protected readonly categories = signal<readonly CategoryNode[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected query = '';
  protected categoryId: string | null = null;
  protected sort = 'name';

  private currentPage = 1;

  /** Cancels an in-flight search when the shopper keeps typing. */
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    void this.loadCategories();
    void this.load();
  }

  protected onSearchInput(): void {
    // Debounced: the search endpoint is the one that touches the database on every keystroke,
    // and it is rate-limited far more tightly than the plain listing.
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.currentPage = 1;
      void this.load();
    }, 350);
  }

  protected onFilterChange(): void {
    this.currentPage = 1;
    void this.load();
  }

  protected async goToPage(page: number): Promise<void> {
    this.currentPage = page;
    await this.load();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected async addToBasket(product: ProductSummary): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.notifications.info('Sign in to start a basket.');
      return;
    }

    try {
      await this.basket.add(product.id, 1);
      this.notifications.success(`${product.name} added to your basket.`);
    } catch {
      // The error interceptor has already surfaced the reason.
    }
  }

  protected pageNumbers(): number[] {
    const total = this.page()?.totalPages ?? 0;
    return Array.from({ length: total }, (_, i) => i + 1);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);

    try {
      this.page.set(
        await this.catalog.list({
          page: this.currentPage,
          pageSize: 12,
          q: this.query.trim() || null,
          categoryId: this.categoryId,
          sort: this.sort,
        }),
      );
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadCategories(): Promise<void> {
    try {
      this.categories.set(await this.catalog.categories());
    } catch {
      // A missing category filter degrades the page; it does not break browsing.
      this.categories.set([]);
    }
  }
}
