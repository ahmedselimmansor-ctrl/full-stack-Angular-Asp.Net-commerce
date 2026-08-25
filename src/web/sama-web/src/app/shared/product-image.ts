import { Component, computed, input, signal } from '@angular/core';

/**
 * Product imagery with a graceful fallback.
 *
 * Images are served from a Cloud Storage bucket through the CDN, independently of the API. A
 * missing or not-yet-uploaded object is a normal state — a product can be created before its
 * photography lands — and the browser's default broken-image icon looks like a fault in the
 * shop rather than a gap in the catalogue. Falling back to a lettered tile keeps the grid
 * intact either way.
 */
@Component({
  selector: 'app-product-image',
  template: `
    @if (source() && !failed()) {
      <img
        [src]="source()"
        [alt]="alt()"
        [attr.loading]="eager() ? null : 'lazy'"
        [attr.fetchpriority]="eager() ? 'high' : null"
        decoding="async"
        (error)="failed.set(true)"
      />
    } @else {
      <div class="fallback" role="img" [attr.aria-label]="alt()">{{ initial() }}</div>
    }
  `,
  styles: `
    :host { display: block; width: 100%; height: 100%; }
    img { width: 100%; height: 100%; object-fit: cover; display: block; }
    .fallback {
      width: 100%; height: 100%;
      display: grid; place-items: center;
      background: var(--sama-surface-alt);
      color: var(--sama-ink-soft);
      font-family: var(--sama-serif);
      font-size: clamp(1.25rem, 18cqi, 3rem);
      container-type: inline-size;
    }
  `,
})
export class ProductImage {
  readonly source = input<string | null>(null);
  readonly alt = input<string>('');

  /** Set on the above-the-fold hero image so it is not lazily deferred. */
  readonly eager = input(false);

  protected readonly failed = signal(false);

  protected readonly initial = computed(() => this.alt().trim().charAt(0).toUpperCase() || '·');
}
