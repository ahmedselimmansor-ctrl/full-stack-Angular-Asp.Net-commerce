import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { HasPermissionDirective } from '../../core/has-permission.directive';

interface AdminLink {
  readonly path: string;
  readonly label: string;
  readonly icon: string;
  /** Any one of these grants access to the section. */
  readonly permissions: readonly string[];
}

/**
 * Back-office layout.
 *
 * Navigation entries are filtered by permission, so a catalogue manager does not see an
 * Orders link they would only be refused at. That is presentation, not protection — the API
 * enforces the same permissions, and hiding a link protects nothing on its own.
 */
@Component({
  selector: 'app-admin-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, HasPermissionDirective],
  template: `
    <div class="admin">
      <aside class="sidebar">
        <p class="sidebar__title">Back office</p>

        <nav aria-label="Back office">
          @for (link of links; track link.path) {
            <a *samaHasPermission="link.permissions"
               [routerLink]="link.path"
               routerLinkActive="is-active"
               [routerLinkActiveOptions]="{ exact: link.path === '' }">
              <span class="icon" aria-hidden="true">{{ link.icon }}</span>
              <span>{{ link.label }}</span>
            </a>
          }
        </nav>

        <a class="sidebar__exit" routerLink="/">Back to the shop</a>
      </aside>

      <section class="content">
        <router-outlet />
      </section>
    </div>
  `,
  styles: `
    .admin {
      display: grid;
      grid-template-columns: 220px 1fr;
      gap: 0;
      min-height: calc(100vh - 60px);
    }

    .sidebar {
      border-right: 1px solid var(--sama-border);
      background: var(--sama-surface);
      padding: 1.5rem 0.75rem;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }

    .sidebar__title {
      margin: 0 0.75rem 0.75rem;
      font-size: 0.72rem;
      font-weight: 700;
      letter-spacing: 0.09em;
      text-transform: uppercase;
      color: var(--sama-ink-soft);
    }

    .sidebar nav { display: flex; flex-direction: column; gap: 0.15rem; }

    .sidebar a {
      display: flex;
      align-items: center;
      gap: 0.65rem;
      padding: 0.55rem 0.75rem;
      border-radius: var(--sama-radius-sm);
      text-decoration: none;
      font-size: 0.92rem;
      color: var(--sama-ink-soft);
    }

    .sidebar a:hover { background: var(--sama-surface-alt); color: var(--sama-ink); }
    .sidebar a.is-active { background: var(--sama-accent-soft); color: var(--sama-accent); font-weight: 600; }

    .icon { width: 1.1rem; text-align: center; opacity: 0.85; }

    .sidebar__exit {
      margin-top: auto;
      font-size: 0.85rem;
      border-top: 1px solid var(--sama-border);
      padding-top: 1rem !important;
      border-radius: 0 !important;
    }

    .content { padding: 2rem 1.75rem 4rem; max-width: 1100px; }

    @media (max-width: 860px) {
      .admin { grid-template-columns: 1fr; }
      .sidebar {
        border-right: 0;
        border-bottom: 1px solid var(--sama-border);
        flex-direction: row;
        overflow-x: auto;
        padding: 0.6rem;
        gap: 0.3rem;
      }
      .sidebar__title, .sidebar__exit { display: none; }
      .sidebar nav { flex-direction: row; }
      .sidebar a { white-space: nowrap; }
      .content { padding: 1.5rem 1rem 3rem; }
    }
  `,
})
export class AdminShell {
  protected readonly auth = inject(AuthService);

  protected readonly links: readonly AdminLink[] = [
    { path: '', label: 'Overview', icon: '◫', permissions: ['catalog:product:write', 'ordering:order:read.any', 'identity:user:read'] },
    { path: 'products', label: 'Products', icon: '❑', permissions: ['catalog:product:write'] },
    { path: 'categories', label: 'Categories', icon: '⌸', permissions: ['catalog:category:write'] },
    { path: 'stock', label: 'Stock', icon: '⛁', permissions: ['inventory:stock:read'] },
    { path: 'orders', label: 'Orders', icon: '✇', permissions: ['ordering:order:read.any'] },
    { path: 'payments', label: 'Payments', icon: '⛃', permissions: ['payment:payment:read.any'] },
    { path: 'users', label: 'Users', icon: '☖', permissions: ['identity:user:read'] },
  ];
}
