# Ingot Website

Bilingual static website for Ingot, the open-source closed-loop process-optimization platform.

The home page must:

- lead with the product’s general capability: define experiments, connect observations, learn process response, recommend safely, verify, and transfer knowledge;
- remain independent of any specific equipment, production asset, material, or manufacturing process;
- link to documentation, source, quickstart, and contributing guide;
- use canonical assets from `public/brand`;
- remain statically exportable.

```bash
npm ci
npm run build
npm test
npm run lint
```

Production output is written to `out/`.
