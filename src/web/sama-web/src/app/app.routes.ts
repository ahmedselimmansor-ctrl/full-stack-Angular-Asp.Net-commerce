import type { Routes } from '@angular/router';
import { authGuard, permissionGuard } from './core/guards';

/**
 * Every feature is lazily loaded. The storefront's first paint should not pay for the
 * checkout, order-history or back-office bundles that most visitors never reach — the admin
 * area in particular is dead weight for all but a handful of users.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/catalog/product-list').then(m => m.ProductListPage),
    title: 'Sama — Shop',
  },
  {
    path: 'products/:slug',
    loadComponent: () => import('./features/catalog/product-detail').then(m => m.ProductDetailPage),
  },
  {
    path: 'sign-in',
    loadComponent: () => import('./features/auth/sign-in').then(m => m.SignInPage),
    title: 'Sign in — Sama',
  },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register').then(m => m.RegisterPage),
    title: 'Create an account — Sama',
  },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/account-page').then(m => m.AccountPage),
    title: 'Your account — Sama',
  },
  {
    path: 'basket',
    canActivate: [authGuard],
    loadComponent: () => import('./features/basket/basket-page').then(m => m.BasketPage),
    title: 'Your basket — Sama',
  },
  {
    path: 'checkout',
    canActivate: [authGuard],
    loadComponent: () => import('./features/checkout/checkout-page').then(m => m.CheckoutPage),
    title: 'Checkout — Sama',
  },
  {
    path: 'orders',
    canActivate: [authGuard],
    loadComponent: () => import('./features/orders/order-list').then(m => m.OrderListPage),
    title: 'Your orders — Sama',
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
        title: 'Back office — Sama',
      },
      {
        path: 'products',
        canActivate: [permissionGuard],
        data: { permissions: ['catalog:product:write'] },
        loadComponent: () => import('./features/admin/product-table').then(m => m.AdminProductTable),
        title: 'Products — Sama',
      },
      {
        // Before the :id route, or "new" would be read as a product id.
        path: 'products/new',
        canActivate: [permissionGuard],
        data: { permissions: ['catalog:product:write'] },
        loadComponent: () => import('./features/admin/product-form').then(m => m.AdminProductForm),
        title: 'New product — Sama',
      },
      {
        path: 'products/:id',
        canActivate: [permissionGuard],
        data: { permissions: ['catalog:product:write'] },
        loadComponent: () => import('./features/admin/product-form').then(m => m.AdminProductForm),
        title: 'Edit product — Sama',
      },
      {
        path: 'categories',
        canActivate: [permissionGuard],
        data: { permissions: ['catalog:category:write'] },
        loadComponent: () => import('./features/admin/category-table').then(m => m.AdminCategoryTable),
        title: 'Categories — Sama',
      },
      {
        path: 'stock',
        canActivate: [permissionGuard],
        data: { permissions: ['inventory:stock:read'] },
        loadComponent: () => import('./features/admin/stock-table').then(m => m.AdminStockTable),
        title: 'Stock — Sama',
      },
      {
        path: 'orders',
        canActivate: [permissionGuard],
        data: { permissions: ['ordering:order:read.any'] },
        loadComponent: () => import('./features/admin/order-table').then(m => m.AdminOrderTable),
        title: 'Orders — Sama',
      },
      {
        path: 'payments',
        canActivate: [permissionGuard],
        data: { permissions: ['payment:payment:read.any'] },
        loadComponent: () => import('./features/admin/payment-table').then(m => m.AdminPaymentTable),
        title: 'Payments — Sama',
      },
      {
        path: 'users',
        canActivate: [permissionGuard],
        data: { permissions: ['identity:user:read'] },
        loadComponent: () => import('./features/admin/user-table').then(m => m.AdminUserTable),
        title: 'Users — Sama',
      },
    ],
  },

  {
    path: 'forbidden',
    loadComponent: () => import('./features/auth/forbidden').then(m => m.ForbiddenPage),
    title: 'Not permitted — Sama',
  },
  {
    // A real page, not a redirect: redirecting hides stale links instead of surfacing them.
    path: '**',
    loadComponent: () => import('./features/errors/not-found').then(m => m.NotFoundPage),
    title: 'Page not found — Sama',
  },
];
