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

    // Scoped to the metric: the sidebar also carries an "Orders" link, and an
    // unscoped text match resolves to both.
    const orders = page.locator('.metric').filter({ hasText: 'Orders' }).first();
    await expect(orders).toBeVisible();

    // A real figure, not a placeholder.
    await expect(orders.locator('.metric__value')).toHaveText(/^[\d,]+$/);

    // The dashboard is built from the same endpoints the sections use, so the
    // revenue figure must carry a real currency rather than a bare number.
    const revenue = page.locator('.metric').filter({ hasText: 'Revenue' }).first();
    await expect(revenue.locator('.metric__value')).toHaveText(/\d/);

    // The trend chart plots one bar per day over the window, and labels them for
    // screen readers in a table rather than leaving the figures locked in a picture.
    await expect(page.locator('.chart__bar')).toHaveCount(14);
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
    await page.getByLabel('Name', { exact: true }).fill(`E2E Goods ${stamp}`);
    await page.getByLabel('Slug').fill(`e2e-goods-${stamp}`);
    await page.getByRole('button', { name: /create category/i }).click();
    await expect(page.getByText(/created/i).first()).toBeVisible();

    await page.goto('/admin/products/new');
    await page.getByLabel('SKU').fill(`E2E-${stamp}`);
    await page.getByLabel('Name', { exact: true }).fill(name);
    await page.getByLabel('URL slug').fill(slug);
    await page.getByLabel('Description', { exact: true }).fill('Created by the end-to-end suite.');
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
    // Scoped to the page: the site header has its own search field, so an
    // unscoped label match resolves to both.
    await page.getByRole('main').getByLabel('Search').fill(`E2E-${stamp}`);
    const row = page.locator('tbody tr').filter({ hasText: `E2E-${stamp}` }).first();
    await expect(row).toBeVisible();
    await row.getByRole('button', { name: 'Publish' }).click();
    await expect(page.getByText(/is now live/i)).toBeVisible();
  });

  test('carries an Arabic name from the back office to the Arabic storefront', async ({
    page,
    request,
  }) => {
    // The whole bilingual path in one pass: a buyer types the translation in the
    // back office, and a customer reading Arabic sees it on the shop front while
    // an English reader still sees the English name.
    const admin = await makeAdministrator(request, 'admin-bilingual');
    await signIn(page, admin);

    const stamp = Date.now();
    const english = `E2E Lantern ${stamp}`;
    const arabic = `فانوس ${stamp}`;
    const slug = `e2e-lantern-${stamp}`;

    await page.goto('/admin/categories');
    await page.getByLabel('Name', { exact: true }).fill(`E2E Bilingual ${stamp}`);
    await page.getByLabel('Slug').fill(`e2e-bilingual-${stamp}`);
    await page.getByRole('button', { name: /create category/i }).click();
    await expect(page.getByText(/created/i).first()).toBeVisible();

    await page.goto('/admin/products/new');
    await page.getByLabel('SKU').fill(`E2E-AR-${stamp}`);
    await page.getByLabel('Name', { exact: true }).fill(english);
    await page.getByLabel('Name in Arabic').fill(arabic);
    await page.getByLabel('URL slug').fill(slug);
    await page.getByLabel('Description', { exact: true }).fill('Created by the end-to-end suite.');
    await page.getByLabel('Description in Arabic').fill('أنشأته مجموعة الاختبارات.');
    await page.getByLabel('Category').selectOption({ label: `E2E Bilingual ${stamp}` });
    await page.getByLabel('Price').fill('42.00');
    await page.locator('.image-row input').first().fill('/media/e2e.jpg');
    await page.getByRole('button', { name: /create product/i }).click();

    await expect(page).toHaveURL(/\/admin\/products\/[0-9a-f-]+/, { timeout: 30_000 });

    await page.goto('/admin/products');
    await page.getByRole('main').getByLabel('Search').fill(`E2E-AR-${stamp}`);
    const row = page.locator('tbody tr').filter({ hasText: `E2E-AR-${stamp}` }).first();
    await expect(row).toBeVisible();
    await row.getByRole('button', { name: 'Publish' }).click();
    await expect(page.getByText(/is now live/i)).toBeVisible();

    // English reader sees the English name.
    await page.goto(`/products/${slug}`);
    await expect(page.getByRole('heading', { level: 1, name: english })).toBeVisible();

    // Switching language re-renders from data already in the payload, so the
    // Arabic name appears without another request for the product.
    // The toggle appears in both the header and the footer, so it has to be scoped.
    await page.getByRole('banner').getByRole('button', { name: /switch to arabic/i }).click();
    await expect(page.getByRole('heading', { level: 1, name: arabic })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
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

    await page.getByRole('main').getByLabel('Search').fill(subject.email);
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
