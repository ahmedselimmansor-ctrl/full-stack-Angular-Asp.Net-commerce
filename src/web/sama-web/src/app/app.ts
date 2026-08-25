import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { APP_CONFIG } from './core/app-config';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';
import { BasketBadge } from './shared/basket-badge';
import { NoticeStack } from './shared/notice-stack';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, BasketBadge, NoticeStack],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly auth = inject(AuthService);
  protected readonly notifications = inject(NotificationService);

  protected readonly storeName = inject(APP_CONFIG).storeName;
  protected readonly isAuthenticated = this.auth.isAuthenticated;
  protected readonly displayName = this.auth.displayName;
  protected readonly year = new Date().getFullYear();

  protected readonly initials = computed(() => {
    const name = this.displayName();
    if (!name) return '';

    return name
      .split(/\s+/)
      .slice(0, 2)
      .map((part) => part.charAt(0).toUpperCase())
      .join('');
  });

  protected async signOut(): Promise<void> {
    await this.auth.logout();
  }
}
