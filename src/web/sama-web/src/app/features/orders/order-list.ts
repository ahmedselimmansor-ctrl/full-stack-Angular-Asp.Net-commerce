import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { OrderService } from '../../core/api.service';
import type { OrderSummary, Paged } from '../../core/models';
import { LocaleService, LocalDatePipe, MoneyPipe, TranslatePipe } from '../../core/i18n';
import { orderStatusChip, StatusChip } from '../../shared/ui/status-chip';

@Component({
  selector: 'app-order-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LocalDatePipe, MoneyPipe, StatusChip, TranslatePipe],
  template: `
    <div class="page orders-page">
      <h1>{{ 'orders.title' | t }}</h1>

      @if (loading()) {
        <p class="muted" aria-busy="true">{{ 'common.loading' | t }}</p>
      } @else if ((page()?.totalCount ?? 0) === 0) {
        <div class="card empty">
          <h2>{{ 'orders.empty.title' | t }}</h2>
          <p class="muted">{{ 'orders.empty.copy' | t }}</p>
          <a class="btn" routerLink="/shop">{{ 'basket.empty.action' | t }}</a>
        </div>
      } @else {
        <ul class="orders">
          @for (order of page()!.items; track order.id) {
            <li>
              <a class="order" [routerLink]="['/orders', order.id]">
                <div class="order__main">
                  <p class="order__number mono">{{ order.orderNumber }}</p>
                  <p class="order__meta muted">
                    {{ order.createdAt | localDate }}
                    <span aria-hidden="true">·</span>
                    {{
                      order.lineCount === 1
                        ? ('orders.lineCountOne' | t)
                        : locale.t('orders.lineCount', { count: locale.formatNumber(order.lineCount) })
                    }}
                  </p>
                </div>

                <app-status-chip [spec]="chip(order.status)" />
                <p class="order__total tabular">{{ order.total | money: order.currency }}</p>
              </a>
            </li>
          }
        </ul>

        @if (page()!.totalPages > 1) {
          <nav class="pager" [attr.aria-label]="'common.pagination' | t">
            <button
              type="button"
              class="btn btn--ghost btn--sm"
              [disabled]="!page()!.hasPrevious"
              (click)="go(page()!.page - 1)"
            >
              {{ 'common.previous' | t }}
            </button>

            <span class="muted pager__count">
              {{
                locale.t('common.page', {
                  page: locale.formatNumber(page()!.page),
                  total: locale.formatNumber(page()!.totalPages)
                })
              }}
            </span>

            <button
              type="button"
              class="btn btn--ghost btn--sm"
              [disabled]="!page()!.hasNext"
              (click)="go(page()!.page + 1)"
            >
              {{ 'common.next' | t }}
            </button>
          </nav>
        }
      }
    </div>
  `,
  styles: `
    .orders-page { padding-block: clamp(1.5rem, 4vw, 2.5rem); }
    .orders-page h1 { margin-block-end: clamp(1.25rem, 3vw, 2rem); }

    .orders {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.6rem;
    }

    .order {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto auto;
      gap: clamp(0.75rem, 2vw, 1.25rem);
      align-items: center;
      padding: 1rem;
      text-decoration: none;
      color: inherit;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--sama-radius);
      transition: border-color 140ms var(--sama-ease), transform 140ms var(--sama-ease);
    }

    .order:hover { border-color: var(--accent); transform: translateY(-1px); }
    .order p { margin: 0; }
    .order__main { min-inline-size: 0; }
    .order__number { font-weight: 600; font-size: 0.95rem; }
    .order__meta { font-size: 0.82rem; }
    .order__total { font-weight: 600; white-space: nowrap; }

    .empty {
      display: grid;
      gap: 0.5rem;
      justify-items: center;
      text-align: center;
      padding-block: clamp(2.5rem, 6vw, 4rem);
    }

    .empty h2 { margin: 0; }
    .empty p { margin: 0; max-inline-size: 40ch; }
    .empty .btn { margin-block-start: 0.75rem; }

    .pager {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 1rem;
      margin-block-start: 1.75rem;
    }

    .pager__count { font-size: 0.88rem; }

    @media (max-width: 620px) {
      .order { grid-template-columns: minmax(0, 1fr) auto; row-gap: 0.6rem; }
    }
  `,
})
export class OrderListPage {
  private readonly orders = inject(OrderService);
  protected readonly locale = inject(LocaleService);

  protected readonly page = signal<Paged<OrderSummary> | null>(null);
  protected readonly loading = signal(true);

  /** The same mapping the back office uses, so a status cannot read two ways. */
  protected readonly chip = orderStatusChip;

  constructor() {
    void this.load(1);
  }

  protected async go(page: number): Promise<void> {
    await this.load(page);
  }

  private async load(page: number): Promise<void> {
    this.loading.set(true);

    try {
      this.page.set(await this.orders.list(page));
    } finally {
      this.loading.set(false);
    }
  }
}
