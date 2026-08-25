import { test as base, expect, type Page } from '@playwright/test';
import { execSync } from 'node:child_process';

const API = process.env['SAMA_API_URL'] ?? 'http://127.0.0.1:8080';
const PASSWORD = 'CorrectHorse123Battery';

export interface Account {
  readonly email: string;
  readonly password: string;
  readonly displayName: string;
  readonly accessToken: string;
  readonly userId: string;
}

/** Decodes the subject from a JWT without verifying it — this is test code reading its own token. */
function subjectOf(accessToken: string): string {
  const payload = accessToken.split('.')[1];
  const padded = payload + '='.repeat((4 - (payload.length % 4)) % 4);
  return JSON.parse(Buffer.from(padded, 'base64url').toString()).sub as string;
}

/**
 * Registers a fresh account through the API.
 *
 * Every test gets its own, so nothing depends on state another test left behind and the suite
 * can be re-run against the same database without cleaning up first.
 */
export async function registerAccount(request: import('@playwright/test').APIRequestContext, label: string): Promise<Account> {
  const email = `e2e-${label}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@sama.test`;
  const displayName = `E2E ${label}`;

  const response = await request.post(`${API}/api/v1/auth/register`, {
    data: { email, password: PASSWORD, displayName },
  });

  if (!response.ok()) {
    throw new Error(`Could not register ${email}: ${response.status()} ${await response.text()}`);
  }

  const body = await response.json();

  return {
    email,
    password: PASSWORD,
    displayName,
    accessToken: body.accessToken,
    userId: subjectOf(body.accessToken),
  };
}

/** Signs in through the UI, which is also what exercises the form the customer uses. */
export async function signIn(page: Page, account: Account): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email').fill(account.email);
  await page.getByLabel('Password').fill(account.password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  // The account button carries the signed-in person's initials, so it only appears
  // once the session is established.
  await expect(accountButton(page)).toBeVisible();
}

/** The header's account control, which opens the menu holding Sign out. */
export function accountButton(page: Page) {
  return page.getByRole('button', { name: /^Account,/ });
}

/**
 * Signs out through the header menu.
 *
 * Sign out lives behind the account menu rather than sitting in the bar, so the
 * menu has to be opened first — the same two steps a customer takes.
 */
export async function signOut(page: Page): Promise<void> {
  await accountButton(page).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible();
}

/**
 * Registers an account and grants it the administrator role.
 *
 * The grant is written straight to the database because Sama deliberately has no endpoint
 * that hands out administrator — an API that could mint its own admin would be the obvious
 * way in. Roles are baked into a token at issue time, so a fresh sign-in follows.
 */
export async function registerAdministrator(
  request: import('@playwright/test').APIRequestContext,
  label: string,
): Promise<Account> {
  const account = await registerAccount(request, label);

  execSync(
    `docker exec sama-postgres psql -U sama -d sama -q -c ` +
    `"INSERT INTO identity.user_roles(user_id, role) VALUES ('${account.userId}','administrator') ` +
    `ON CONFLICT DO NOTHING;"`,
    { stdio: 'ignore' },
  );

  const refreshed = await request.post(`${API}/api/v1/auth/login`, {
    data: { email: account.email, password: account.password },
  });

  return { ...account, accessToken: (await refreshed.json()).accessToken };
}

export interface ProvisionedProduct {
  readonly id: string;
  readonly slug: string;
  readonly sku: string;
  readonly name: string;
  readonly price: number;
}

/**
 * Creates a published product with stock, for a test that needs something buyable.
 *
 * The price matters: the simulated payment provider declines any amount whose minor units are
 * 13, which is how the compensating path is exercised on demand. A test about the happy path
 * must not accidentally pick one of those, so the price is chosen here rather than inherited
 * from whatever the catalogue already contains.
 */
export async function provisionProduct(
  request: import('@playwright/test').APIRequestContext,
  admin: Account,
  label: string,
  options: { price?: number; stock?: number } = {},
): Promise<ProvisionedProduct> {
  const auth = { Authorization: `Bearer ${admin.accessToken}` };
  const stamp = `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;

  const category = await request.post(`${API}/api/v1/categories`, {
    headers: auth,
    data: { name: `E2E ${label} ${stamp}`, slug: `e2e-${label}-${stamp}`.toLowerCase(), sortOrder: 1 },
  });
  const categoryId = (await category.json()).id;

  const sku = `E2E-${label.toUpperCase()}-${stamp}`;
  const name = `E2E ${label} ${stamp}`;
  const slug = `e2e-${label}-product-${stamp}`.toLowerCase();
  const price = options.price ?? 24.5;

  const created = await request.post(`${API}/api/v1/products`, {
    headers: auth,
    data: {
      sku, name, slug,
      description: `Provisioned by the end-to-end suite for ${label}.`,
      categoryId, price, currency: 'USD',
      images: [{ url: '/media/e2e.jpg', altText: name, sortOrder: 0 }],
    },
  });

  if (!created.ok()) {
    throw new Error(`Could not create ${sku}: ${created.status()} ${await created.text()}`);
  }

  const { id } = await created.json();

  await request.post(`${API}/api/v1/products/${id}/publish`, { headers: auth });
  await request.post(`${API}/api/v1/stock`, {
    headers: auth,
    data: { productId: id, sku, quantityOnHand: options.stock ?? 50, reorderLevel: 1 },
  });

  return { id, slug, sku, name, price };
}

export { expect, API, PASSWORD };
export const test = base;
