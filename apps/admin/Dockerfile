FROM node:24.15.0-bookworm-slim AS build
WORKDIR /app

RUN npm install -g pnpm@12.8.1

ARG VITE_API_BASE_URL=http://localhost:8080
ARG VITE_KEYCLOAK_AUTHORITY=http://localhost:8180/realms/farm-basics
ARG VITE_KEYCLOAK_CLIENT_ID=farm-basics-admin
ARG VITE_APP_URL=http://admin.localhost:8080

ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_KEYCLOAK_AUTHORITY=$VITE_KEYCLOAK_AUTHORITY \
    VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID \
    VITE_APP_URL=$VITE_APP_URL

COPY apps/admin/package.json apps/admin/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY apps/admin/ ./
RUN pnpm run build

FROM caddy:2.8.4-alpine AS runtime
COPY --from=build /app/dist /srv
COPY apps/admin/Caddyfile /etc/caddy/Caddyfile
EXPOSE 8080
