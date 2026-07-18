FROM node:22-alpine AS build
WORKDIR /src/docs-site

COPY docs-site/package.json docs-site/package-lock.json ./
RUN npm ci

COPY docs-site/ ./
COPY docs/ /src/docs/
COPY site/public/brand/ /src/site/public/brand/
RUN npm run build

FROM nginx:1.29-alpine AS runtime
COPY deploy/nginx-static.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/docs-site/out/ /usr/share/nginx/html/

EXPOSE 80
