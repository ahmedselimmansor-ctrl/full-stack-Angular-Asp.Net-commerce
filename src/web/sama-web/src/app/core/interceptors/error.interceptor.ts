import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from '../notification.service';
import type { ProblemDetails } from '../models';

/** A failure translated into something worth showing a person. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    override readonly message: string,
    readonly code: string | null,
    readonly fieldErrors: Record<string, string[]> | null,
    readonly retryAfterSeconds: number | null,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * Turns transport failures into `ApiError`, so components branch on a typed shape rather than
 * digging through `HttpErrorResponse`, and surfaces the ones a user needs to know about.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const notifications = inject(NotificationService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const problem = (error.error ?? {}) as ProblemDetails;

      // Status 0 is a network failure, a CORS rejection or an aborted request — the browser
      // withholds the detail, so a generic message is the honest one.
      const message =
        error.status === 0
          ? 'Could not reach the server. Check your connection and try again.'
          : (problem.detail ?? problem.title ?? error.message);

      const retryAfter = Number(error.headers.get('Retry-After'));

      const apiError = new ApiError(
        error.status,
        message,
        problem.code ?? null,
        problem.errors ?? null,
        Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null,
      );

      // 401 is handled by the auth interceptor (renew and replay); showing a toast for it
      // would flash an error during an otherwise invisible token renewal.
      // 404 is frequently a legitimate answer the component wants to render itself.
      if (error.status !== 401 && error.status !== 404) {
        notifications.error(
          apiError.retryAfterSeconds
            ? `${message} Try again in ${apiError.retryAfterSeconds}s.`
            : message,
        );
      }

      return throwError(() => apiError);
    }),
  );
};
