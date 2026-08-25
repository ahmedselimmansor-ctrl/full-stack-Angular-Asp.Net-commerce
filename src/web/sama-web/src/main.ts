import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { APP_CONFIG, type AppConfig } from './app/core/app-config';
import { appConfig } from './app/app.config';

/**
 * Configuration is fetched before Angular starts, so the API base URL is a deployment
 * concern rather than a build-time constant. One image, promoted unchanged through every
 * environment.
 */
async function bootstrap(): Promise<void> {
  const response = await fetch('/config.json', { cache: 'no-cache' });

  if (!response.ok) {
    throw new Error(`Could not load /config.json (HTTP ${response.status}).`);
  }

  const runtime = (await response.json()) as AppConfig;

  await bootstrapApplication(App, {
    ...appConfig,
    providers: [...appConfig.providers, { provide: APP_CONFIG, useValue: runtime }],
  });
}

bootstrap().catch((error: unknown) => {
  console.error('Sama failed to start', error);

  document.body.innerHTML =
    '<main style="font:16px system-ui;padding:3rem;text-align:center">' +
    '<h1>Sama is unavailable</h1><p>The storefront could not start. Please try again shortly.</p></main>';
});
