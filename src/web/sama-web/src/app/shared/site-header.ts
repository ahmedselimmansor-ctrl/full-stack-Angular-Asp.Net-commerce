import { ChangeDetectionStrategy, Component, HostListener, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { HasPermissionDirective } from '../core/has-permission.directive';
import { LocaleService, TranslatePipe } from '../core/i18n';
import { BasketBadge } from './basket-badge';
import { ThemeToggle } from './theme-toggle';
import { LocaleToggle } from './locale-toggle';

@Component({
  selector: 'app-site-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, BasketBadge, ThemeToggle, LocaleToggle, HasPermissionDirective, TranslatePipe],
  templateUrl: './site-header.html',
  styleUrl: './site-header.scss',
})
export class SiteHeader {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly locale = inject(LocaleService);

  protected readonly isAuthenticated = this.auth.isAuthenticated;
  protected readonly displayName = this.auth.displayName;

  protected readonly menuOpen = signal(false);
  protected readonly accountOpen = signal(false);
  protected readonly searchOpen = signal(false);
  protected readonly condensed = signal(false);
  protected readonly query = signal('');

  /** Any one of these means the account has something to do in the back office. */
  protected readonly adminPermissions = [
    'catalog:product:write',
    'catalog:category:write',
    'inventory:stock:read',
    'ordering:order:read.any',
    'payment:payment:read.any',
    'identity:user:read',
  ];

  protected readonly departments = [
    { path: '/clothing', label: 'nav.clothing' },
    { path: '/shoes', label: 'nav.shoes' },
    { path: '/electronics', label: 'nav.electronics' },
    { path: '/shop', label: 'nav.shop' },
  ] as const;

  protected readonly initials = computed(() => {
    const name = this.displayName();
    if (!name) return '';

    return name
      .split(/\s+/)
      .slice(0, 2)
      .map((part) => part.charAt(0).toUpperCase())
      .join('');
  });

  @HostListener('window:scroll')
  protected onScroll(): void {
    this.condensed.set(window.scrollY > 24);
  }

  /**
   * Escape closes whichever overlay is open. Anything that covers the page needs
   * a keyboard way out, or a keyboard user is trapped behind it.
   */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.menuOpen.set(false);
    this.searchOpen.set(false);
    this.accountOpen.set(false);
  }

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  protected toggleAccount(): void {
    this.accountOpen.update((open) => !open);
  }

  protected closeAccount(): void {
    this.accountOpen.set(false);
  }

  protected toggleSearch(): void {
    this.searchOpen.update((open) => !open);
  }

  protected search(event: Event): void {
    event.preventDefault();
    const term = this.query().trim();

    // An empty search means "show me everything", not "search for nothing" — the
    // service charges the tight search rate-limit bucket only when q is present.
    void this.router.navigate(['/shop'], { queryParams: term ? { q: term } : {} });
    this.searchOpen.set(false);
    this.menuOpen.set(false);
  }

  protected async signOut(): Promise<void> {
    this.closeMenu();
    this.closeAccount();
    await this.auth.logout();
  }
}
