import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from './app-config';
import type {
  AdjustStockRequest,
  CreateCategoryRequest,
  CreateProductRequest,
  CreateStockItemRequest,
  ManagedProduct,
  OrderSummary,
  Paged,
  PaymentView,
  ProductDetail,
  ProductStatus,
  StockLevel,
  UpdateProductRequest,
  UserSummary,
} from './models';

/**
 * The back-office API.
 *
 * Kept separate from the storefront services because the permissions differ, the caching
 * rules are the opposite (nothing here is cacheable — it includes unpublished products), and
 * mixing them would make it easy to call a management endpoint from a public page.
 */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(APP_CONFIG).apiBaseUrl;

  // --- Catalogue ---------------------------------------------------------------------------

  products(options: {
    page?: number;
    pageSize?: number;
    q?: string | null;
    status?: ProductStatus | null;
  } = {}): Promise<Paged<ManagedProduct>> {
    let params = new HttpParams()
      .set('page', String(options.page ?? 1))
      .set('pageSize', String(options.pageSize ?? 20));

    if (options.q) params = params.set('q', options.q);
    if (options.status) params = params.set('status', options.status);

    return firstValueFrom(
      this.http.get<Paged<ManagedProduct>>(`${this.base}/api/v1/products/manage`, { params }),
    );
  }

  product(id: string): Promise<ProductDetail> {
    return firstValueFrom(this.http.get<ProductDetail>(`${this.base}/api/v1/products/manage/${id}`));
  }

  createProduct(request: CreateProductRequest): Promise<{ id: string; slug: string }> {
    return firstValueFrom(
      this.http.post<{ id: string; slug: string }>(`${this.base}/api/v1/products`, request),
    );
  }

  updateProduct(id: string, request: UpdateProductRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.base}/api/v1/products/${id}`, request));
  }

  changePrice(id: string, price: number, currency: string): Promise<void> {
    return firstValueFrom(
      this.http.patch<void>(`${this.base}/api/v1/products/${id}/price`, { price, currency }),
    );
  }

  publishProduct(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.base}/api/v1/products/${id}/publish`, {}));
  }

  deleteProduct(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/api/v1/products/${id}`));
  }

  createCategory(request: CreateCategoryRequest): Promise<{ id: string; slug: string }> {
    return firstValueFrom(
      this.http.post<{ id: string; slug: string }>(`${this.base}/api/v1/categories`, request),
    );
  }

  // --- Inventory ---------------------------------------------------------------------------

  stock(options: { page?: number; pageSize?: number; lowOnly?: boolean } = {}): Promise<Paged<StockLevel>> {
    const params = new HttpParams()
      .set('page', String(options.page ?? 1))
      .set('pageSize', String(options.pageSize ?? 50))
      .set('lowOnly', String(options.lowOnly ?? false));

    return firstValueFrom(this.http.get<Paged<StockLevel>>(`${this.base}/api/v1/stock`, { params }));
  }

  createStock(request: CreateStockItemRequest): Promise<StockLevel> {
    return firstValueFrom(this.http.post<StockLevel>(`${this.base}/api/v1/stock`, request));
  }

  adjustStock(productId: string, request: AdjustStockRequest): Promise<StockLevel> {
    return firstValueFrom(this.http.put<StockLevel>(`${this.base}/api/v1/stock/${productId}`, request));
  }

  // --- Orders and payments ------------------------------------------------------------------

  orders(page = 1, pageSize = 20): Promise<Paged<OrderSummary>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return firstValueFrom(this.http.get<Paged<OrderSummary>>(`${this.base}/api/v1/orders`, { params }));
  }

  payments(page = 1, pageSize = 20): Promise<Paged<PaymentView>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return firstValueFrom(this.http.get<Paged<PaymentView>>(`${this.base}/api/v1/payments`, { params }));
  }

  refundPayment(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.base}/api/v1/payments/${id}/refund`, {}));
  }

  // --- Users -----------------------------------------------------------------------------------

  users(options: { page?: number; pageSize?: number; search?: string | null } = {}): Promise<Paged<UserSummary>> {
    let params = new HttpParams()
      .set('page', String(options.page ?? 1))
      .set('pageSize', String(options.pageSize ?? 20));

    if (options.search) params = params.set('search', options.search);

    return firstValueFrom(this.http.get<Paged<UserSummary>>(`${this.base}/api/v1/users`, { params }));
  }

  assignRole(userId: string, role: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.base}/api/v1/users/${userId}/roles`, { role }));
  }

  removeRole(userId: string, role: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`${this.base}/api/v1/users/${userId}/roles/${encodeURIComponent(role)}`),
    );
  }

  deactivateUser(userId: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.base}/api/v1/users/${userId}/deactivate`, {}));
  }
}
