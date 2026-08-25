import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { APP_CONFIG } from '../app-config';
import { AuthService } from '../auth.service';
import { authInterceptor } from './auth.interceptor';

const BASE = 'https://api.test';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: { apiBaseUrl: BASE, storeName: 'Sama', defaultCurrency: 'USD' } },
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);

    vi.spyOn(auth, 'token').mockReturnValue('access-1');
  });

  it('attaches the bearer token to Sama API calls', () => {
    http.get(`${BASE}/api/v1/basket`).subscribe();

    const request = backend.expectOne(`${BASE}/api/v1/basket`);
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush({});
  });

  it('never attaches the token to a third-party URL', () => {
    // Sending a session credential to another origin is how a token leaks to a service that
    // had no business seeing it.
    http.get('https://analytics.example/collect').subscribe();

    const request = backend.expectOne('https://analytics.example/collect');
    expect(request.request.headers.get('Authorization')).toBeNull();
    request.flush({});
  });

  it('leaves the anonymous auth endpoints unauthenticated', () => {
    // Sending a stale token to /refresh would be pointless, and to /login actively confusing.
    for (const path of ['/api/v1/auth/login', '/api/v1/auth/register', '/api/v1/auth/refresh']) {
      http.post(`${BASE}${path}`, {}).subscribe();
      const request = backend.expectOne(`${BASE}${path}`);
      expect(request.request.headers.get('Authorization')).toBeNull();
      request.flush({});
    }
  });

  it('renews once on a 401 and replays the original request', async () => {
    const refresh = vi.spyOn(auth, 'refresh').mockImplementation(async () => {
      vi.spyOn(auth, 'token').mockReturnValue('access-2');
      return true;
    });

    const response = new Promise(resolve => http.get(`${BASE}/api/v1/orders`).subscribe(resolve));

    backend.expectOne(`${BASE}/api/v1/orders`)
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    // Flushed on a microtask so the interceptor's refresh promise has resolved.
    await Promise.resolve();

    const replayed = backend.expectOne(`${BASE}/api/v1/orders`);
    expect(replayed.request.headers.get('Authorization')).toBe('Bearer access-2');
    replayed.flush({ items: [] });

    expect(await response).toEqual({ items: [] });
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('gives up when renewal fails, rather than looping', async () => {
    vi.spyOn(auth, 'refresh').mockResolvedValue(false);

    const failed = new Promise<unknown>(resolve =>
      http.get(`${BASE}/api/v1/orders`).subscribe({ error: resolve }));

    backend.expectOne(`${BASE}/api/v1/orders`)
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    await Promise.resolve();
    backend.expectNone(`${BASE}/api/v1/orders`);

    expect(await failed).toBeTruthy();
  });

  it('does not try to renew a 403, which is a permission problem and not an expiry', async () => {
    const refresh = vi.spyOn(auth, 'refresh');

    const failed = new Promise<unknown>(resolve =>
      http.get(`${BASE}/api/v1/users`).subscribe({ error: resolve }));

    backend.expectOne(`${BASE}/api/v1/users`)
      .flush({ title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });

    await failed;
    expect(refresh).not.toHaveBeenCalled();
  });
});
