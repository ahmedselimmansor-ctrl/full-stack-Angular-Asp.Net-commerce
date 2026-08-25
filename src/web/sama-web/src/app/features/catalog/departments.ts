import type { TranslationKey } from '../../core/i18n';

/**
 * The three departments the shop is organised around.
 *
 * `slug` matches the catalogue category slug the API knows about, so a
 * department page can resolve its category without a hard-coded id. The imagery
 * is fixed editorial art direction rather than a product photo: a department
 * should not change its face because one item sold out.
 */
export interface Department {
  readonly path: string;
  readonly slug: string;
  readonly title: TranslationKey;
  readonly lede: TranslationKey;
  readonly note: TranslationKey;
  readonly blurb: TranslationKey;
  readonly image: string;
  readonly arabic: string;
  readonly english: string;
}

const unsplash = (id: string, width: number) =>
  `https://images.unsplash.com/${id}?auto=format&fit=crop&q=80&w=${width}`;

export const DEPARTMENTS: readonly Department[] = [
  {
    path: '/clothing',
    slug: 'clothing',
    title: 'clothing.title',
    lede: 'clothing.lede',
    note: 'clothing.note',
    blurb: 'home.departments.clothing.copy',
    image: unsplash('photo-1539533018447-63fcce2678e3', 900),
    arabic: 'ملابس',
    english: 'Clothing',
  },
  {
    path: '/shoes',
    slug: 'shoes',
    title: 'shoes.title',
    lede: 'shoes.lede',
    note: 'shoes.note',
    blurb: 'home.departments.shoes.copy',
    image: unsplash('photo-1595950653106-6c9ebd614d3a', 900),
    arabic: 'أحذية',
    english: 'Shoes',
  },
  {
    path: '/electronics',
    slug: 'electronics',
    title: 'electronics.title',
    lede: 'electronics.lede',
    note: 'electronics.note',
    blurb: 'home.departments.electronics.copy',
    image: unsplash('photo-1505740420928-5e560c06d30e', 900),
    arabic: 'إلكترونيات',
    english: 'Electronics',
  },
];

export const departmentBySlug = (slug: string): Department | undefined =>
  DEPARTMENTS.find((department) => department.slug === slug);
