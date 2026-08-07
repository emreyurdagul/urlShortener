import http from "k6/http";
import { check } from "k6";

// Light, fixed-rate redirect load for a fair micro-vs-mono latency comparison on
// a modest machine. Instead of "20 VUs as fast as possible" (which saturates an
// 8-core box and makes k6 fight the stack for CPU), it drives a STEADY arrival
// rate so neither side saturates — then the latency percentiles are the signal.
const BASE = __ENV.GATEWAY || "http://gateway:8080";
const HOST = __ENV.LINK_DOMAIN || "localhost:8080";
const RATE = parseInt(__ENV.RATE || "250");   // requests/sec, held constant
const DURATION = __ENV.DURATION || "15s";

export const options = {
  scenarios: {
    redirects: {
      executor: "constant-arrival-rate",
      rate: RATE,
      timeUnit: "1s",
      duration: DURATION,
      preAllocatedVUs: 20,
      maxVUs: 60,
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
