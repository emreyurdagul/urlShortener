FROM docker.io/library/node:22-bookworm-slim AS build
WORKDIR /app
COPY web/package.json web/package-lock.json* ./
RUN npm install
COPY web/ ./
# Next bakes rewrite destinations into the build, so the gateway URL must be
# present at build time (not just runtime).
ARG GATEWAY_URL=http://gateway:8080
ENV GATEWAY_URL=${GATEWAY_URL}
# NEXT_PUBLIC_* vars are inlined into the client bundle at build time. The live
# dashboard opens this WebSocket directly from the browser, so behind TLS it
# must be a wss:// URL on the gateway's public domain.
ARG NEXT_PUBLIC_GATEWAY_WS=
ENV NEXT_PUBLIC_GATEWAY_WS=${NEXT_PUBLIC_GATEWAY_WS}
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build

FROM docker.io/library/node:22-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
# Next's standalone server must bind all interfaces so the reverse proxy
# (a separate container) can reach it; otherwise it listens on localhost only.
ENV HOSTNAME=0.0.0.0
# Next.js standalone output bundles a minimal server + node_modules.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public
EXPOSE 3001
ENV PORT=3001
CMD ["node", "server.js"]
