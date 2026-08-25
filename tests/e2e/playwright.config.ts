import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests against a running stack.
 *
 * These drive a real browser against the real gateway, real services, real PostgreSQL and
 * real Redis. That is the point: the unit and integration suites already cover the pieces,
 * and what is left to prove is that a customer can actually get from the shop front to a
 * confirmed order across seven processes and an asynchronous saga.
 *
 * Bring the stack up first:
 *   docker compose up -d && ./scripts/run-local.sh start
 *   cd src/web/sama-web && npm start
 */
export default defineConfig({
  testDir: './specs',

  // Clears rate-limit counters first: the suite registers an account per test from one
  // address, which is exactly the pattern the registration limit exists to stop.
  globalSetup: require.resolve('./global-setup'),

  // The checkout journey waits on an asynchronous saga; the default 30s is too tight.
  timeout: 90_000,
  expect: { timeout: 15_000 },

  // Serial locally so the shared database stays predictable; CI overrides with a worker per
  // shard once the fixtures are isolated per run.
  fullyParallel: false,
  workers: 1,

  // A test that only passes on a retry is a flaky test, and forbidOnly stops a focused test
  // being merged by accident.
  retries: process.env['CI'] ? 1 : 0,
  forbidOnly: !!process.env['CI'],

  reporter: process.env['CI']
    ? [['list'], ['html', { open: 'never' }]]
    : [['list']],

  use: {
    baseURL: process.env['SAMA_WEB_URL'] ?? 'http://127.0.0.1:4200',

    // Kept only for failures — traces are large, and a passing run has nothing to explain.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',

    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },

  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
