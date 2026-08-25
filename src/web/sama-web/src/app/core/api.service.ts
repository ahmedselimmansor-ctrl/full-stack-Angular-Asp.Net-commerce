import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from './app-config';
import type {
  Basket,
  CategoryNode,
  OrderDetail,
  OrderSummary,
  Paged,
  ProductDetail,
  ProductSummary,
  ShippingAddress,
} from './models';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(APP_CONFIG).apiBaseUrl;

  private categoryCache: Promise<CategoryNode[]> | null = null;

  list(options: {
    page?: number;
    pageSize?: number;
    categoryId?: string | null;
    q?: string | null;
    sort?: string | null;
  } = {}): Promise<Paged<ProductSummary>> {
    let params = new HttpParams()
      .set('page', String(options.page ?? 1))
      .set('pageSize', String(options.pageSize ?? 12));

    if (options.categoryId) params = params.set('categoryId', options.categoryId);
    if (options.q) params = params.set('q', options.q);
    if (options.sort) params = params.set('sort', options.sort);

    return firstValueFrom(
      this.http.get<Paged<ProductSummary>>(`${this.base}/api/v1/products`, { params }),
    );
  }

  bySlug(slug: string): Promise<ProductDetail> {
    return firstValueFrom(
      this.http.get<ProductDetail>(`${this.base}/api/v1/products/${encodeURIComponent(slug)}`),
    );
  }

  categories(): Promise<CategoryNode[]> {
    return firstValueFrom(this.http.get<CategoryNode[]>(`${this.base}/api/v1/categories`));
  }

  /**
   * The category tree changes about as often as the shop is redesigned, but every
   * department page needs it to turn a slug in the URL into the id the list
   * endpoint wants. Caching the in-flight promise means a visitor moving between
   * departments fetches it once, and two components mounting together share one
   * request rather than racing.
   */
  categoriesCached(): Promise<CategoryNode[]> {
    this.categoryCache ??= this.categories().catch((error) => {
      // A failed fetch must not poison the cache, or the shop stays broken until reload.
      this.categoryCache = null;
      throw error;
    });

    return this.categoryCache;
  }

  async categoryBySlug(slug: string): Promise<CategoryNode | null> {
    const flatten = (nodes: readonly CategoryNode[]): CategoryNode[] =>
      nodes.flatMap((node) => [node, ...flatten(node.children)]);

    return flatten(await this.categoriesCached()).find((node) => node.slug === slug) ?? null;
  }
}

@Injectable({ providedIn: 'root' })
export class BasketService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(APP_CONFIG).apiBaseUrl;

  get(): Promise<Basket> {
    return firstValueFrom(this.http.get<Basket>(`${this.base}/api/v1/basket`));
  }

  addItem(productId: string, quantity: number): Promise<Basket> {
    return firstValueFrom(
      this.http.post<Basket>(`${this.base}/api/v1/basket/items`, { productId, quantity }),
    );
  }

  setQuantity(productId: string, quantity: number): Promise<Basket> {
    return firstValueFrom(
      this.http.put<Basket>(`${this.base}/api/v1/basket/items/${productId}`, { quantity }),
    );
  }

  removeItem(productId: string): Promise<Basket> {
    return firstValueFrom(this.http.delete<Basket>(`${this.base}/api/v1/basket/items/${productId}`));
  }

  clear(): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/api/v1/basket`));
  }
}

export interface CheckoutAccepted {
  readonly id: string;
  readonly orderNumber: string;
  readonly status: string;
  readonly total: number;
  readonly currency: string;
}

@Injectable({ providedIn: 'root' })
export class OrderService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(APP_CONFIG).apiBaseUrl;

  /**
   * Places an order.
   *
   * The idempotency key is generated per attempt and reused across retries of that same
   * attempt, which is what makes a lost response safe: the server replays the original
   * outcome instead of charging the customer twice.
   */
  checkout(shippingAddress: ShippingAddress, idempotencyKey: string): Promise<CheckoutAccepted> {
    return firstValueFrom(
      this.http.post<CheckoutAccepted>(
        `${this.base}/api/v1/orders`,
        { shippingAddress },
        { headers: { 'Idempotency-Key': idempotencyKey } },
      ),
    );
  }

  list(page = 1, pageSize = 20): Promise<Paged<OrderSummary>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return firstValueFrom(
      this.http.get<Paged<OrderSummary>>(`${this.base}/api/v1/orders`, { params }),
    );
  }

  byId(id: string): Promise<OrderDetail> {
    return firstValueFrom(this.http.get<OrderDetail>(`${this.base}/api/v1/orders/${id}`));
  }

  cancel(id: string, reason: string): Promise<OrderDetail> {
    return firstValueFrom(
      this.http.post<OrderDetail>(`${this.base}/api/v1/orders/${id}/cancel`, { reason }),
    );
  }
}
