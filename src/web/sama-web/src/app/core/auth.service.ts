import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from './app-config';
import type { TokenResponse, UserProfile } from './models';

/**
 * Holds the session and renews it.
 *
 * The access token lives in a signal and nowhere else — not `localStorage`, not a cookie the
 * page can read. Anything persisted in web storage is readable by injected script, and a
 * token that survives a reload also survives an attacker's exfiltration. Keeping it in memory
 * means the worst an XSS can steal is a credential that expires in fifteen minutes.
 *
 * The refresh token is never touched by this code at all: the API sets it as an HttpOnly
 * cookie scoped to `/api/v1/auth`, so the browser presents it on refresh and JavaScript
 * cannot read it. `restore()` recovers a session after a reload by exchanging that cookie.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(APP_CONFIG);

  private readonly accessToken = signal<string | null>(null);
  private readonly profile = signal<UserProfile | null>(null);

  /** True once a session has been established, whether by sign-in or by restoring a cookie. */
  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  readonly currentUser = this.profile.asReadonly();

  readonly displayName = computed(() => this.profile()?.displayName ?? null);

  /**
   * In-flight refresh, shared by every caller.
   *
   * Under a burst of parallel requests each 401 would otherwise start its own refresh. Because
   * refresh tokens rotate and reuse is treated as theft, the second one to land would revoke
   * the whole family and sign the user out — so single-flighting this is a correctness
   * requirement, not an optimisation.
   */
  private inFlightRefresh: Promise<boolean> | null = null;

  token(): string | null {
    return this.accessToken();
  }

  hasPermission(permission: string): boolean {
    return this.profile()?.permissions.includes(permission) ?? false;
  }

  hasAnyPermission(...permissions: readonly string[]): boolean {
    const held = this.profile()?.permissions;
    return held ? permissions.some((p) => held.includes(p)) : false;
  }

  async login(email: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<TokenResponse>(
        `${this.config.apiBaseUrl}/api/v1/auth/login`,
        { email, password },
        // Lets the browser accept the Set-Cookie carrying the refresh token.
        { withCredentials: true },
      ),
    );

    this.accessToken.set(response.accessToken);
    await this.loadProfile();
  }

  async register(email: string, password: string, displayName: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<TokenResponse>(
        `${this.config.apiBaseUrl}/api/v1/auth/register`,
        { email, password, displayName },
        { withCredentials: true },
      ),
    );

    this.accessToken.set(response.accessToken);
    await this.loadProfile();
  }

  /**
   * Rebuilds the session on application start. A reload clears the in-memory access token, but
   * the refresh cookie survives, so this is what keeps a signed-in user signed in.
   */
  async restore(): Promise<void> {
    await this.refresh();
  }

  /** Exchanges the refresh cookie for a new access token. Returns false when the session is over. */
  refresh(): Promise<boolean> {
    this.inFlightRefresh ??= this.performRefresh().finally(() => {
      this.inFlightRefresh = null;
    });

    return this.inFlightRefresh;
  }

  private async performRefresh(): Promise<boolean> {
    try {
      const response = await firstValueFrom(
        this.http.post<TokenResponse>(
          `${this.config.apiBaseUrl}/api/v1/auth/refresh`,
          // Empty body on purpose: the token travels in the cookie, which this code cannot read.
          {},
          { withCredentials: true },
        ),
      );

      this.accessToken.set(response.accessToken);
      await this.loadProfile();
      return true;
    } catch {
      this.clear();
      return false;
    }
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(
        this.http.post(`${this.config.apiBaseUrl}/api/v1/auth/logout`, {}, { withCredentials: true }),
      );
    } catch {
      // A failed round trip must not leave the UI believing the user is still signed in;
      // clearing locally is the part that matters to them.
    } finally {
      this.clear();
    }
  }

  private async loadProfile(): Promise<void> {
    const profile = await firstValueFrom(
      this.http.get<UserProfile>(`${this.config.apiBaseUrl}/api/v1/auth/me`),
    );

    this.profile.set(profile);
  }

  private clear(): void {
    this.accessToken.set(null);
    this.profile.set(null);
  }
}
