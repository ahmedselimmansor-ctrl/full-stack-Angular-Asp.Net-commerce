import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { BasketStore } from '../../core/basket-store';
import type { ProductDetail } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { LocaleService, LocalisedPipe, MoneyPipe, TranslatePipe, pickLocalised } from '../../core/i18n';
import { ProductImage } from '../../shared/product-image';
import { Skeleton } from '../../shared/ui/skeleton';

@Component({
  selector: 'app-product-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MoneyPipe, TranslatePipe, LocalisedPipe, ProductImage, Skeleton],
  templateUrl: './product-detail.html',
  styleUrl: './product-detail.scss',
})
export class ProductDetailPage {
  private readonly catalog = inject(CatalogService);
  private readonly basket = inject(BasketStore);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  protected readonly locale = inject(LocaleService);

  /** Bound from the route by withComponentInputBinding(). */
  readonly slug = input.required<string>();

  protected readonly product = signal<ProductDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly selected = signal(0);
  protected readonly adding = signal(false);
  protected readonly quantity = signal(1);
  protected readonly quantities = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

  constructor() {
    queueMicrotask(() => void this.load());
  }

  /** Used for alt text and the basket toast, which are not rendered through a pipe. */
  protected displayName(item: ProductDetail): string {
    return pickLocalised(this.locale, item.name, item.nameAr);
  }

  protected onQuantityChange(event: Event): void {
    this.quantity.set(Number((event.target as HTMLSelectElement).value));
  }

  protected async addToBasket(item: ProductDetail): Promise<void> {
    if (!this.auth.isAuthenticated()) {
      this.notifications.info(this.locale.t('product.signInToBuy'));
      void this.router.navigate(['/sign-in'], { queryParams: { returnUrl: this.router.url } });
      return;
    }

    this.adding.set(true);

    try {
      await this.basket.add(item.id, this.quantity());
      this.notifications.success(
        this.locale.t('product.addedNamed', { name: this.displayName(item) }),
      );
    } catch {
      // Already reported by the error interceptor.
    } finally {
      this.adding.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.product.set(await this.catalog.bySlug(this.slug()));
    } catch {
      this.product.set(null);
    } finally {
      this.loading.set(false);
    }
  }
}
