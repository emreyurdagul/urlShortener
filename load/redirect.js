import http from "k6/http";
import { check } from "k6";

const BASE = __ENV.GATEWAY || "http://gateway:8080";
// Short links live under a domain; the gateway resolves it from the Host
// header, so the load test must present the domain the link was created on.
const HOST = __ENV.LINK_DOMAIN || "localhost:8080";

export const options = {
  scenarios: {
    redirects: {
      executor: "constant-vus",
      vus: 20,
      duration: "30s",
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<100"],
  },
};

export function setup() {
  const res = http.post(
    `${BASE}/api/links`,
    JSON.stringify({ url: "https://example.com" }),
    { headers: { "Content-Type": "application/json", Host: HOST } },
  );
  check(res, { "link created": (r) => r.status === 201 });
  return { code: res.json("code") };
}

export default function (data) {
  const res = http.get(`${BASE}/${data.code}`, {
    redirects: 0,
    headers: { Host: HOST },
  });
  check(res, { "is 302": (r) => r.status === 302 });
}
