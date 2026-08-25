import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';
import { ApiError } from '../../core/interceptors/error.interceptor';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <div class="page narrow">
      <div class="card">
        <h1>{{ 'auth.register.title' | t }}</h1>

        <form (submit)="submit($event)" novalidate>
          <label class="field">
            <span>{{ 'auth.displayName' | t }}</span>
            <input name="displayName" [(ngModel)]="displayName" required
                   autocomplete="name" [disabled]="busy()" />
            @if (fieldError('DisplayName'); as message) { <span class="error-text">{{ message }}</span> }
          </label>

          <label class="field">
            <span>{{ 'auth.email' | t }}</span>
            <input type="email" name="email" [(ngModel)]="email" required
                   autocomplete="email" [disabled]="busy()" />
            @if (fieldError('Email'); as message) { <span class="error-text">{{ message }}</span> }
          </label>

          <label class="field">
            <span>{{ 'auth.password' | t }}</span>
            <input type="password" name="password" [(ngModel)]="password" required
                   autocomplete="new-password" [disabled]="busy()" />
            <span class="hint muted">At least 12 characters, including a letter and a digit.</span>
            @if (fieldError('Password'); as message) { <span class="error-text">{{ message }}</span> }
          </label>

          @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }

          <button type="submit" class="btn full" [disabled]="busy()">
            {{ busy() ? 'Creating…' : 'Create account' }}
          </button>
        </form>

        <p class="alt muted">
          Already have an account? <a routerLink="/sign-in">Sign in</a>
        </p>
      </div>
    </div>
  `,
  styles: `
    .narrow { max-width: 440px; }
    .full { width: 100%; margin-top: 0.5rem; }
    .alt { margin: 1.25rem 0 0; text-align: center; font-size: 0.9rem; }
    .hint { display: block; font-size: 0.78rem; margin-top: 0.25rem; }
  `,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected displayName = '';
  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]> | null>(null);

  /** Surfaces the API's per-field validation next to the field it belongs to. */
  protected fieldError(field: string): string | null {
    return this.fieldErrors()?.[field]?.[0] ?? null;
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(null);

    try {
      await this.auth.register(this.email.trim(), this.password, this.displayName.trim());
      await this.router.navigateByUrl('/');
    } catch (caught: unknown) {
      if (caught instanceof ApiError) {
        this.fieldErrors.set(caught.fieldErrors);
        this.error.set(caught.fieldErrors ? null : caught.message);
      } else {
        this.error.set('Something went wrong. Please try again.');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
