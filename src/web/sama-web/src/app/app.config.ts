import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  type ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { routes } from './app.routes';
import { AuthService } from './core/auth.service';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { SamaTitleStrategy } from './core/title.strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // Signals drive every piece of state in this app, so zone.js has nothing left to do.
    provideZonelessChangeDetection(),

    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),

    provideHttpClient(
      withFetch(),
      // Order matters: the error interceptor wraps the auth one, so a 401 that the auth
      // interceptor successfully renews and replays never surfaces as a user-visible error.
      withInterceptors([errorInterceptor, authInterceptor]),
    ),

    // Titles come from translation keys so the tab reads in the page's language.
    { provide: TitleStrategy, useClass: SamaTitleStrategy },

    // Recovers a session from the refresh cookie before the first route renders, so a reload
    // does not briefly show the signed-out shell to someone who is signed in.
    provideAppInitializer(() => inject(AuthService).restore()),
  ],
};
