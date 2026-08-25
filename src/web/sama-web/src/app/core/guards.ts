import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Requires a signed-in user, sending anyone else to sign-in with a return path.
 *
 * This is a usability guard, not a security boundary: it decides what to render, and the API
 * decides what may actually be read or changed. Every route it protects is also enforced
 * server-side, because anything the browser checks can be bypassed by the browser.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};

/** Requires at least one of the listed permissions on the route's `data.permissions`. */
export const permissionGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
  }

  const required = (route.data['permissions'] as string[] | undefined) ?? [];
  if (required.length === 0 || auth.hasAnyPermission(...required)) {
    return true;
  }

  return router.createUrlTree(['/forbidden']);
};
