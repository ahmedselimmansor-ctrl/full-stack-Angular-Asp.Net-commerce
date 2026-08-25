import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BasketStore } from '../../core/basket-store';
import type { BasketLine } from '../../core/models';
import { LocaleService, MoneyPipe, TranslatePipe } from '../../core/i18n';
import { ProductImage } from '../../shared/product-image';

@Component({
  selector: 'app-basket',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MoneyPipe, TranslatePipe, ProductImage],
  templateUrl: './basket-page.html',
  styleUrl: './basket-page.scss',
})
export class BasketPage {
  protected readonly locale = inject(LocaleService);
  protected readonly store = inject(BasketStore);
  protected readonly quantities = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20];

  constructor() {
    void this.store.refresh();
  }

  protected async changeQuantity(line: BasketLine, event: Event): Promise<void> {
    const quantity = Number((event.target as HTMLSelectElement).value);
    await this.store.setQuantity(line.productId, quantity);
  }

  protected async remove(line: BasketLine): Promise<void> {
    await this.store.remove(line.productId);
  }
}
