import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import { CatalogService } from '../../core/api.service';
import { ApiError } from '../../core/interceptors/error.interceptor';
import type { CategoryNode, ProductImage as ProductImageModel } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';

/** Currencies the API accepts. Kept in step with Money's supported set. */
const CURRENCIES = ['USD', 'EUR', 'GBP', 'AED', 'SAR', 'EGP', 'QAR', 'JOD', 'KWD', 'JPY'] as const;

@Component({
  selector: 'app-admin-product-form',
  imports: [FormsModule, RouterLink, PageHeader, Skeleton],
  template: `
    <app-page-header
      [title]="isNew() ? 'New product' : 'Edit product'"
      [description]="isNew() ? 'Products are created as drafts and are not visible until published.' : ''">
      <a class="sama-button sama-button--ghost" routerLink="/admin/products">Cancel</a>
    </app-page-header>

    @if (loading()) {
      <app-skeleton [count]="4" [height]="72" />
    } @else {
      <form class="sama-card form" (submit)="save($event)" novalidate>
        <div class="row">
          <label class="sama-field">
            <span>SKU</span>
            <input name="sku" [(ngModel)]="sku" [disabled]="!isNew() || saving()" required />
            <!-- The SKU is the merchant's own identifier and is referenced by orders and
                 stock, so it is fixed once the product exists. -->
            @if (!isNew()) { <span class="hint sama-muted">A SKU cannot be changed after creation.</span> }
            @if (fieldError('Sku'); as m) { <span class="sama-error">{{ m }}</span> }
          </label>

          <label class="sama-field">
            <span>Name</span>
            <input name="name" [(ngModel)]="name" (blur)="suggestSlug()" [disabled]="saving()" required />
            @if (fieldError('Name'); as m) { <span class="sama-error">{{ m }}</span> }
          </label>
        </div>

        <label class="sama-field">
          <span>URL slug</span>
          <input name="slug" [(ngModel)]="slug" [disabled]="saving()" required />
          <span class="hint sama-muted">Appears in the product's address: /products/{{ slug || 'your-slug' }}</span>
          @if (fieldError('Slug'); as m) { <span class="sama-error">{{ m }}</span> }
        </label>

        <label class="sama-field">
          <span>Description</span>
          <textarea name="description" rows="5" [(ngModel)]="description" [disabled]="saving()" required></textarea>
          @if (fieldError('Description'); as m) { <span class="sama-error">{{ m }}</span> }
        </label>

        <div class="row">
          <label class="sama-field">
            <span>Category</span>
            <select name="categoryId" [(ngModel)]="categoryId" [disabled]="saving()" required>
              <option [ngValue]="''" disabled>Choose a category</option>
              @for (category of flatCategories(); track category.id) {
                <option [ngValue]="category.id">{{ category.label }}</option>
              }
            </select>
            @if (fieldError('CategoryId'); as m) { <span class="sama-error">{{ m }}</span> }
          </label>

          <label class="sama-field">
            <span>Brand</span>
            <input name="brand" [(ngModel)]="brand" [disabled]="saving()" />
          </label>
        </div>

        <div class="row">
          <label class="sama-field">
            <span>Price</span>
            <input name="price" type="number" step="0.01" min="0" [(ngModel)]="price" [disabled]="saving()" required />
            @if (fieldError('Price'); as m) { <span class="sama-error">{{ m }}</span> }
          </label>

          <label class="sama-field">
            <span>Currency</span>
            <select name="currency" [(ngModel)]="currency" [disabled]="saving()">
              @for (code of currencies; track code) { <option [value]="code">{{ code }}</option> }
            </select>
          </label>
        </div>

        <fieldset class="images">
          <legend>Images</legend>
          <p class="hint sama-muted">
            Paths in the media bucket, served through the CDN. A product needs at least one
            image before it can be published.
          </p>

          @for (image of images(); track $index; let i = $index) {
            <div class="image-row">
              <input [value]="image.url" (input)="updateImageUrl(i, $event)"
                     placeholder="/media/example.jpg" [disabled]="saving()" />
              <input [value]="image.altText ?? ''" (input)="updateImageAlt(i, $event)"
                     placeholder="Alt text, for screen readers" [disabled]="saving()" />
              <button type="button" class="sama-button sama-button--ghost"
                      (click)="removeImage(i)" [disabled]="saving()"
                      [attr.aria-label]="'Remove image ' + (i + 1)">Remove</button>
            </div>
          }

          <button type="button" class="sama-button sama-button--ghost" (click)="addImage()" [disabled]="saving()">
            Add an image
          </button>
        </fieldset>

        @if (error()) { <p class="sama-error" role="alert">{{ error() }}</p> }

        <div class="form__actions">
          <button type="submit" class="sama-button" [disabled]="saving()">
            {{ saving() ? 'Saving…' : isNew() ? 'Create product' : 'Save changes' }}
          </button>
          <a class="sama-button sama-button--ghost" routerLink="/admin/products">Cancel</a>
        </div>
      </form>
    }
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: 0.25rem; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
    .hint { display: block; font-size: 0.78rem; margin-top: 0.25rem; }
    textarea {
      width: 100%; padding: 0.6rem 0.7rem; font: inherit;
      border: 1px solid var(--sama-border); border-radius: var(--sama-radius-sm);
      background: var(--sama-surface); color: var(--sama-ink); resize: vertical;
    }
    .images {
      border: 1px solid var(--sama-border);
      border-radius: var(--sama-radius-sm);
      padding: 1rem;
      margin: 0.5rem 0 1rem;
    }
    .images legend { font-size: 0.85rem; font-weight: 600; padding: 0 0.4rem; }
    .image-row { display: grid; grid-template-columns: 1.4fr 1fr auto; gap: 0.5rem; margin-bottom: 0.5rem; }
    .image-row input {
      padding: 0.5rem 0.6rem; font: inherit; font-size: 0.9rem;
      border: 1px solid var(--sama-border); border-radius: var(--sama-radius-sm);
      background: var(--sama-surface); color: var(--sama-ink);
    }
    .form__actions { display: flex; gap: 0.5rem; margin-top: 0.5rem; }
    .form__actions a { text-decoration: none; }
    @media (max-width: 720px) {
      .row, .image-row { grid-template-columns: 1fr; }
    }
  `,
})
export class AdminProductForm {
  private readonly admin = inject(AdminService);
  private readonly catalog = inject(CatalogService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Route parameter: 'new' or a product id. */
  readonly id = input<string>('new');

  protected readonly isNew = computed(() => this.id() === 'new');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]> | null>(null);
  protected readonly images = signal<ProductImageModel[]>([]);
  protected readonly flatCategories = signal<readonly { id: string; label: string }[]>([]);

