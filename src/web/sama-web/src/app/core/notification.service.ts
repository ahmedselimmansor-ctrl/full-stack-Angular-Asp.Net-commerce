import { Injectable, signal } from '@angular/core';

export type NoticeKind = 'error' | 'success' | 'info';

export interface Notice {
  readonly id: number;
  readonly kind: NoticeKind;
  readonly message: string;
}

/** Transient messages surfaced in one place, so nothing fails silently. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;
  private readonly notices = signal<readonly Notice[]>([]);

  readonly current = this.notices.asReadonly();

  error(message: string): void {
    this.push('error', message, 8000);
  }

  success(message: string): void {
    this.push('success', message, 4000);
  }

  info(message: string): void {
    this.push('info', message, 4000);
  }

  dismiss(id: number): void {
    this.notices.update((all) => all.filter((n) => n.id !== id));
  }

  private push(kind: NoticeKind, message: string, ttlMs: number): void {
    const notice: Notice = { id: this.nextId++, kind, message };
    this.notices.update((all) => [...all, notice]);

    setTimeout(() => this.dismiss(notice.id), ttlMs);
  }
}
