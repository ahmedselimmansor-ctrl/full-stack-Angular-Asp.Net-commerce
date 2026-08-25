import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { MoneyPipe } from '../../shared/money.pipe';
import { PageHeader } from '../../shared/ui/page-header';
import { Skeleton } from '../../shared/ui/skeleton';

interface Tile {
  readonly label: string;
  readonly value: string;
  readonly hint: string;
  readonly link?: string;
  readonly tone?: 'good' | 'warn' | 'bad';
}

/**
 * Back-office overview.
 *
 * Deliberately built from the same endpoints the individual sections use, rather than a
 * bespoke stats API. A separate reporting endpoint would be faster, but it would also be a
 * second definition of "how many orders are there" that can drift from the first.
 */
@Component({
  selector: 'app-admin-dashboard',
  imports: [RouterLink, PageHeader, Skeleton, MoneyPipe],
  template: `
    <app-page-header
      title="Overview"
      [description]="'Signed in as ' + (auth.displayName() ?? 'an administrator') + '.'" />

    @if (loading()) {
      <app-skeleton [count]="2" [height]="96" />
    } @else if (failed()) {
      <div class="sama-card">
        <p class="sama-muted">Some figures could not be loaded. They will reappear on the next refresh.</p>
      </div>
    } @else {
      <div class="tiles">
        @for (tile of tiles(); track tile.label) {
          @if (tile.link) {
            <a class="tile" [routerLink]="tile.link">
              <p class="tile__label">{{ tile.label }}</p>
              <p class="tile__value" [class]="tile.tone ? 'tone-' + tile.tone : ''">{{ tile.value }}</p>
              <p class="tile__hint sama-muted">{{ tile.hint }}</p>
            </a>
          } @else {
            <div class="tile">
              <p class="tile__label">{{ tile.label }}</p>
              <p class="tile__value" [class]="tile.tone ? 'tone-' + tile.tone : ''">{{ tile.value }}</p>
              <p class="tile__hint sama-muted">{{ tile.hint }}</p>
            </div>
          }
        }
      </div>

      @if (recentRevenue() > 0) {
        <section class="sama-card revenue">
          <h2>Recent revenue</h2>
          <p class="revenue__value">{{ recentRevenue() | money: revenueCurrency() }}</p>
          <p class="sama-muted">Across the most recent {{ revenueSampleSize() }} confirmed orders.</p>
        </section>
      }
    }
  `,
  styles: `
    .tiles {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 1rem;
      margin-bottom: 1.5rem;
    }
    .tile {
      background: var(--sama-surface);
      border: 1px solid var(--sama-border);
      border-radius: var(--sama-radius);
      padding: 1.1rem 1.2rem;
      text-decoration: none;
      color: inherit;
      display: block;
    }
    a.tile:hover { border-color: var(--sama-accent); }
    .tile p { margin: 0; }
    .tile__label { font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.06em; color: var(--sama-ink-soft); }
    .tile__value { font-size: 1.9rem; font-weight: 650; font-variant-numeric: tabular-nums; line-height: 1.2; margin: 0.2rem 0; }
    .tile__hint { font-size: 0.8rem; }
    .tone-warn { color: #8a6508; }
    .tone-bad { color: var(--sama-danger); }
    .tone-good { color: var(--sama-success); }
    .revenue__value { font-size: 1.6rem; font-weight: 650; margin: 0.25rem 0; }
  `,
})
export class AdminDashboard {
  private readonly admin = inject(AdminService);
  protected readonly auth = inject(AuthService);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly tiles = signal<readonly Tile[]>([]);
  protected readonly recentRevenue = signal(0);
  protected readonly revenueCurrency = signal('USD');
  protected readonly revenueSampleSize = signal(0);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);

    // allSettled, not all: a catalogue manager holds no order permission, so some of these
    // legitimately 403. One refused call should not blank the whole page.
    const [products, drafts, orders, stock, users] = await Promise.allSettled([
      this.admin.products({ pageSize: 1 }),
      this.admin.products({ pageSize: 1, status: 'Draft' }),
      this.admin.orders(1, 50),
      this.admin.stock({ pageSize: 1, lowOnly: true }),
      this.admin.users({ pageSize: 1 }),
    ]);

    const tiles: Tile[] = [];

    if (products.status === 'fulfilled') {
      tiles.push({
        label: 'Products',
        value: String(products.value.totalCount),
        hint: 'in the catalogue',
        link: 'products',
      });
    }

    if (drafts.status === 'fulfilled' && drafts.value.totalCount > 0) {
      tiles.push({
        label: 'Unpublished',
        value: String(drafts.value.totalCount),
        hint: 'drafts not yet live',
        link: 'products',
        tone: 'warn',
      });
    }

    if (orders.status === 'fulfilled') {
      const all = orders.value.items;

      tiles.push({
        label: 'Orders',
        value: String(orders.value.totalCount),
        hint: 'placed in total',
        link: 'orders',
      });

      // An order still Pending or AwaitingPayment long after it was placed is the signal that
      // the saga has stalled — worth surfacing here rather than waiting for a customer to ask.
      const unsettled = all.filter(o => o.status === 'Pending' || o.status === 'AwaitingPayment');
      if (unsettled.length > 0) {
        tiles.push({
          label: 'In flight',
          value: String(unsettled.length),
          hint: 'orders still settling',
          link: 'orders',
          tone: unsettled.length > 5 ? 'bad' : 'warn',
        });
      }

      const confirmed = all.filter(o => o.status === 'Confirmed' || o.status === 'Shipped');
      if (confirmed.length > 0) {
        this.recentRevenue.set(confirmed.reduce((sum, o) => sum + o.total, 0));
        this.revenueCurrency.set(confirmed[0].currency);
        this.revenueSampleSize.set(confirmed.length);
      }
    }

    if (stock.status === 'fulfilled') {
      tiles.push({
        label: 'Low stock',
        value: String(stock.value.totalCount),
        hint: 'lines at or below reorder level',
        link: 'stock',
        tone: stock.value.totalCount > 0 ? 'warn' : 'good',
      });
    }

    if (users.status === 'fulfilled') {
      tiles.push({
        label: 'Customers',
        value: String(users.value.totalCount),
        hint: 'registered accounts',
        link: 'users',
      });
    }

    this.tiles.set(tiles);
    this.failed.set(tiles.length === 0);
    this.loading.set(false);
  }
}