  protected readonly currencies = CURRENCIES;

  protected sku = '';
  protected name = '';
  protected slug = '';
  protected description = '';
  protected categoryId = '';
  protected brand = '';
  protected price = 0;
  protected currency = 'USD';

  /** Price is changed through its own endpoint, so it is tracked to know whether it moved. */
  private originalPrice = 0;
  private originalCurrency = 'USD';

  constructor() {
    queueMicrotask(() => void this.load());
  }

  protected fieldError(field: string): string | null {
    return this.fieldErrors()?.[field]?.[0] ?? null;
  }

  /** Fills an empty slug from the name, rather than making someone type it twice. */
  protected suggestSlug(): void {
    if (this.slug.trim() || !this.name.trim()) return;

    this.slug = this.name
      .toLowerCase()
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')   // strip accents
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
  }

  protected addImage(): void {
    this.images.update(list => [...list, { url: '', altText: '', sortOrder: list.length }]);
  }

  protected removeImage(index: number): void {
    this.images.update(list => list.filter((_, i) => i !== index));
  }

  protected updateImageUrl(index: number, event: Event): void {
    const url = (event.target as HTMLInputElement).value;
    this.images.update(list => list.map((img, i) => (i === index ? { ...img, url } : img)));
  }

  protected updateImageAlt(index: number, event: Event): void {
    const altText = (event.target as HTMLInputElement).value;
    this.images.update(list => list.map((img, i) => (i === index ? { ...img, altText } : img)));
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();

    this.saving.set(true);
    this.error.set(null);
    this.fieldErrors.set(null);

    const images = this.images()
      .filter(i => i.url.trim())
      .map((i, index) => ({ url: i.url.trim(), altText: i.altText?.trim() || null, sortOrder: index }));

    try {
      if (this.isNew()) {
        const created = await this.admin.createProduct({
          sku: this.sku.trim(),
          name: this.name.trim(),
          slug: this.slug.trim(),
          description: this.description.trim(),
          categoryId: this.categoryId,
          price: Number(this.price),
          currency: this.currency,
          brand: this.brand.trim() || null,
          images,
        });

        this.notifications.success(`${this.name} created as a draft.`);
        await this.router.navigate(['/admin/products', created.id]);
        return;
      }

      await this.admin.updateProduct(this.id(), {
        name: this.name.trim(),
        slug: this.slug.trim(),
        description: this.description.trim(),
        categoryId: this.categoryId,
        brand: this.brand.trim() || null,
        images,
      });

      // Price lives behind its own endpoint because changing it emits an event that
      // invalidates caches downstream; only call it when the price actually moved.
      if (Number(this.price) !== this.originalPrice || this.currency !== this.originalCurrency) {
        await this.admin.changePrice(this.id(), Number(this.price), this.currency);
        this.originalPrice = Number(this.price);
        this.originalCurrency = this.currency;
      }

      this.notifications.success('Changes saved.');
    } catch (caught: unknown) {
      if (caught instanceof ApiError) {
        this.fieldErrors.set(caught.fieldErrors);
        this.error.set(caught.fieldErrors ? null : caught.message);
      } else {
        this.error.set('The product could not be saved.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const categories = await this.catalog.categories();
      this.flatCategories.set(this.flatten(categories));

      if (this.isNew()) {
        this.addImage();
        return;
      }

      const product = await this.admin.product(this.id());

      this.sku = product.sku;
      this.name = product.name;
      this.slug = product.slug;
      this.description = product.description;
      this.categoryId = product.categoryId;
      this.brand = product.brand ?? '';
      this.price = product.price;
      this.currency = product.currency;
      this.originalPrice = product.price;
      this.originalCurrency = product.currency;
      this.images.set(product.images.map(i => ({ ...i })));

      if (this.images().length === 0) this.addImage();
    } catch {
      this.error.set('This product could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }

  /** Flattens the tree into indented options, so nesting is visible in a plain select. */
  private flatten(nodes: readonly CategoryNode[], depth = 0): { id: string; label: string }[] {
    return nodes.flatMap(node => [
      { id: node.id, label: `${'  '.repeat(depth)}${depth > 0 ? '↳ ' : ''}${node.name}` },
      ...this.flatten(node.children, depth + 1),
    ]);
  }
}
