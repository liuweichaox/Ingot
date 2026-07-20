FROM node:22-alpine AS build
WORKDIR /src/apps/docs-site

COPY apps/docs-site/package.json apps/docs-site/package-lock.json ./
RUN npm ci

COPY apps/docs-site/ ./
COPY docs/ /src/docs/
COPY apps/website/public/brand/ /src/apps/website/public/brand/
RUN npm run build

FROM nginx:1.29-alpine AS runtime
COPY deploy/nginx-static.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/apps/docs-site/out/ /usr/share/nginx/html/

EXPOSE 80
