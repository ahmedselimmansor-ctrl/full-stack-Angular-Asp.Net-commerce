import type { Routes } from '@angular/router';
import { authGuard } from './core/guards';

/**
 * Every feature is lazily loaded. The storefront's first paint should not pay for the
 * checkout, order-history or account bundles that most visitors never reach.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/catalog/product-list').then((m) => m.ProductListPage),
    title: 'Sama — Shop',
  },
  {
    path: 'products/:slug',
    loadComponent: () => import('./features/catalog/product-detail').then((m) => m.ProductDetailPage),
  },
  {
    path: 'sign-in',
    loadComponent: () => import('./features/auth/sign-in').then((m) => m.SignInPage),
    title: 'Sign in — Sama',
  },
  {
    path: 'register',
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterPage),
    title: 'Create an account — Sama',
  },
  {
    path: 'basket',
    canActivate: [authGuard],
    loadComponent: () => import('./features/basket/basket-page').then((m) => m.BasketPage),
    title: 'Your basket — Sama',
  },
  {
    path: 'checkout',
    canActivate: [authGuard],
    loadComponent: () => import('./features/checkout/checkout-page').then((m) => m.CheckoutPage),
    title: 'Checkout — Sama',
  },
  {
    path: 'orders',
    canActivate: [authGuard],
    loadComponent: () => import('./features/orders/order-list').then((m) => m.OrderListPage),
    title: 'Your orders — Sama',
  },
  {
    path: 'orders/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/orders/order-detail').then((m) => m.OrderDetailPage),
  },
  {
    path: 'forbidden',
    loadComponent: () => import('./features/auth/forbidden').then((m) => m.ForbiddenPage),
    title: 'Not permitted — Sama',
  },
  { path: '**', redirectTo: '' },
];
