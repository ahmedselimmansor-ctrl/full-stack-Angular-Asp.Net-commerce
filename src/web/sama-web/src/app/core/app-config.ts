import { InjectionToken } from '@angular/core';

/**
 * Settings resolved at runtime rather than compiled in.
 *
 * The bundle is built once and promoted through dev, staging and production unchanged; only
 * `config.json` differs per environment. Baking the API URL in at build time would mean three
 * different artefacts, and the one you tested would not be the one you shipped.
 */
export interface AppConfig {
  readonly apiBaseUrl: string;
  readonly storeName: string;
  readonly defaultCurrency: string;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');
