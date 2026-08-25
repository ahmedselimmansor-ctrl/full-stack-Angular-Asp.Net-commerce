import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from '../../core/app-config';
import { AuthService } from '../../core/auth.service';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ApiError } from '../../core/interceptors/error.interceptor';
import { NotificationService } from '../../core/notification.service';
import { PageHeader } from '../../shared/ui/page-header';

@Component({
  selector: 'app-account',
  imports: [FormsModule, RouterLink, PageHeader, HasPermissionDirective],
  template: `
    <div class="sama-page narrow">
      <app-page-header title="Your account" [description]="auth.currentUser()?.email ?? ''" />

      @if (auth.currentUser(); as profile) {
        <section class="sama-card">
          <h2>Profile</h2>
          <dl class="details">
            <dt>Name</dt><dd>{{ profile.displayName }}</dd>
            <dt>Email</dt><dd>{{ profile.email }}</dd>
            <dt>Email confirmed</dt><dd>{{ profile.emailConfirmed ? 'Yes' : 'Not yet' }}</dd>
            <dt>Roles</dt>
            <dd>
              @for (role of profile.roles; track role) {
                <span class="tag">{{ label(role) }}</span>
              }
            </dd>
          </dl>

          <div class="links">
            <a class="sama-button sama-button--ghost" routerLink="/orders">Your orders</a>
            <a *samaHasPermission="adminPermissions" class="sama-button sama-button--ghost" routerLink="/admin">
              Back office
            </a>
          </div>
        </section>

        <section class="sama-card">
          <h2>Change password</h2>
          <p class="sama-muted">
            Changing your password signs you out on every other device, including this one.
          </p>

          <form (submit)="changePassword($event)" novalidate>
            <label class="sama-field">
              <span>Current password</span>
              <input type="password" name="current" [(ngModel)]="currentPassword"
                     autocomplete="current-password" [disabled]="busy()" required />
              @if (fieldError('CurrentPassword'); as m) { <span class="sama-error">{{ m }}</span> }
            </label>

            <label class="sama-field">
              <span>New password</span>
              <input type="password" name="next" [(ngModel)]="newPassword"
                     autocomplete="new-password" [disabled]="busy()" required />
              <span class="hint sama-muted">At least 12 characters, including a letter and a digit.</span>
              @if (fieldError('NewPassword'); as m) { <span class="sama-error">{{ m }}</span> }
            </label>

            @if (error()) { <p class="sama-error" role="alert">{{ error() }}</p> }

            <button type="submit" class="sama-button" [disabled]="busy()">
              {{ busy() ? 'Updating…' : 'Change password' }}
            </button>
          </form>
        </section>

        <section class="sama-card">
          <h2>Sessions</h2>
          <p class="sama-muted">
            Signing out everywhere revokes every refresh token and rotates your security stamp,
            so tokens already issued stop working immediately rather than at their next expiry.
          </p>
          <button type="button" class="sama-button sama-button--danger" [disabled]="busy()"
                  (click)="signOutEverywhere()">
            Sign out on all devices
          </button>
        </section>
      } @else {
        <div class="sama-card">
          <p class="sama-muted">Your profile could not be loaded.</p>
        </div>
      }
    </div>
  `,
  styles: `
    .narrow { max-width: 640px; }
    .sama-card { margin-bottom: 1.25rem; }
    .sama-card h2 { font-size: 1.05rem; margin-bottom: 0.5rem; }
    .details { display: grid; grid-template-columns: 150px 1fr; gap: 0.4rem 1rem; margin: 0 0 1.25rem; }
    .details dt { color: var(--sama-ink-soft); font-size: 0.88rem; }
    .details dd { margin: 0; }
    .tag {
      display: inline-block; padding: 0.1rem 0.5rem; margin-right: 0.3rem;
      border-radius: 999px; background: var(--sama-accent-soft); color: var(--sama-accent);
      font-size: 0.76rem; font-weight: 600;
    }
    .links { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .links a { text-decoration: none; }
    .hint { display: block; font-size: 0.78rem; margin-top: 0.25rem; }
    @media (max-width: 560px) { .details { grid-template-columns: 1fr; gap: 0.1rem 0; } }
  `,
})
export class AccountPage {
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
      this.notifications.success('Password changed. Please sign in again.');
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
      this.notifications.success('Signed out on every device.');
      await this.auth.logout();
      await this.router.navigateByUrl('/sign-in');
    } catch {
      this.notifications.error('Could not sign out everywhere. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
