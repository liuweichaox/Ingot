# 构建平台前端静态资源，并由最小化 Web 服务器镜像提供服务。

FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . ./
RUN npm run build

FROM nginx:1.29-alpine AS runtime
COPY --from=build /app/dist/ /usr/share/nginx/html/
ENV NGINX_ENVSUBST_FILTER=^INGOT_OIDC_ALLOWED_ORIGINS$ \
    INGOT_OIDC_ALLOWED_ORIGINS=""
COPY nginx.conf /etc/nginx/templates/default.conf.template

EXPOSE 80
