import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from '../../core/app-config';
import { AuthService } from '../../core/auth.service';
import { LocaleService, TranslatePipe } from '../../core/i18n';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ApiError } from '../../core/interceptors/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { PageHeader } from '../../shared/ui/page-header';

@Component({
  selector: 'app-account',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, PageHeader, HasPermissionDirective, TranslatePipe],
  template: `
    <div class="page narrow">
      <app-page-header [title]="'account.title' | t" [description]="auth.currentUser()?.email ?? ''" />

      @if (auth.currentUser(); as profile) {
        <section class="card">
          <h2>{{ 'account.profile' | t }}</h2>
          <dl class="details">
            <dt>{{ 'account.name' | t }}</dt><dd>{{ profile.displayName }}</dd>
            <dt>{{ 'account.email' | t }}</dt><dd>{{ profile.email }}</dd>
            <dt>{{ 'account.emailConfirmed' | t }}</dt>
            <dd>{{ (profile.emailConfirmed ? 'account.confirmedYes' : 'account.confirmedNo') | t }}</dd>
            <dt>{{ 'account.roles' | t }}</dt>
            <dd>
              @for (role of profile.roles; track role) {
                <span class="tag">{{ label(role) }}</span>
              }
            </dd>
          </dl>

          <div class="links">
            <a class="btn btn--ghost" routerLink="/orders">{{ 'orders.title' | t }}</a>
            <a *samaHasPermission="adminPermissions" class="btn btn--ghost" routerLink="/admin">
              {{ 'admin.title' | t }}
            </a>
          </div>
        </section>

        <section class="card">
          <h2>{{ 'account.changePassword' | t }}</h2>
          <p class="muted">{{ 'account.passwordSignsOut' | t }}</p>

          <form (submit)="changePassword($event)" novalidate>
            <label class="field">
              <span>{{ 'account.currentPassword' | t }}</span>
              <input type="password" name="current" [(ngModel)]="currentPassword"
                     autocomplete="current-password" [disabled]="busy()" required />
              @if (fieldError('CurrentPassword'); as m) { <span class="error-text">{{ m }}</span> }
            </label>

            <label class="field">
              <span>{{ 'account.newPassword' | t }}</span>
              <input type="password" name="next" [(ngModel)]="newPassword"
                     autocomplete="new-password" [disabled]="busy()" required />
              <span class="hint muted">{{ 'account.passwordHint' | t }}</span>
              @if (fieldError('NewPassword'); as m) { <span class="error-text">{{ m }}</span> }
            </label>

            @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }

            <button type="submit" class="btn" [disabled]="busy()">
              {{ (busy() ? 'account.updating' : 'account.changePassword') | t }}
            </button>
          </form>
        </section>

        <section class="card">
          <h2>{{ 'account.sessions' | t }}</h2>
          <p class="muted">{{ 'account.sessionsCopy' | t }}</p>
          <button type="button" class="btn btn--danger" [disabled]="busy()"
                  (click)="signOutEverywhere()">
            {{ 'account.signOutAll' | t }}
          </button>
        </section>
      } @else {
        <div class="card">
          <p class="muted">{{ 'account.profileFailed' | t }}</p>
        </div>
      }
    </div>
  `,
  styles: `
    .narrow { max-inline-size: 640px; padding-block: clamp(1.5rem, 4vw, 2.5rem); }
    .card { margin-block-end: 1.25rem; }
    .card h2 { font-size: 1.05rem; margin-block-end: 0.5rem; }
    .details { display: grid; grid-template-columns: 150px minmax(0, 1fr); gap: 0.4rem 1rem; margin: 0 0 1.25rem; }
    .details dt { color: var(--text-soft); font-size: 0.88rem; }
    .details dd { margin: 0; }
    .tag {
      display: inline-block; padding: 0.1rem 0.5rem; margin-inline-end: 0.3rem;
      border-radius: var(--sama-radius-pill); background: var(--accent-wash); color: var(--accent);
      font-size: 0.76rem; font-weight: 600;
    }
    .links { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .links a { text-decoration: none; }
    .hint { display: block; font-size: 0.78rem; margin-block-start: 0.25rem; }
    @media (max-width: 560px) { .details { grid-template-columns: 1fr; gap: 0.1rem 0; } }
  `,
})
export class AccountPage {
  protected readonly locale = inject(LocaleService);
  private readonly http = inject(HttpClient);
  private readonly base = inject(APP_CONFIG).apiBaseUrl;
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  protected readonly auth = inject(AuthService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]> | null>(null);

  protected readonly adminPermissions = [
    'catalog:product:write',
    'ordering:order:read.any',
    'identity:user:read',
    'inventory:stock:read',
  ];

  protected currentPassword = '';
  protected newPassword = '';

  protected label(role: string): string {
    return role.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
  }

  protected fieldError(field: string): string | null {
    return this.fieldErrors()?.[field]?.[0] ?? null;
  }

  protected async changePassword(event: Event): Promise<void> {
    event.preventDefault();

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(null);

    try {
      await firstValueFrom(
        this.http.post(`${this.base}/api/v1/auth/change-password`, {
          currentPassword: this.currentPassword,
          newPassword: this.newPassword,
        }),
      );

      // The server has just revoked every session including this one, so there is nothing to
      // stay signed in with.
      this.notifications.success(this.locale.t('notify.passwordChanged'));
      await this.auth.logout();
      await this.router.navigateByUrl('/sign-in');
    } catch (caught: unknown) {
      if (caught instanceof ApiError) {
        this.fieldErrors.set(caught.fieldErrors);
        this.error.set(caught.fieldErrors ? null : caught.message);
      } else {
        this.error.set('Your password could not be changed.');
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async signOutEverywhere(): Promise<void> {
    this.busy.set(true);

    try {
      await firstValueFrom(this.http.post(`${this.base}/api/v1/auth/logout-all`, {}));
      this.notifications.success(this.locale.t('notify.signedOutEverywhere'));
      await this.auth.logout();
      await this.router.navigateByUrl('/sign-in');
    } catch {
      this.notifications.error(this.locale.t('notify.signOutEverywhereFailed'));
    } finally {
      this.busy.set(false);
    }
  }
}
