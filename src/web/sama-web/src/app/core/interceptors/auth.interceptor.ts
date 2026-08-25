import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { APP_CONFIG } from '../app-config';
import { AuthService } from '../auth.service';

/** Paths that must never carry a bearer token, and must never trigger a refresh loop. */
const ANONYMOUS_PATHS = ['/api/v1/auth/login', '/api/v1/auth/register', '/api/v1/auth/refresh'];

/**
 * Attaches the access token and transparently renews it once on expiry.
 *
 * Access tokens are deliberately short-lived, so a 401 mid-session is routine rather than
 * exceptional. Renewing and replaying here keeps that invisible to the user; without it, a
 * fifteen-minute token would mean being bounced to the sign-in page every fifteen minutes.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const config = inject(APP_CONFIG);

  // Only our own API. Attaching the token to a third-party URL would leak the credential.
  const isSamaApi = request.url.startsWith(config.apiBaseUrl);
  const isAnonymous = ANONYMOUS_PATHS.some((path) => request.url.includes(path));

  const authorised =
    isSamaApi && !isAnonymous && auth.token()
      ? request.clone({ setHeaders: { Authorization: `Bearer ${auth.token()}` } })
      : request;

  return next(authorised).pipe(
    catchError((error: unknown) => {
      const isExpired =
        error instanceof HttpErrorResponse && error.status === 401 && isSamaApi && !isAnonymous;

      if (!isExpired) {
        return throwError(() => error);
      }

      // AuthService single-flights this, so a burst of parallel 401s produces one refresh.
      return from(auth.refresh()).pipe(
        switchMap((renewed) => {
          if (!renewed) {
            return throwError(() => error);
          }

          // Retried exactly once. A second 401 after a successful refresh means the request is
          // genuinely unauthorised, and retrying again would spin.
          return next(
            request.clone({ setHeaders: { Authorization: `Bearer ${auth.token()}` } }),
          );
        }),
      );
    }),
  );
};
