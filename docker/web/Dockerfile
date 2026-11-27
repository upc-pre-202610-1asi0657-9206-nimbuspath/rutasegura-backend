# BuildKit named context web-dist must contain the Vite production build, not its source or secrets.
# docker build --build-context web-dist=E:/rutasegura-webapp/dist -f docker/web/Dockerfile -t rutasegura/web:local .
FROM nginx:1.28-alpine
COPY docker/web/default.conf /etc/nginx/conf.d/default.conf
COPY --from=web-dist / /usr/share/nginx/html/
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s CMD wget -q -O /dev/null http://127.0.0.1/health/web || exit 1
