# Web image (ADR 0029): the Vite build served by unprivileged nginx on port 8080. Build context: web/.
#   docker build -t rd-web web
# Base images are pinned by digest (Dependabot's docker ecosystem bumps tag + digest together).

# Node 26 = web/.nvmrc. The guard below fails the build if the two ever drift apart.
FROM node:26-alpine@sha256:c6ad9e9c1d80f6eb607c26b4b0a132ff7a2344fad1c78ea3baa2a7d4c8de6d94 AS build
WORKDIR /web
COPY .nvmrc package.json package-lock.json ./
RUN test "$(node -p 'process.versions.node.split(".")[0]')" = "$(cat .nvmrc)" && npm ci
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.30-alpine@sha256:f5116ed4606f3eb015dd4a8e28cb9b351476dc0ad4bc6e9f93b19cec130cdf03 AS final
USER root
# Our server block replaces the image's default one (same file name), and snippets/ holds the header include.
RUN rm -f /etc/nginx/conf.d/default.conf /usr/share/nginx/html/*.html /usr/share/nginx/html/*.ico \
    && mkdir -p /etc/nginx/conf.d/snippets \
    && chown -R nginx:root /etc/nginx/conf.d
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY nginx/security-headers.conf.template /etc/nginx/templates/snippets/security-headers.conf.template
COPY --chmod=0755 nginx/15-resolver.envsh /docker-entrypoint.d/15-resolver.envsh
COPY --from=build /web/dist /usr/share/nginx/html

# Runtime settings (the entrypoint renders the templates from these on every start; ADR 0029 §5).
#   API_UPSTREAM    internal API address; Bicep sets the API's internal ingress FQDN
#   CSP_ENTRA_HOST  https://<tenant subdomain>.ciamlogin.com, empty while the deployment is guests-only
#   CSP_MAP_HOSTS   hosts allowed to serve map tiles, sprites and glyphs (ADR 0033 §5). The default is the MapLibre
#                   fallback only; Bicep adds the Azure Maps hosts when Azure Maps display is enabled.
#   CSP_STYLE_HASHES  sha256 of the <style> elements injected at runtime (Sonner's toast CSS, react-remove-scroll's lock
#                   style); style-src stays free of 'unsafe-inline'. A dependency bump that changes that CSS makes the
#                   Playwright run against this image fail with the new hash in the message: add it here.
ENV API_UPSTREAM=http://api \
    CSP_ENTRA_HOST= \
    CSP_MAP_HOSTS=https://tiles.openfreemap.org \
    CSP_STYLE_HASHES="'sha256-StEaX+se6YS7pqjzrzMIA0KaX9zF/8zAhvQXZAe5epY=' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=' 'sha256-nzTgYzXYDNe6BAHiiI7NNlfK8n/auuOAhh2t92YvuXo='" \
    NGINX_ENVSUBST_FILTER='^(API_UPSTREAM|NGINX_RESOLVER|CSP_)'

USER nginx
EXPOSE 8080
