# 构建并托管 Ingot 官网的静态生产产物。

FROM node:22-alpine AS build
WORKDIR /app

COPY apps/website/package.json apps/website/package-lock.json ./
RUN npm ci

COPY apps/website/ ./
RUN npm run build

FROM nginx:1.29-alpine AS runtime
COPY deploy/nginx-static.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/out/ /usr/share/nginx/html/

EXPOSE 80
