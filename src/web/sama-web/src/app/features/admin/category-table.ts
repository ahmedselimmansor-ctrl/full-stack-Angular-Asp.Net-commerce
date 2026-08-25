import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../core/admin.service';
import { TranslatePipe } from '../../core/i18n';
import { CatalogService } from '../../core/api.service';
import { ApiError } from '../../core/interceptors/error.interceptor';
import type { CategoryNode } from '../../core/models';
import { NotificationService } from '../../core/notification.service';
import { EmptyIcons, EmptyState } from '../../shared/ui/empty-state';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';

@Component({
  selector: 'app-admin-categories',
  imports: [FormsModule, PageHeader, Skeleton, EmptyState, TranslatePipe],
  template: `
    <app-page-header [title]="'admin.categories.title' | t" [description]="'admin.categories.lede' | t" />

    <div class="layout">
      <section>
        @if (loading()) {
          <app-skeleton [count]="4" [height]="44" />
        } @else if (tree().length === 0) {
          <app-empty-state
            [icon]="EmptyIcons.category"
            [title]="'admin.categories.empty' | t"
            description="Products need a category, so create one before adding your first product." />
        } @else {
          <ul class="tree">
            @for (node of flattened(); track node.id) {
              <li [style.padding-left.rem]="node.depth * 1.25">
                <span class="tree__name">{{ node.name }}</span>
                <code class="tree__slug muted">/{{ node.slug }}</code>
              </li>
            }
          </ul>
        }
      </section>

      <aside class="card">
        <h2>{{ 'admin.categories.new' | t }}</h2>

        <form (submit)="create($event)" novalidate>
          <label class="field">
            <span>{{ 'field.name' | t }}</span>
            <input name="name" [(ngModel)]="name" (blur)="suggestSlug()" [disabled]="saving()" required />
            @if (fieldError('Name'); as m) { <span class="error-text">{{ m }}</span> }
          </label>

          <label class="field">
            <span>{{ 'field.slug' | t }}</span>
            <input name="slug" [(ngModel)]="slug" [disabled]="saving()" required />
            @if (fieldError('Slug'); as m) { <span class="error-text">{{ m }}</span> }
          </label>

          <label class="field">
            <span>{{ 'field.parent' | t }}</span>
            <select name="parentId" [(ngModel)]="parentId" [disabled]="saving()">
              <option [ngValue]="null">None — a top-level category</option>
              @for (node of flattened(); track node.id) {
                <option [ngValue]="node.id">{{ node.indentedName }}</option>
              }
            </select>
          </label>

          <label class="field">
            <span>{{ 'field.sortOrder' | t }}</span>
            <input name="sortOrder" type="number" min="0" [(ngModel)]="sortOrder" [disabled]="saving()" />
            <span class="hint muted">Lower numbers appear first.</span>
          </label>

          @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }

          <button type="submit" class="btn full" [disabled]="saving()">
            {{ saving() ? 'Creating…' : 'Create category' }}
          </button>
        </form>
      </aside>
    </div>
  `,
  styles: `
    .layout { display: grid; grid-template-columns: 1fr 320px; gap: 1.5rem; align-items: start; }
    .tree {
      list-style: none; margin: 0; padding: 0;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--sama-radius);
    }
    .tree li {
      display: flex; align-items: baseline; gap: 0.75rem;
      padding: 0.6rem 0.9rem;
      border-bottom: 1px solid var(--line);
    }
    .tree li:last-child { border-bottom: 0; }
    .tree__name { font-weight: 550; }
    .tree__slug { font-size: 0.8rem; }
    .full { width: 100%; margin-top: 0.5rem; }
    .hint { display: block; font-size: 0.78rem; margin-top: 0.25rem; }
    @media (max-width: 860px) { .layout { grid-template-columns: 1fr; } }
  `,
})
export class AdminCategoryTable {
  protected readonly EmptyIcons = EmptyIcons;
  private readonly admin = inject(AdminService);
  private readonly catalog = inject(CatalogService);
  private readonly notifications = inject(NotificationService);

  protected readonly tree = signal<readonly CategoryNode[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]> | null>(null);

  protected name = '';
  protected slug = '';
  protected parentId: string | null = null;
  protected sortOrder = 0;

  constructor() {
    void this.load();
  }

  protected fieldError(field: string): string | null {
    return this.fieldErrors()?.[field]?.[0] ?? null;
  }

  /** Flattens the tree for both the listing and the parent picker. */
  protected flattened(): readonly { id: string; name: string; slug: string; depth: number; indentedName: string }[] {
    const walk = (nodes: readonly CategoryNode[], depth: number): {
      id: string; name: string; slug: string; depth: number; indentedName: string;
    }[] =>
      nodes.flatMap(node => [
        {
          id: node.id,
          name: node.name,
          slug: node.slug,
          depth,
          indentedName: `${'  '.repeat(depth)}${depth > 0 ? '↳ ' : ''}${node.name}`,
        },
        ...walk(node.children, depth + 1),
      ]);

    return walk(this.tree(), 0);
  }

  protected suggestSlug(): void {
    if (this.slug.trim() || !this.name.trim()) return;

    this.slug = this.name
      .toLowerCase()
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
  }

  protected async create(event: Event): Promise<void> {
    event.preventDefault();

    this.saving.set(true);
    this.error.set(null);
    this.fieldErrors.set(null);

    try {
      await this.admin.createCategory({
        name: this.name.trim(),
        slug: this.slug.trim(),
        parentId: this.parentId,
        sortOrder: Number(this.sortOrder) || 0,
      });

      this.notifications.success(`${this.name} created.`);
      this.name = '';
      this.slug = '';
      this.parentId = null;
      this.sortOrder = 0;

      await this.load();
    } catch (caught: unknown) {
      if (caught instanceof ApiError) {
        this.fieldErrors.set(caught.fieldErrors);
        this.error.set(caught.fieldErrors ? null : caught.message);
      } else {
        this.error.set('The category could not be created.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.tree.set(await this.catalog.categories());
    } finally {
      this.loading.set(false);
    }
  }
}
