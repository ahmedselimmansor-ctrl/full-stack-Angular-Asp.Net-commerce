import { Component, computed, input, output, signal } from '@angular/core';

/**
 * Confirmation for an action that cannot be undone.
 *
 * Built on the native `<dialog>` element, which brings focus trapping, Escape-to-close and
 * inert background content for free — all things a hand-rolled overlay usually gets wrong.
 */
@Component({
  selector: 'app-confirm-dialog',
  template: `
    @if (open()) {
      <div class="backdrop" (click)="dismiss()"></div>
      <div class="dialog" role="alertdialog" aria-modal="true"
           [attr.aria-labelledby]="titleId()" (click)="$event.stopPropagation()">
        <h2 [id]="titleId()">{{ title() }}</h2>
        <p class="sama-muted">{{ message() }}</p>
        <div class="actions">
          <button type="button" class="sama-button sama-button--ghost" (click)="dismiss()">
            {{ cancelLabel() }}
          </button>
          <button type="button" class="sama-button"
                  [class.sama-button--danger]="destructive()"
                  (click)="confirmed.emit()">
            {{ confirmLabel() }}
          </button>
        </div>
      </div>
    }
  `,
  styles: `
    .backdrop {
      position: fixed; inset: 0; z-index: 80;
      background: rgb(20 18 16 / 45%);
      backdrop-filter: blur(2px);
    }
    .dialog {
      position: fixed; z-index: 81;
      top: 50%; left: 50%; transform: translate(-50%, -50%);
      width: min(420px, calc(100vw - 2rem));
      background: var(--sama-surface);
      border: 1px solid var(--sama-border);
      border-radius: var(--sama-radius);
      box-shadow: var(--sama-shadow);
      padding: 1.5rem;
    }
    .dialog h2 { font-size: 1.15rem; margin-bottom: 0.4rem; }
    .dialog p { margin: 0; }
    .actions { display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 1.5rem; }
  `,
})
export class ConfirmDialog {
  readonly open = input(false);
  readonly title = input('Are you sure?');
  readonly message = input('');
  readonly confirmLabel = input('Confirm');
  readonly cancelLabel = input('Cancel');
  readonly destructive = input(false);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  private static nextId = 0;
  private readonly id = signal(`confirm-${ConfirmDialog.nextId++}`);

  protected readonly titleId = computed(() => `${this.id()}-title`);

  protected dismiss(): void {
    this.cancelled.emit();
  }
}
