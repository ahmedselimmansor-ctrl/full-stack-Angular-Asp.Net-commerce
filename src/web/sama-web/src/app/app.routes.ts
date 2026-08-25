import type { Routes } from '@angular/router';
import { authGuard, permissionGuard } from './core/guards';

/**
 * Every feature is lazily loaded. The storefront's first paint should not pay for the
 * checkout, order-history or back-office bundles that most visitors never reach — the admin
 * area in particular is dead weight for all but a handful of users.
 */
const catalogPage = () => import('./features/catalog/catalog-page').then(m => m.CatalogPage);

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/home/home-page').then(m => m.HomePage),
    data: { titleKey: 'brand.tagline' },
  },
  {
    path: 'shop',
    loadComponent: catalogPage,
    data: { titleKey: 'catalog.title' },
  },

  // The three departments share one component; `data.department` is what makes
  // each one pin its category and show its own masthead.
  {
    path: 'clothing',
    loadComponent: catalogPage,
    data: { department: 'clothing', titleKey: 'clothing.title' },
  },
  {
    path: 'shoes',
    loadComponent: catalogPage,
    data: { department: 'shoes', titleKey: 'shoes.title' },
  },
  {
    path: 'electronics',
    loadComponent: catalogPage,
    data: { department: 'electronics', titleKey: 'electronics.title' },
  },

  {
    path: 'products/:slug',
    loadComponent: () => import('./features/catalog/product-detail').then(m => m.ProductDetailPage),
  },
  {
    path: 'sign-in',
    loadComponent: () => import('./features/auth/sign-in').then(m => m.SignInPage),
    data: { titleKey: 'auth.signIn.title' },
  },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register').then(m => m.RegisterPage),
    data: { titleKey: 'auth.register.title' },
  },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/account-page').then(m => m.AccountPage),
    data: { titleKey: 'account.title' },
  },
  {
    path: 'basket',
    canActivate: [authGuard],
    loadComponent: () => import('./features/basket/basket-page').then(m => m.BasketPage),
    data: { titleKey: 'basket.title' },
  },
  {
    path: 'checkout',
    canActivate: [authGuard],
    loadComponent: () => import('./features/checkout/checkout-page').then(m => m.CheckoutPage),
    data: { titleKey: 'checkout.title' },
  },
  {
    path: 'orders',
    canActivate: [authGuard],
    loadComponent: () => import('./features/orders/order-list').then(m => m.OrderListPage),
    data: { titleKey: 'orders.title' },
  },
  {
    path: 'orders/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/orders/order-detail').then(m => m.OrderDetailPage),
  },

  // --- Back office ---------------------------------------------------------------------
  // Guarded on the parent so a child route cannot be reached directly without the check.
  // This decides what renders; the API decides what may actually be read or changed.
  {
    path: 'admin',
    canActivate: [permissionGuard],
    data: {
      permissions: [
        'catalog:product:write',
        'catalog:category:write',
        'inventory:stock:read',
        'ordering:order:read.any',
        'payment:payment:read.any',
        'identity:user:read',
      ],
    },
    loadComponent: () => import('./features/admin/admin-shell').then(m => m.AdminShell),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/admin/dashboard').then(m => m.AdminDashboard),
        data: { titleKey: 'admin.title' },
      },
      {
        path: 'products',
        canActivate: [permissionGuard],
        loadComponent: () => import('./features/admin/product-table').then(m => m.AdminProductTable),
        data: { permissions: ['catalog:product:write'], titleKey: 'admin.products.title' },
      },
      {
        // Before the :id route, or "new" would be read as a product id.
        path: 'products/new',
        canActivate: [permissionGuard],
        data: { permissions: ['catalog:product:write'], titleKey: 'admin.products.new' },
        loadComponent: () => import('./features/admin/product-form').then(m => m.AdminProductForm),
      },
      {
        path: 'products/:id',
        canActivate: [permissionGuard],
        data: { permissions: ['catalog:product:write'], titleKey: 'admin.products.edit' },
        loadComponent: () => import('./features/admin/product-form').then(m => m.AdminProductForm),
      },
      {
        path: 'categories',
        canActivate: [permissionGuard],
        loadComponent: () => import('./features/admin/category-table').then(m => m.AdminCategoryTable),
        data: { permissions: ['catalog:category:write'], titleKey: 'admin.categories.title' },
      },
      {
        path: 'stock',
        canActivate: [permissionGuard],
        loadComponent: () => import('./features/admin/stock-table').then(m => m.AdminStockTable),
        data: { permissions: ['inventory:stock:read'], titleKey: 'admin.nav.stock' },
      },
      {
        path: 'orders',
        canActivate: [permissionGuard],
        loadComponent: () => import('./features/admin/order-table').then(m => m.AdminOrderTable),
        data: { permissions: ['ordering:order:read.any'], titleKey: 'admin.nav.orders' },
      },
      {
        path: 'payments',
        canActivate: [permissionGuard],
        loadComponent: () => import('./features/admin/payment-table').then(m => m.AdminPaymentTable),
        data: { permissions: ['payment:payment:read.any'], titleKey: 'admin.nav.payments' },
      },
      {
        path: 'users',
        canActivate: [permissionGuard],
        loadComponent: () => import('./features/admin/user-table').then(m => m.AdminUserTable),
        data: { permissions: ['identity:user:read'], titleKey: 'admin.nav.users' },
      },
    ],
  },

  {
    path: 'forbidden',
    loadComponent: () => import('./features/auth/forbidden').then(m => m.ForbiddenPage),
    data: { titleKey: 'error.forbidden.title' },
  },
  {
    // A real page, not a redirect: redirecting hides stale links instead of surfacing them.
    path: '**',
    loadComponent: () => import('./features/errors/not-found').then(m => m.NotFoundPage),
    data: { titleKey: 'error.notFound.title' },
  },
];
