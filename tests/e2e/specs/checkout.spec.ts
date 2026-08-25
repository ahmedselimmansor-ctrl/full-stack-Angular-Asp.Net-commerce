import { accountButton, API, expect, provisionProduct, registerAccount, registerAdministrator, signIn, signOut, test } from './fixtures';

/**
 * The journey that has to work: browse, add to basket, check out, and watch the saga settle.
 *
 * Everything else in this repository exists to make this path correct — the outbox, the
 * compensating actions, the idempotency key. This is the test that proves it end to end.
 */
test.describe('checkout', () => {
  test('takes a customer from the shop front to a confirmed order', async ({ page, request }) => {
    const admin = await registerAdministrator(request, 'checkout-admin');
    const product = await provisionProduct(request, admin, 'checkout');

    const account = await registerAccount(request, 'checkout');
    await signIn(page, account);

    // --- add something to the basket ------------------------------------------------------
    // Straight to the provisioned product rather than "whatever is first in the grid", which
    // is how this test kept landing on the fixture priced to trigger a decline.
    await page.goto(`/products/${product.slug}`);
    await expect(page.getByRole('heading', { level: 1, name: product.name })).toBeVisible();

    await page.getByRole('button', { name: /add to basket/i }).click();
    await expect(page.getByText(/added to your basket/i)).toBeVisible();

    await page.goto('/basket');
    await expect(page.getByRole('heading', { name: /your basket/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /go to checkout/i })).toBeVisible();

    // --- check out --------------------------------------------------------------------------
    await page.getByRole('link', { name: /go to checkout/i }).click();
    await expect(page).toHaveURL(/\/checkout/);

    await page.getByLabel('Street address').fill('12 Corniche El Nil');
    await page.getByLabel('City').fill('Cairo');
    await page.getByLabel('Postal code').fill('11511');
    await page.getByRole('button', { name: /place order/i }).click();

    // Checkout answers 202 with the saga still running, so the customer lands on the order.
    await expect(page).toHaveURL(/\/orders\/[0-9a-f-]+/, { timeout: 30_000 });
    await expect(page.getByRole('heading', { name: /^SAMA-/ })).toBeVisible();

    // --- the saga settles without the page being reloaded -----------------------------------
    // The order screen polls while the status is unsettled, so this proves stock was reserved,
    // payment was taken, and both replies made it back through Pub/Sub. Scoped to the status
    // chip: "Cancelled" also appears in the cancellation banner, and an unscoped text match
    // would resolve to two elements.
    const status = page.locator('app-status-chip');
    await expect(status).toHaveText(/Confirmed|Cancelled/, { timeout: 60_000 });
    await expect(status).toHaveText('Confirmed');
  });

  test('empties the basket once the order is confirmed', async ({ page, request }) => {
    // Clearing on confirmation rather than at checkout is deliberate: an order that fails
    // should leave the basket intact rather than making the customer rebuild it.
    const admin = await registerAdministrator(request, 'basketclear-admin');
    const product = await provisionProduct(request, admin, 'basketclear');

    const account = await registerAccount(request, 'basketclear');
    await signIn(page, account);

    await page.goto(`/products/${product.slug}`);
    await page.getByRole('button', { name: /add to basket/i }).click();
    await expect(page.getByText(/added to your basket/i)).toBeVisible();

    await page.goto('/checkout');
    await page.getByLabel('Street address').fill('12 Corniche El Nil');
    await page.getByLabel('City').fill('Cairo');
    await page.getByLabel('Postal code').fill('11511');
    await page.getByRole('button', { name: /place order/i }).click();

    await expect(page).toHaveURL(/\/orders\//, { timeout: 30_000 });
    await expect(page.locator('app-status-chip')).toHaveText('Confirmed', { timeout: 60_000 });

    await page.goto('/basket');
    await expect(page.getByText(/basket is empty|nothing here yet/i)).toBeVisible({ timeout: 30_000 });
  });

  test('refuses to check out an empty basket', async ({ page, request }) => {
    const account = await registerAccount(request, 'emptybasket');
    await signIn(page, account);

    await page.goto('/checkout');

    await expect(page.getByText(/basket is empty/i)).toBeVisible();
  });

  test('shows a customer only their own orders', async ({ page, request }) => {
    // Row-level access control, seen from the outside: the API narrows the listing inside the
    // SQL, so another customer's order is not merely hidden by the UI.
    const admin = await registerAdministrator(request, 'scope-admin');
    const product = await provisionProduct(request, admin, 'scope');

    const buyer = await registerAccount(request, 'buyer');
    const stranger = await registerAccount(request, 'stranger');

    await signIn(page, buyer);
    await page.goto(`/products/${product.slug}`);
    await page.getByRole('button', { name: /add to basket/i }).click();
    await expect(page.getByText(/added to your basket/i)).toBeVisible();

    await page.goto('/checkout');
    await page.getByLabel('Street address').fill('12 Corniche El Nil');
    await page.getByLabel('City').fill('Cairo');
    await page.getByLabel('Postal code').fill('11511');
    await page.getByRole('button', { name: /place order/i }).click();
    await expect(page).toHaveURL(/\/orders\//, { timeout: 30_000 });

    const orderUrl = page.url();

    await signOut(page);
    await signIn(page, stranger);

    // Their own list is empty...
    await page.goto('/orders');
    await expect(page.getByText(/no orders yet/i)).toBeVisible();

    // ...and the other customer's order is not found, rather than forbidden. A 403 would
    // confirm it exists and turn id probing into an order-count oracle.
    await page.goto(orderUrl);
    await expect(page.getByText(/could not find that order/i)).toBeVisible();
  });

  test('a repeated checkout with the same key does not place a second order', async ({ request }) => {
    // Driven through the API rather than the UI, because the browser deliberately generates a
    // fresh key per attempt — this is the retry a flaky mobile connection produces.
    const admin = await registerAdministrator(request, 'idempotent-admin');
    const product = await provisionProduct(request, admin, 'idempotent');
    const account = await registerAccount(request, 'idempotent');
    const productId = product.id;

    await request.post(`${API}/api/v1/basket/items`, {
      headers: { Authorization: `Bearer ${account.accessToken}` },
      data: { productId, quantity: 1 },
    });

    const key = `e2e-${Date.now()}`;
    const payload = {
      headers: { Authorization: `Bearer ${account.accessToken}`, 'Idempotency-Key': key },
      data: { shippingAddress: { line1: '1 Nile St', city: 'Cairo', postcode: '11511', country: 'EG' } },
    };

    const first = await request.post(`${API}/api/v1/orders`, payload);
    const second = await request.post(`${API}/api/v1/orders`, payload);

    expect(first.status()).toBe(202);
    expect((await first.json()).id).toBe((await second.json()).id);

    const orders = await (await request.get(`${API}/api/v1/orders`, {
      headers: { Authorization: `Bearer ${account.accessToken}` },
    })).json();

    expect(orders.totalCount).toBe(1);
  });

  test('rejects a checkout with no idempotency key', async ({ request }) => {
    // A client with no key has no safe retry story, and allowing it turns a dropped response
    // into a double charge.
    const account = await registerAccount(request, 'nokey');

    const response = await request.post(`${API}/api/v1/orders`, {
      headers: { Authorization: `Bearer ${account.accessToken}` },
      data: { shippingAddress: { line1: '1 Nile St', city: 'Cairo', postcode: '11511', country: 'EG' } },
    });

    expect(response.status()).toBe(400);
    expect(await response.text()).toContain('Idempotency-Key');
  });
});
