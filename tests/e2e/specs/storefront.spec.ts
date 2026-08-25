import { API, expect, registerAccount, signIn, test } from './fixtures';

/**
 * The shop front as an anonymous visitor sees it.
 *
 * Browsing must work without an account — a storefront that demands a sign-in before showing
 * anything has no customers to sign in.
 */
test.describe('storefront', () => {
  test('shows the catalogue to an anonymous visitor', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByRole('heading', { name: /everyday things/i })).toBeVisible();
    await expect(page.locator('article.card').first()).toBeVisible();

    // Nothing about the page should suggest an account is required to look.
    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible();
  });

  test('filters the catalogue by search term', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('article.card').first()).toBeVisible();

    await page.getByLabel('Search').fill('zzzznotarealproduct');

    // Debounced by 350ms, then an empty state rather than a bare page.
    await expect(page.getByText(/nothing matched/i)).toBeVisible();
  });

  test('opens a product from the grid', async ({ page }) => {
    await page.goto('/');

    const firstCard = page.locator('article.card').first();
    const name = (await firstCard.locator('.card__title').innerText()).trim();

    await firstCard.locator('.card__title a').click();

    await expect(page).toHaveURL(/\/products\//);
    await expect(page.getByRole('heading', { level: 1, name })).toBeVisible();
    await expect(page.getByRole('button', { name: /add to basket/i })).toBeVisible();
  });

  test('serves a real 404 page rather than redirecting a stale link away', async ({ page }) => {
    // Redirecting would hide the broken link instead of surfacing it.
    await page.goto('/this-page-does-not-exist');

    await expect(page.getByRole('heading', { name: /couldn.t find that page/i })).toBeVisible();
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
    await expect(page.getByRole('link', { name: /your account/i })).toBeVisible();

    await page.getByRole('button', { name: 'Sign out' }).click();
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

    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
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
