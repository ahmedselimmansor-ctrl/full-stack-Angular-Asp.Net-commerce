import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Renders its content only when the user holds one of the given permissions:
 * `<button *samaHasPermission="['catalog:product:write']">Edit</button>`
 *
 * This hides controls the user cannot use — it does not protect the operation behind them.
 * The API enforces the same permission, so removing this from the DOM changes nothing about
 * what an attacker can do.
 */
@Directive({ selector: '[samaHasPermission]' })
export class HasPermissionDirective {
  private readonly auth = inject(AuthService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  readonly samaHasPermission = input.required<readonly string[]>();

  private rendered = false;

  constructor() {
    effect(() => {
      // Reading currentUser() subscribes this effect to sign-in and sign-out, so the control
      // appears and disappears without a reload.
      this.auth.currentUser();

      const allowed = this.auth.hasAnyPermission(...this.samaHasPermission());

      if (allowed && !this.rendered) {
        this.container.createEmbeddedView(this.template);
        this.rendered = true;
      } else if (!allowed && this.rendered) {
        this.container.clear();
        this.rendered = false;
      }
    });
  }
}
