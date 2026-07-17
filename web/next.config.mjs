/** @type {import('next').NextConfig} */
const gateway = process.env.GATEWAY_URL || "http://localhost:8080";

const nextConfig = {
  output: "standalone",
  async rewrites() {
    // Browser calls same-origin /api/* ; Next proxies to the gateway so there
    // is no CORS to deal with. The live-metrics WebSocket connects directly.
    return [{ source: "/api/:path*", destination: `${gateway}/api/:path*` }];
  },
};

export default nextConfig;
