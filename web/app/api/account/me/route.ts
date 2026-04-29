import { forwardJson, proxyToApi } from "@/lib/server-api";

export async function GET() {
  return forwardJson(await proxyToApi("/account/me"));
}
