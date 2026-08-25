import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink],
  template: `
    <div class="sama-page narrow">
      <div class="sama-card">
        <h1>Not permitted</h1>
        <p class="sama-muted">Your account does not have access to that area.</p>
        <a class="sama-button" routerLink="/">Back to the shop</a>
      </div>
    </div>
  `,
  styles: `
    .narrow { max-width: 460px; }
    .sama-card { text-align: center; padding: 2.5rem; }
    .sama-button { margin-top: 1rem; }
  `,
})
export class ForbiddenPage {}
