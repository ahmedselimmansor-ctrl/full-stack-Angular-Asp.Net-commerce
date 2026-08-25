# Testing the storefront

```bash
npm run test
```

## A caveat about the folder name

Test discovery fails if the checkout path contains parentheses.

The Angular unit-test builder hands Vitest a list of **absolute** paths as `include` patterns.
Vitest treats those as globs, and `(` and `)` are extglob syntax — so a path like
`…/Sama-E-comerce-Angular-(.Net)/…` never matches itself. The symptom is misleading: the
builder prints the spec files it found, and Vitest then reports `No test files found`.

Nothing in the repository can fix this, because the path is above it. Rename the checkout:

```bash
mv "$HOME/Sama-E-comerce-Angular-(.Net)" "$HOME/sama-commerce"
```

CI is unaffected — GitHub checks out to `/home/runner/work/sama-commerce/sama-commerce`.
