import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, TranslatePipe],
  template: `
    <div class="page narrow">
      <div class="card">
        <h1>{{ 'error.forbidden.title' | t }}</h1>
        <p class="muted">{{ 'error.forbidden.copy' | t }}</p>
        <a class="btn" routerLink="/">{{ 'error.notFound.action' | t }}</a>
      </div>
    </div>
  `,
  styles: `
    .narrow { max-width: 460px; }
    .card { text-align: center; padding: 2.5rem; }
    .btn { margin-top: 1rem; }
  `,
})
export class ForbiddenPage {}
