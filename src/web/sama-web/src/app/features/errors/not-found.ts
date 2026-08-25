import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * A real page rather than a silent redirect to the home page.
 *
 * Redirecting hides the mistake: someone following a stale link ends up on the shop front
 * wondering why, and a broken internal link is never noticed at all.
 */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  template: `
    <div class="sama-page narrow">
      <div class="sama-card">
        <p class="code" aria-hidden="true">404</p>
        <h1>We couldn't find that page</h1>
        <p class="sama-muted">
          The link may be out of date, or the product may have been withdrawn from sale.
        </p>
        <div class="actions">
          <a class="sama-button" routerLink="/">Back to the shop</a>
          <a class="sama-button sama-button--ghost" routerLink="/orders">Your orders</a>
        </div>
      </div>
    </div>
  `,
  styles: `
    .narrow { max-width: 520px; }
    .sama-card { text-align: center; padding: 3rem 2rem; }
    .code {
      font-family: var(--sama-serif);
      font-size: 3.5rem;
      line-height: 1;
      margin: 0 0 0.5rem;
      color: var(--sama-accent);
      opacity: 0.45;
    }
    .actions { display: flex; gap: 0.5rem; justify-content: center; margin-top: 1.5rem; flex-wrap: wrap; }
    .actions a { text-decoration: none; }
  `,
})
export class NotFoundPage {}
