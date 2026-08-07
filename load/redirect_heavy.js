import http from "k6/http";
import { check } from "k6";

// Heavy, saturating redirect load — constant VUs hammering as fast as possible,
// to push the machine and compare throughput + tail latency under contention.
// (The light, fixed-rate counterpart is redirect_light.js.)
const BASE = __ENV.GATEWAY || "http://gateway:8080";
const HOST = __ENV.LINK_DOMAIN || "localhost:8080";
const VUS = parseInt(__ENV.VUS || "30");
const DURATION = __ENV.DURATION || "20s";

export const options = {
  scenarios: {
    redirects: {
      executor: "constant-vus",
      vus: VUS,
      duration: DURATION,
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
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
