import { forwardJson, proxyToApi } from "@/lib/server-api";

export async function POST() {
  return forwardJson(await proxyToApi("/account/phone-verification/send", { method: "POST" }));
}
