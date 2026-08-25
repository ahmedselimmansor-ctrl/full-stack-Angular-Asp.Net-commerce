import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { LocaleService, TranslatePipe, type TranslationKey } from '../../core/i18n';
import { ThemeToggle } from '../../shared/theme-toggle';
import { LocaleToggle } from '../../shared/locale-toggle';

interface AdminLink {
  readonly path: string;
  readonly label: TranslationKey;
  readonly icon: string;
  /** Any one of these grants access to the section. */
  readonly permissions: readonly string[];
}

/**
 * Back-office layout.
 *
 * Navigation entries are filtered by permission, so a catalogue manager does not
 * see an Orders link they would only be refused at. That is presentation, not
 * protection — the API enforces the same permissions, and hiding a link protects
 * nothing on its own.
 */
@Component({
  selector: 'app-admin-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, HasPermissionDirective,
    TranslatePipe, ThemeToggle, LocaleToggle,
  ],
  templateUrl: './admin-shell.html',
  styleUrl: './admin-shell.scss',
})
export class AdminShell {
  protected readonly auth = inject(AuthService);
  protected readonly locale = inject(LocaleService);

  protected readonly navOpen = signal(false);

  protected readonly links: readonly AdminLink[] = [
    {
      path: '',
      label: 'admin.nav.overview',
      permissions: ['catalog:product:write', 'ordering:order:read.any', 'identity:user:read'],
      icon: '<rect x="2.5" y="2.5" width="6" height="6" rx="1.4"/><rect x="11.5" y="2.5" width="6" height="6" rx="1.4"/><rect x="2.5" y="11.5" width="6" height="6" rx="1.4"/><rect x="11.5" y="11.5" width="6" height="6" rx="1.4"/>',
    },
    {
      path: 'products',
      label: 'admin.nav.products',
      permissions: ['catalog:product:write'],
      icon: '<path d="M10 2.5l7 3.7v7.6l-7 3.7-7-3.7V6.2z"/><path d="M3 6.2l7 3.8 7-3.8M10 10v7.5"/>',
    },
    {
      path: 'categories',
      label: 'admin.nav.categories',
      permissions: ['catalog:category:write'],
      icon: '<path d="M3 5.5h5l1.5 2H17v7a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 3 14.5z"/>',
    },
    {
      path: 'stock',
      label: 'admin.nav.stock',
      permissions: ['inventory:stock:read'],
      icon: '<ellipse cx="10" cy="5" rx="6.5" ry="2.5"/><path d="M3.5 5v10c0 1.4 2.9 2.5 6.5 2.5s6.5-1.1 6.5-2.5V5M3.5 10c0 1.4 2.9 2.5 6.5 2.5s6.5-1.1 6.5-2.5"/>',
    },
    {
      path: 'orders',
      label: 'admin.nav.orders',
      permissions: ['ordering:order:read.any'],
      icon: '<path d="M4 3.5h9l3 3v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-12a1 1 0 0 1 1-1z"/><path d="M12.5 3.5v3.5H16M6 10h8M6 13h5"/>',
    },
    {
      path: 'payments',
      label: 'admin.nav.payments',
      permissions: ['payment:payment:read.any'],
      icon: '<rect x="2.5" y="4.5" width="15" height="11" rx="1.6"/><path d="M2.5 8.5h15M5.5 12.5h3"/>',
    },
    {
      path: 'users',
      label: 'admin.nav.users',
      permissions: ['identity:user:read'],
      icon: '<circle cx="10" cy="7" r="3"/><path d="M4 16.5a6 6 0 0 1 12 0"/>',
    },
  ];

  protected toggleNav(): void {
    this.navOpen.update((open) => !open);
  }

  protected closeNav(): void {
    this.navOpen.set(false);
  }
}
