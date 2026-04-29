import { forwardJson, proxyToApi } from "@/lib/server-api";

export async function POST() {
  return forwardJson(await proxyToApi("/account/payment-method/setup-intent", { method: "POST" }));
}
