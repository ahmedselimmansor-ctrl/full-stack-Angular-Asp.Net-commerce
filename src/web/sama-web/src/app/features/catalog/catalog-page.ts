import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { CatalogService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { BasketStore } from '../../core/basket-store';
import { NotificationService } from '../../core/notification.service';
import { LocaleService, TranslatePipe, type TranslationKey } from '../../core/i18n';
import { ProductCard } from '../../shared/ui/product-card';
import { Skeleton } from '../../shared/ui/skeleton';
import type { CategoryNode, ProductSummary } from '../../core/models';
import { departmentBySlug, type Department } from './departments';

type SortKey = 'featured' | 'priceAsc' | 'priceDesc' | 'newest';

const SORT_PARAMS: Record<SortKey, string> = {
  featured: 'name',
  priceAsc: 'price',
  priceDesc: '-price',
  newest: 'newest',
};

/**
 * One page behind `/shop` and all three department routes.
 *
 * A department is `/shop` with its category pinned and an editorial masthead on
 * top. Splitting them into four components would have meant maintaining the same
 * grid, filter and pagination logic in four places for a difference that is
 * entirely presentational.
 */
@Component({
  selector: 'app-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ProductCard, Skeleton, TranslatePipe],
  templateUrl: './catalog-page.html',
  styleUrl: './catalog-page.scss',
})
export class CatalogPage {
  private readonly catalog = inject(CatalogService);
  private readonly basket = inject(BasketStore);
  private readonly notifications = inject(NotificationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  protected readonly locale = inject(LocaleService);

  private readonly params = toSignal(this.route.queryParams, { initialValue: {} as Record<string, string> });
  private readonly data = toSignal(this.route.data, { initialValue: {} as { department?: string } });

  protected readonly department = computed<Department | undefined>(() =>
    this.data().department ? departmentBySlug(this.data().department!) : undefined,
  );

  protected readonly query = computed(() => this.params()['q'] ?? '');
  protected readonly sort = computed<SortKey>(() => {
    const value = this.params()['sort'];
    return value in SORT_PARAMS ? (value as SortKey) : 'featured';
  });

  protected readonly categories = signal<readonly CategoryNode[]>([]);
  protected readonly categoryId = signal<string | null>(null);

  protected readonly items = signal<readonly ProductSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly hasNext = signal(false);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly failed = signal(false);
  protected readonly adding = signal<string | null>(null);

  protected readonly sortOptions: ReadonlyArray<{ key: SortKey; label: TranslationKey }> = [
    { key: 'featured', label: 'catalog.sort.featured' },
    { key: 'priceAsc', label: 'catalog.sort.priceAsc' },
    { key: 'priceDesc', label: 'catalog.sort.priceDesc' },
    { key: 'newest', label: 'catalog.sort.newest' },
  ];

  protected readonly hasFilters = computed(
    () => Boolean(this.query()) || (!this.department() && this.categoryId() !== null),
  );

  private page = 1;

  constructor() {
    void this.loadCategories();

    // Re-runs whenever the route lands on a different department or the query
    // string changes, which covers both navigation and in-page filtering.
    effect(() => {
      const department = this.department();
      const q = this.query();
      const sort = this.sort();
      void this.reload(department, q, sort);
    });
  }

  private async loadCategories(): Promise<void> {
    try {
      this.categories.set(await this.catalog.categoriesCached());
    } catch {
      // The filter simply does not render; the grid is still usable.
    }
  }

  private async reload(department: Department | undefined, q: string, sort: SortKey): Promise<void> {
    this.page = 1;
    this.loading.set(true);
    this.failed.set(false);

    try {
      let categoryId = this.categoryId();

      if (department) {
        const node = await this.catalog.categoryBySlug(department.slug);
        categoryId = node?.id ?? null;
        this.categoryId.set(categoryId);
      }

      const result = await this.catalog.list({
        page: 1,
        pageSize: 12,
        categoryId,
        q: q || null,
        sort: SORT_PARAMS[sort],
      });

      this.items.set(result.items);
      this.total.set(result.totalCount);
      this.hasNext.set(result.hasNext);
    } catch {
      this.failed.set(true);
      this.items.set([]);
    } finally {
      this.loading.set(false);
    }
  }

  protected async loadMore(): Promise<void> {
    if (this.loadingMore() || !this.hasNext()) return;

    this.loadingMore.set(true);

    try {
      const result = await this.catalog.list({
        page: this.page + 1,
        pageSize: 12,
        categoryId: this.categoryId(),
        q: this.query() || null,
        sort: SORT_PARAMS[this.sort()],
      });

      this.page += 1;
      this.items.update((current) => [...current, ...result.items]);
      this.hasNext.set(result.hasNext);
    } catch {
      this.notifications.error(this.locale.t('error.generic.copy'));
    } finally {
      this.loadingMore.set(false);
    }
  }

  /**
   * Filters live in the query string so a filtered view can be linked, shared and
   * reached with the back button. Keeping them in component state alone would
   * make the back button jump past the whole filtering session in one step.
   */
  protected setSort(value: string): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sort: value === 'featured' ? null : value },
      queryParamsHandling: 'merge',
    });
  }

  protected setCategory(value: string): void {
    this.categoryId.set(value || null);
    void this.reload(this.department(), this.query(), this.sort());
  }

  protected clearFilters(): void {
    this.categoryId.set(null);
    void this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  protected async add(product: ProductSummary): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.notifications.info(this.locale.t('auth.signIn.title'));
      void this.router.navigate(['/sign-in'], { queryParams: { returnUrl: this.router.url } });
      return;
    }

    this.adding.set(product.id);

    try {
      await this.basket.add(product.id, 1);
      this.notifications.success(this.locale.t('product.added'));
    } catch {
      this.notifications.error(this.locale.t('error.generic.copy'));
    } finally {
      this.adding.set(null);
    }
  }
}
