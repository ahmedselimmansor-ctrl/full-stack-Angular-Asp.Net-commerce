import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n';

/**
 * A real page rather than a silent redirect to the home page.
 *
 * Redirecting hides the mistake: someone following a stale link ends up on the shop front
 * wondering why, and a broken internal link is never noticed at all.
 */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink, TranslatePipe],
  template: `
    <div class="page narrow">
      <div class="card">
        <p class="code" aria-hidden="true">404</p>
        <h1>{{ 'error.notFound.title' | t }}</h1>
        <p class="muted">
          The link may be out of date, or the product may have been withdrawn from sale.
        </p>
        <div class="actions">
          <a class="btn" routerLink="/">{{ 'error.notFound.action' | t }}</a>
          <a class="btn btn--ghost" routerLink="/orders">{{ 'orders.title' | t }}</a>
        </div>
      </div>
    </div>
  `,
  styles: `
    .narrow { max-width: 520px; }
    .card { text-align: center; padding: 3rem 2rem; }
    .code {
      font-family: var(--sama-display);
      font-size: 3.5rem;
      line-height: 1;
      margin: 0 0 0.5rem;
      color: var(--accent);
      opacity: 0.45;
    }
    .actions { display: flex; gap: 0.5rem; justify-content: center; margin-top: 1.5rem; flex-wrap: wrap; }
    .actions a { text-decoration: none; }
  `,
})
export class NotFoundPage {}
