import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';
import { ApiError } from '../../core/interceptors/error.interceptor';

@Component({
  selector: 'app-sign-in',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <div class="page narrow">
      <div class="card">
        <h1>{{ 'auth.signIn.title' | t }}</h1>
        <p class="muted">{{ 'auth.signIn.lede' | t }}</p>

        <form (submit)="submit($event)" novalidate>
          <label class="field">
            <span>{{ 'auth.email' | t }}</span>
            <input type="email" name="email" [(ngModel)]="email" required
                   autocomplete="email" [disabled]="busy()" />
          </label>

          <label class="field">
            <span>{{ 'auth.password' | t }}</span>
            <input type="password" name="password" [(ngModel)]="password" required
                   autocomplete="current-password" [disabled]="busy()" />
          </label>

          @if (error()) { <p class="error-text" role="alert">{{ error() }}</p> }

          <button type="submit" class="btn full" [disabled]="busy()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
        </form>

        <p class="alt muted">
          New here? <a routerLink="/register">Create an account</a>
        </p>
      </div>
    </div>
  `,
  styles: `
    .narrow { max-width: 420px; }
    .full { width: 100%; margin-top: 0.5rem; }
    .alt { margin: 1.25rem 0 0; text-align: center; font-size: 0.9rem; }
  `,
})
export class SignInPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.login(this.email.trim(), this.password);

      // Only ever a path within this app: honouring an absolute URL here would turn the
      // sign-in page into an open redirect that phishers could point anywhere.
      const target = this.route.snapshot.queryParamMap.get('returnUrl');
      await this.router.navigateByUrl(target?.startsWith('/') ? target : '/');
    } catch (caught: unknown) {
      this.error.set(
        caught instanceof ApiError && caught.status === 401
          ? 'That email address or password is incorrect.'
          : caught instanceof ApiError
            ? caught.message
            : 'Something went wrong. Please try again.',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
