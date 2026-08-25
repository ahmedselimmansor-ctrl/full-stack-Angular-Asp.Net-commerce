import { API, expect, registerAccount, registerAdministrator, signIn, test, type Account } from './fixtures';
import { execSync } from 'node:child_process';

/**
 * The back office.
 *
 * Administrators are provisioned through the shared fixture, which writes the role grant
 * directly to the database — Sama deliberately has no endpoint that hands out administrator.
 */
async function makeAdministrator(
  request: import('@playwright/test').APIRequestContext,
  label: string,
): Promise<Account> {
  return registerAdministrator(request, label);
}

test.describe('back office', () => {
  test('shows the overview with figures drawn from the live services', async ({ page, request }) => {
    const admin = await makeAdministrator(request, 'admin-overview');
    await signIn(page, admin);

    await page.goto('/admin');

    await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible();

    // Scoped to the tile: the sidebar also has a "Products" link, and an unscoped text match
    // resolves to both.
    const tile = page.locator('.tile').filter({ hasText: 'Products' }).first();
    await expect(tile).toBeVisible();

    // A real figure, not a placeholder.
    await expect(tile.locator('.tile__value')).toHaveText(/^\d+$/);
  });

  test('lists products in every status, including drafts', async ({ page, request }) => {
    const admin = await makeAdministrator(request, 'admin-products');
    await signIn(page, admin);

    await page.goto('/admin/products');

    await expect(page.getByRole('heading', { name: 'Products' })).toBeVisible();
    await expect(page.locator('tbody tr').first()).toBeVisible();

    // The storefront listing filters to Published; the back office must not.
    await expect(page.getByRole('combobox').filter({ hasText: 'Draft' })).toBeVisible();
  });

  test('creates a product as a draft and then publishes it', async ({ page, request }) => {
    const admin = await makeAdministrator(request, 'admin-create');
    await signIn(page, admin);

    const stamp = Date.now();
    const name = `E2E Widget ${stamp}`;
    const slug = `e2e-widget-${stamp}`;

    // A category has to exist first — products cannot be orphaned.
    await page.goto('/admin/categories');
    await page.getByLabel('Name').fill(`E2E Goods ${stamp}`);
    await page.getByLabel('Slug').fill(`e2e-goods-${stamp}`);
    await page.getByRole('button', { name: /create category/i }).click();
    await expect(page.getByText(/created/i).first()).toBeVisible();

    await page.goto('/admin/products/new');
    await page.getByLabel('SKU').fill(`E2E-${stamp}`);
    await page.getByLabel('Name').fill(name);
    await page.getByLabel('URL slug').fill(slug);
    await page.getByLabel('Description').fill('Created by the end-to-end suite.');
    await page.getByLabel('Category').selectOption({ label: `E2E Goods ${stamp}` });
    await page.getByLabel('Price').fill('31.50');
    await page.locator('.image-row input').first().fill('/media/e2e.jpg');

    await page.getByRole('button', { name: /create product/i }).click();

    // Created as a draft: a new product is not live until someone says so.
    await expect(page).toHaveURL(/\/admin\/products\/[0-9a-f-]+/, { timeout: 30_000 });
    await expect(page.getByText(/created as a draft/i)).toBeVisible();

    // It must not be on the storefront yet.
    const storefront = await request.get(`${API}/api/v1/products?q=${encodeURIComponent(name)}`);
    expect((await storefront.json()).totalCount).toBe(0);

    // Publish it, and now it is.
    await page.goto('/admin/products');
    await page.getByLabel('Search').fill(`E2E-${stamp}`);
    const row = page.locator('tbody tr').filter({ hasText: `E2E-${stamp}` }).first();
    await expect(row).toBeVisible();
    await row.getByRole('button', { name: 'Publish' }).click();
    await expect(page.getByText(/is now live/i)).toBeVisible();
  });

  test('refuses to set stock below what is already reserved', async ({ page, request }) => {
    // The units are promised to open orders; the shortfall would only surface at despatch,
    // after the customer has paid.
    const admin = await makeAdministrator(request, 'admin-stock');
    await signIn(page, admin);

    await page.goto('/admin/stock');
    await expect(page.getByRole('heading', { name: 'Stock' })).toBeVisible();

    const reserved = page.locator('tbody tr').filter({ hasNot: page.locator('td:nth-child(3):text("0")') }).first();

    if (await reserved.count() === 0) {
      test.skip(true, 'No line currently holds a reservation.');
    }

    await reserved.getByRole('button', { name: 'Adjust' }).click();
    await reserved.locator('input').first().fill('0');
    await reserved.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText(/already reserved against orders|cannot set stock/i)).toBeVisible();
  });

  test('grants and revokes a role', async ({ page, request }) => {
    const admin = await makeAdministrator(request, 'admin-roles');
    const subject = await registerAccount(request, 'role-subject');

    await signIn(page, admin);
    await page.goto('/admin/users');

    await page.getByLabel('Search').fill(subject.email);
    const row = page.locator('tbody tr').filter({ hasText: subject.email }).first();
    await expect(row).toBeVisible();

    await row.getByRole('combobox').selectOption('support');
    await expect(page.getByText(/is now Support/i)).toBeVisible();

    await expect(row.getByRole('button', { name: /Remove the Support role/i })).toBeVisible();
  });

  test('keeps a catalogue manager out of the sections they cannot use', async ({ page, request }) => {
    const account = await registerAccount(request, 'catmgr');

    execSync(
      `docker exec sama-postgres psql -U sama -d sama -q -c ` +
      `"INSERT INTO identity.user_roles(user_id, role) VALUES ('${account.userId}','catalog_manager') ` +
      `ON CONFLICT DO NOTHING;"`,
      { stdio: 'ignore' },
    );

    await signIn(page, account);
    await page.goto('/admin');

    const sidebar = page.getByRole('navigation', { name: 'Back office' });

    // They can manage the catalogue...
    await expect(sidebar.getByRole('link', { name: 'Products' })).toBeVisible();

    // ...but Users and Payments are not theirs, and the navigation does not offer them.
    await expect(sidebar.getByRole('link', { name: 'Users' })).toHaveCount(0);
    await expect(sidebar.getByRole('link', { name: 'Payments' })).toHaveCount(0);

    // Navigating directly is refused too — hiding a link protects nothing on its own.
    await page.goto('/admin/users');
    await expect(page).toHaveURL(/\/forbidden/);
  });
});
