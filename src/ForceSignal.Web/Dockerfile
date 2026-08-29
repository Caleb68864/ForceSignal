FROM node:22-alpine AS build
WORKDIR /app

ARG VITE_API_BASE_URL=http://localhost:8080
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

COPY src/ForceSignal.Web/package*.json ./
RUN npm ci

COPY src/ForceSignal.Web ./
RUN npm run build

# The Content-Security-Policy has to name the API origin, which is only known here. Render it
# from the same build argument the app is compiled against, plus its WebSocket form for SignalR.
RUN API_WS_ORIGIN="$(printf '%s' "$VITE_API_BASE_URL" | sed -E 's#^http#ws#')" \
 && sed -e "s#__API_ORIGIN__#$VITE_API_BASE_URL#g" -e "s#__API_WS_ORIGIN__#$API_WS_ORIGIN#g" security-headers.inc > security-headers.rendered.inc

FROM nginxinc/nginx-unprivileged:1.29-alpine
COPY src/ForceSignal.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/security-headers.rendered.inc /etc/nginx/security-headers.inc
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD wget -qO- http://127.0.0.1:8080/health >/dev/null || exit 1
