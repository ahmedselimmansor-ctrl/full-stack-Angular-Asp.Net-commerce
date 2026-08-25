import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, type ActivatedRouteSnapshot, type RouterStateSnapshot } from '@angular/router';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { APP_CONFIG } from './app-config';
import { AuthService } from './auth.service';
import { authGuard, permissionGuard } from './guards';

describe('route guards', () => {
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: 'https://api.test', storeName: 'Sama', defaultCurrency: 'USD' } },
      ],
    });

    auth = TestBed.inject(AuthService);
  });

  const state = (url: string) => ({ url }) as RouterStateSnapshot;
  const route = (permissions?: string[]) =>
    ({ data: permissions ? { permissions } : {} }) as unknown as ActivatedRouteSnapshot;

  function run<T>(fn: () => T): T {
    return TestBed.runInInjectionContext(fn);
  }

  it('lets a signed-in user through', () => {
    vi.spyOn(auth, 'isAuthenticated').mockReturnValue(true as never);

    expect(run(() => authGuard(route(), state('/orders')))).toBe(true);
  });

  it('redirects an anonymous visitor to sign-in, remembering where they were going', () => {
    vi.spyOn(auth, 'isAuthenticated').mockReturnValue(false as never);

    const result = run(() => authGuard(route(), state('/checkout')));

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree))
      .toContain('returnUrl=%2Fcheckout');
  });

  it('admits a user holding any one of the required permissions', () => {
    vi.spyOn(auth, 'isAuthenticated').mockReturnValue(true as never);
    vi.spyOn(auth, 'hasAnyPermission').mockReturnValue(true);

    expect(run(() => permissionGuard(route(['catalog:product:write']), state('/admin')))).toBe(true);
  });

  it('sends a signed-in user without the permission to the forbidden page, not to sign-in', () => {
    // Bouncing them to sign-in would suggest their session is the problem, and they would sign
    // in again to exactly the same refusal.
    vi.spyOn(auth, 'isAuthenticated').mockReturnValue(true as never);
    vi.spyOn(auth, 'hasAnyPermission').mockReturnValue(false);

    const result = run(() => permissionGuard(route(['identity:user:read']), state('/admin/users')));

    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/forbidden');
  });

  it('admits anyone when a route names no permissions', () => {
    vi.spyOn(auth, 'isAuthenticated').mockReturnValue(true as never);

    expect(run(() => permissionGuard(route(), state('/admin')))).toBe(true);
  });
});
