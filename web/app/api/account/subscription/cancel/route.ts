import { forwardJson, proxyToApi, readJsonBody } from "@/lib/server-api";

export async function POST(request: Request) {
  return forwardJson(await proxyToApi("/account/subscription/cancel", { method: "POST", body: JSON.stringify(await readJsonBody(request)) }));
}
