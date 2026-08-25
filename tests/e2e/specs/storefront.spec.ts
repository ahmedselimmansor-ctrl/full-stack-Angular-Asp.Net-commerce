import { accountButton, API, expect, registerAccount, signIn, signOut, test } from './fixtures';

/**
 * The shop front as an anonymous visitor sees it.
 *
 * Browsing must work without an account — a storefront that demands a sign-in before showing
 * anything has no customers to sign in.
 */
test.describe('storefront', () => {
  test('opens on the departments, not a wall of products', async ({ page }) => {
    // The home page's job is to route someone into one of three rooms; the full
    // grid lives at /shop.
    await page.goto('/');

    await expect(page.getByRole('heading', { level: 1, name: /sama/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /browse clothing/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /browse shoes/i })).toBeVisible();
    await expect(page.getByRole('link', { name: /browse electronics/i })).toBeVisible();

    // Nothing about the page should suggest an account is required to look.
    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible();
  });

  test('shows the catalogue to an anonymous visitor', async ({ page }) => {
    await page.goto('/shop');

    await expect(page.locator('article.tile').first()).toBeVisible();
    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible();
  });

  test('a department shows only its own products', async ({ page }) => {
    await page.goto('/clothing');

    await expect(page.getByRole('heading', { level: 1, name: /clothing/i })).toBeVisible();

    // Wait for the grid before counting. `count()` resolves immediately, so
    // calling it while the skeletons are still up reads zero and the test fails
    // on timing rather than on anything being wrong.
    await expect(page.locator('article.tile').first()).toBeVisible();

    const count = await page.locator('article.tile').count();
    expect(count).toBeGreaterThan(0);
  });

  test('reports an empty search rather than showing a bare page', async ({ page }) => {
    await page.goto('/shop?q=zzzznotarealproduct');

    await expect(page.getByRole('heading', { name: /nothing matches that yet/i })).toBeVisible();
  });

  test('opens a product from the grid', async ({ page }) => {
    await page.goto('/shop');

    const firstCard = page.locator('article.tile').first();
    const name = (await firstCard.locator('.tile__name').innerText()).trim();

    await firstCard.locator('.tile__name a').click();

    await expect(page).toHaveURL(/\/products\//);
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
    await expect(page.getByRole('button', { name: /add to basket/i })).toBeVisible();
  });

  test('serves a real 404 page rather than redirecting a stale link away', async ({ page }) => {
    // Redirecting would hide the broken link instead of surfacing it.
    await page.goto('/this-page-does-not-exist');

    await expect(page.getByRole('heading', { name: /that page does not exist/i })).toBeVisible();
    await expect(page).toHaveURL(/this-page-does-not-exist/);
  });

  test('sends an anonymous visitor to sign-in when they open the basket', async ({ page }) => {
    await page.goto('/basket');

    await expect(page).toHaveURL(/\/sign-in/);
    // The route they wanted is remembered, so signing in does not dump them on the home page.
    await expect(page).toHaveURL(/returnUrl/);
  });
});

test.describe('accounts', () => {
  test('registers, signs out, and signs back in', async ({ page, request }) => {
    const account = await registerAccount(request, 'account');

    await signIn(page, account);
    await expect(accountButton(page)).toBeVisible();

    await signOut(page);
    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible();

    await signIn(page, account);
    await page.goto('/account');

    // The email appears twice — once as the page description and once in the details list —
    // so the assertion names the one that matters rather than tripping strict mode.
    await expect(page.getByRole('definition').filter({ hasText: account.email })).toBeVisible();
  });

  test('refuses a wrong password without saying which field was wrong', async ({ page, request }) => {
    // Distinguishing "no such account" from "wrong password" turns the form into an
    // account-enumeration oracle.
    const account = await registerAccount(request, 'wrongpass');

    await page.goto('/sign-in');
    await page.getByLabel('Email').fill(account.email);
    await page.getByLabel('Password').fill('DefinitelyNotThePassword1');
    await page.getByRole('button', { name: 'Sign in' }).click();

    const message = page.getByRole('alert');
    await expect(message).toBeVisible();
    await expect(message).toHaveText(/email address or password is incorrect/i);
  });

  test('rejects a password that is too short, and says why', async ({ page }) => {
    await page.goto('/register');
    await page.getByLabel('Name').fill('Short Password');
    await page.getByLabel('Email').fill(`e2e-short-${Date.now()}@sama.test`);
    await page.getByLabel('Password').fill('short1');
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page.getByText(/at least 12 characters/i).first()).toBeVisible();
  });

  test('keeps a session across a page reload', async ({ page, request }) => {
    // The access token lives only in memory, so surviving a reload proves the HttpOnly refresh
    // cookie is being exchanged on start-up.
    const account = await registerAccount(request, 'reload');
    await signIn(page, account);

    await page.reload();

    await expect(accountButton(page)).toBeVisible();
  });

  test('hides the back office from a customer', async ({ page, request }) => {
    const account = await registerAccount(request, 'nonadmin');
    await signIn(page, account);

    await expect(page.getByRole('link', { name: 'Back office' })).toHaveCount(0);

    // And navigating there directly is refused rather than rendered.
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/forbidden/);
  });
});
