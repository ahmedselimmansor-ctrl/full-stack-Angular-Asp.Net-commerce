/** Wire contracts shared with the Sama API. Field names match the JSON exactly. */

export interface Paged<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPrevious: boolean;
  readonly hasNext: boolean;
}

export interface ProductSummary {
  readonly id: string;
  readonly sku: string;
  readonly name: string;
  readonly slug: string;
  readonly brand: string | null;
  readonly price: number;
  readonly currency: string;
  readonly categoryId: string;
  readonly primaryImageUrl: string | null;
}

export interface ProductDetail extends Omit<ProductSummary, 'primaryImageUrl'> {
  readonly description: string;
  readonly status: string;
  readonly images: readonly ProductImage[];
}

export interface ProductImage {
  readonly url: string;
  readonly altText: string | null;
  readonly sortOrder: number;
}

export interface CategoryNode {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly sortOrder: number;
  readonly children: readonly CategoryNode[];
}

export interface Basket {
  readonly ownerId: string;
  readonly items: readonly BasketLine[];
  readonly totalQuantity: number;
  readonly subtotal: number;
  readonly currency: string;
  readonly updatedAt: string;
}

export interface BasketLine {
  readonly productId: string;
  readonly sku: string;
  readonly name: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly lineTotal: number;
  readonly currency: string;
  readonly imageUrl: string | null;
}

export type OrderStatus = 'Pending' | 'AwaitingPayment' | 'Confirmed' | 'Shipped' | 'Cancelled';

export interface OrderSummary {
  readonly id: string;
  readonly orderNumber: string;
  readonly status: OrderStatus;
  readonly total: number;
  readonly currency: string;
  readonly lineCount: number;
  readonly createdAt: string;
}

export interface OrderDetail {
  readonly id: string;
  readonly orderNumber: string;
  readonly status: OrderStatus;
  readonly total: number;
  readonly currency: string;
  readonly lines: readonly OrderLine[];
  readonly shippingAddress: ShippingAddress;
  readonly cancellationReason: string | null;
  readonly createdAt: string;
  readonly confirmedAt: string | null;
}

export interface OrderLine {
  readonly productId: string;
  readonly sku: string;
  readonly name: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly lineTotal: number;
}

export interface ShippingAddress {
  readonly line1: string;
  readonly city: string;
  readonly postcode: string;
  readonly country: string;
}

export interface TokenResponse {
  readonly accessToken: string;
  /** Present for native clients. The browser ignores it and relies on the HttpOnly cookie. */
  readonly refreshToken: string;
  readonly tokenType: string;
  readonly expiresIn: number;
}

export interface UserProfile {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly emailConfirmed: boolean;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

/** RFC 9457 problem document, as returned by every Sama failure path. */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly code?: string;
  readonly traceId?: string;
  readonly errors?: Record<string, string[]>;
}
