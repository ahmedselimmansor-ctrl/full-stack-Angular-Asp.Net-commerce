import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../../core/api.service';
import { BasketStore } from '../../core/basket-store';
import { LocaleService, TranslatePipe, type TranslationKey } from '../../core/i18n';
import { ProductCard } from '../../shared/ui/product-card';
import { Skeleton } from '../../shared/ui/skeleton';
import type { ProductSummary } from '../../core/models';
import { DEPARTMENTS } from '../catalog/departments';

@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ProductCard, Skeleton, TranslatePipe],
  templateUrl: './home-page.html',
  styleUrl: './home-page.scss',
})
export class HomePage {
  private readonly catalog = inject(CatalogService);
  private readonly basket = inject(BasketStore);
  protected readonly locale = inject(LocaleService);

  protected readonly departments = DEPARTMENTS;

  /**
   * Three simultaneous commitments, not a sequence — so they are a plain list and
   * carry no step numbers.
   */
  protected readonly principles: ReadonlyArray<{ title: TranslationKey; copy: TranslationKey }> = [
    { title: 'home.promise.one.title', copy: 'home.promise.one.copy' },
    { title: 'home.promise.two.title', copy: 'home.promise.two.copy' },
    { title: 'home.promise.three.title', copy: 'home.promise.three.copy' },
  ];

  protected readonly arriving = signal(true);
  protected readonly failed = signal(false);
  protected readonly newest = signal<readonly ProductSummary[]>([]);
  protected readonly adding = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.arriving.set(true);
    this.failed.set(false);

    try {
      const page = await this.catalog.list({ pageSize: 8, sort: 'newest' });
      this.newest.set(page.items);
    } catch {
      this.failed.set(true);
    } finally {
      this.arriving.set(false);
    }
  }

  protected async add(product: ProductSummary): Promise<void> {
    this.adding.set(product.id);

    try {
      await this.basket.add(product.id, 1);
    } finally {
      this.adding.set(null);
    }
  }
}
