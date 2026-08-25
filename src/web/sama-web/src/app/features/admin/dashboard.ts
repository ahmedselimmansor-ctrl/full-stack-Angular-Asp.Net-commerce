import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import { LocaleService, MoneyPipe, LocalDatePipe, TranslatePipe, type TranslationKey } from '../../core/i18n';
import { Skeleton } from '../../shared/ui/skeleton';
import { orderStatusChip, paymentStatusChip, StatusChip } from '../../shared/ui/status-chip';
import { TrendChart, type TrendPoint } from './charts/trend-chart';
import { BreakdownBar, type BreakdownSlice } from './charts/breakdown-bar';
import type { OrderSummary, PaymentView, StockLevel } from '../../core/models';

interface Metric {
  readonly label: TranslationKey;
  readonly value: string;
  readonly hint: string;
  readonly link?: string;
  readonly tone?: 'good' | 'warn' | 'bad';
}

/** A day's worth of orders, keyed by local date. */
const DAYS = 14;

/**
 * Back-office overview.
 *
 * Built from the same endpoints the individual sections use rather than a bespoke
 * reporting API. A dedicated stats endpoint would be faster, but it would also be
 * a second definition of "how many orders are there" that can drift from the
 * first, and a dashboard that disagrees with the orders page is worse than a slow
 * one.
 */
@Component({
  selector: 'app-admin-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink, Skeleton, StatusChip, TrendChart, BreakdownBar,
    MoneyPipe, LocalDatePipe, TranslatePipe,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class AdminDashboard {
  private readonly admin = inject(AdminService);
  protected readonly locale = inject(LocaleService);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  private readonly orders = signal<readonly OrderSummary[]>([]);
  private readonly payments = signal<readonly PaymentView[]>([]);
  private readonly lowStock = signal<readonly StockLevel[]>([]);

  protected readonly currency = computed(() => this.orders()[0]?.currency ?? 'EGP');

  protected readonly orderStatusChip = orderStatusChip;
  protected readonly paymentStatusChip = paymentStatusChip;

  /**
   * Cancelled orders are excluded from revenue. Counting them would make a bad
   * week look like a good one, which is the opposite of what this figure is for.
   */
  private readonly earning = computed(() =>
    this.orders().filter((order) => order.status !== 'Cancelled'),
  );

  protected readonly revenue = computed(() =>
    this.earning().reduce((sum, order) => sum + order.total, 0),
  );

  protected readonly averageOrder = computed(() => {
    const earning = this.earning();
    return earning.length ? this.revenue() / earning.length : 0;
  });

  protected readonly failedPayments = computed(() =>
    this.payments().filter((payment) => payment.status === 'Failed'),
  );

  protected readonly criticalStock = computed(() =>
    this.lowStock().filter((item) => item.belowReorderLevel),
  );

  protected readonly allClear = computed(
    () => this.failedPayments().length === 0 && this.criticalStock().length === 0,
  );

  protected readonly recent = computed(() => this.orders().slice(0, 6));

  protected readonly metrics = computed<readonly Metric[]>(() => [
    {
      label: 'admin.overview.revenue',
      value: this.locale.formatMoney(this.revenue(), this.currency()),
      hint: this.locale.t('admin.overview.last30'),
    },
    {
      label: 'admin.overview.orders',
      value: this.locale.formatNumber(this.earning().length),
      hint: this.locale.t('admin.overview.last30'),
      link: 'orders',
    },
    {
      label: 'admin.overview.averageOrder',
      value: this.locale.formatMoney(this.averageOrder(), this.currency()),
      hint: this.locale.t('admin.overview.last30'),
    },
    {
      label: 'admin.overview.lowStock',
      value: this.locale.formatNumber(this.criticalStock().length),
      hint: this.locale.t('admin.overview.reorderHint'),
      link: 'stock',
      tone: this.criticalStock().length > 0 ? 'warn' : 'good',
    },
  ]);

  /** Revenue bucketed by local day, with empty days kept so gaps stay visible. */
  protected readonly trend = computed<readonly TrendPoint[]>(() => {
    const totals = new Map<string, number>();

    for (const order of this.earning()) {
      const key = new Date(order.createdAt).toISOString().slice(0, 10);
      totals.set(key, (totals.get(key) ?? 0) + order.total);
    }

    const today = new Date();

    return Array.from({ length: DAYS }, (_, index) => {
      const date = new Date(today);
      date.setDate(today.getDate() - (DAYS - 1 - index));
      const iso = date.toISOString().slice(0, 10);

      return {
        iso,
        // Day and short month only: fourteen full dates will not fit an axis.
        label: new Intl.DateTimeFormat(this.locale.locale() === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB', {
          day: 'numeric',
          month: 'short',
        }).format(date),
        value: totals.get(iso) ?? 0,
      };
    });
  });

  protected readonly statusBreakdown = computed<readonly BreakdownSlice[]>(() => {
    const counts = new Map<string, number>();
    for (const order of this.orders()) {
      counts.set(order.status, (counts.get(order.status) ?? 0) + 1);
    }

    const tones: Record<string, BreakdownSlice['tone']> = {
      Confirmed: 'good',
      Shipped: 'accent',
      Pending: 'neutral',
      AwaitingPayment: 'warn',
      Cancelled: 'bad',
    };

    return [...counts.entries()].map(([status, value]) => ({
      label: status,
      value,
      tone: tones[status] ?? 'neutral',
    }));
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);

    // Fetched together but settled independently: a manager without payment
    // permission still gets the order figures instead of an empty dashboard.
    const [orders, payments, stock] = await Promise.allSettled([
      this.admin.orders(1, 100),
      this.admin.payments(1, 50),
      this.admin.stock({ pageSize: 100, lowOnly: true }),
    ]);

    if (orders.status === 'fulfilled') this.orders.set(orders.value.items);
    if (payments.status === 'fulfilled') this.payments.set(payments.value.items);
    if (stock.status === 'fulfilled') this.lowStock.set(stock.value.items);

    this.failed.set(orders.status === 'rejected');
    this.loading.set(false);
  }
}
