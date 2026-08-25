import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { APP_CONFIG } from './app-config';
import { AuthService } from './auth.service';

const BASE = 'https://api.test';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: BASE, storeName: 'Sama', defaultCurrency: 'USD' } },
      ],
    });

    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  /**
   * Lets pending microtasks settle.
   *
   * The service issues the profile request only after the token response resolves, so a test
   * that flushes both in one synchronous pass would try to answer a request that has not been
   * made yet.
   */
  const settle = () => new Promise(resolve => setTimeout(resolve, 0));

  async function respondToSignIn(): Promise<void> {
    http.expectOne(`${BASE}/api/v1/auth/login`).flush({
      accessToken: 'access-1',
      refreshToken: 'ignored-by-the-browser',
      tokenType: 'Bearer',
      expiresIn: 900,
    });
    await settle();
  }

  async function respondToProfile(): Promise<void> {
    http.expectOne(`${BASE}/api/v1/auth/me`).flush({
      id: 'u1',
      email: 'a@sama.test',
      displayName: 'Ahmed',
      emailConfirmed: true,
      roles: ['customer'],
      permissions: ['basket:basket:write.own', 'ordering:order:read.own'],
    });
    await settle();
  }

  /** Drives a full sign-in, answering both requests in order. */
  async function signIn(): Promise<void> {
    const pending = service.login('a@sama.test', 'password');
    await respondToSignIn();
    await respondToProfile();
    await pending;
  }

  it('starts signed out', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.token()).toBeNull();
  });

  it('holds the access token only in memory, never in web storage', async () => {
    await signIn();

    expect(service.token()).toBe('access-1');

    // The whole point of keeping it in a signal: an XSS that reads storage finds nothing,
    // so the worst it can steal is a credential that expires in minutes.
    expect(localStorage.getItem('access-1')).toBeNull();
    expect(JSON.stringify(localStorage)).not.toContain('access-1');
    expect(JSON.stringify(sessionStorage)).not.toContain('access-1');
  });

  it('exposes the signed-in profile and its permissions', async () => {
    await signIn();

    expect(service.isAuthenticated()).toBe(true);
    expect(service.displayName()).toBe('Ahmed');
    expect(service.hasPermission('basket:basket:write.own')).toBe(true);
    expect(service.hasPermission('catalog:product:write')).toBe(false);
    expect(service.hasAnyPermission('catalog:product:write', 'ordering:order:read.own')).toBe(true);
  });

  it('sends an empty body on refresh, because the token is in a cookie it cannot read', async () => {
    const refreshed = service.refresh();

    const request = http.expectOne(`${BASE}/api/v1/auth/refresh`);
    expect(request.request.body).toEqual({});
    // withCredentials is what lets the browser attach the HttpOnly refresh cookie.
    expect(request.request.withCredentials).toBe(true);

    request.flush({ accessToken: 'access-2', refreshToken: '', tokenType: 'Bearer', expiresIn: 900 });
    await settle();
    await respondToProfile();

    expect(await refreshed).toBe(true);
    expect(service.token()).toBe('access-2');
  });

  it('collapses concurrent refreshes into a single request', async () => {
    // Refresh tokens rotate and reuse is treated as theft, so two parallel refreshes would
    // revoke the whole family and sign the user out. Single-flighting is correctness here,
    // not an optimisation.
    const first = service.refresh();
    const second = service.refresh();
    const third = service.refresh();

    const requests = http.match(`${BASE}/api/v1/auth/refresh`);
    expect(requests.length).toBe(1);

    requests[0].flush({ accessToken: 'access-3', refreshToken: '', tokenType: 'Bearer', expiresIn: 900 });
    await settle();
    await respondToProfile();

    expect(await Promise.all([first, second, third])).toEqual([true, true, true]);
  });

  it('clears the session when the refresh token is rejected', async () => {
    const refreshed = service.refresh();

    http.expectOne(`${BASE}/api/v1/auth/refresh`).flush(
      { title: 'Unauthorized' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(await refreshed).toBe(false);
    expect(service.isAuthenticated()).toBe(false);
  });

  it('signs out locally even when the server call fails', async () => {
    await signIn();

    const signOut = service.logout();
    http.expectOne(`${BASE}/api/v1/auth/logout`).error(new ProgressEvent('network'));
    await settle();
    await signOut;

    // A failed round trip must not leave the UI believing the user is still signed in.
    expect(service.isAuthenticated()).toBe(false);
    expect(service.currentUser()).toBeNull();
  });
});
