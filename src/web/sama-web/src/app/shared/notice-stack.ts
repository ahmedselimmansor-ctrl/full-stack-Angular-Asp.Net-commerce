import { Component, inject } from '@angular/core';
import { NotificationService } from '../core/notification.service';

@Component({
  selector: 'app-notice-stack',
  template: `
    <!-- assertive: these announce failures the user must act on, so they interrupt rather
         than wait for a pause in screen-reader output. -->
    <div class="stack" role="status" aria-live="assertive">
      @for (notice of notifications.current(); track notice.id) {
        <div class="notice notice--{{ notice.kind }}">
          <span>{{ notice.message }}</span>
          <button type="button" (click)="notifications.dismiss(notice.id)" aria-label="Dismiss">×</button>
        </div>
      }
    </div>
  `,
  styles: `
    .stack {
      position: fixed;
      /* Toasts sit on the trailing edge, which is the left one in Arabic. */
      inset-block-end: 1rem;
      inset-inline-end: 1rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      z-index: 60;
      max-inline-size: min(420px, calc(100vw - 2rem));
    }
    .notice {
      display: flex;
      align-items: flex-start;
      gap: 0.75rem;
      padding: 0.7rem 0.9rem;
      border-radius: var(--sama-radius-sm);
      border: 1px solid var(--line);
      background: var(--surface);
      box-shadow: var(--sama-shadow);
      font-size: 0.9rem;
    }
    .notice--error { border-inline-start: 3px solid var(--critical); }
    .notice--success { border-inline-start: 3px solid var(--positive); }
    .notice--info { border-inline-start: 3px solid var(--accent); }
    button {
      background: none; border: 0; cursor: pointer;
      color: var(--text-soft); font-size: 1.1rem; line-height: 1; padding: 0;
    }
  `,
})
export class NoticeStack {
  protected readonly notifications = inject(NotificationService);
}
