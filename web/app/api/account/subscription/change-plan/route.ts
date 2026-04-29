import { forwardJson, proxyToApi, readJsonBody } from "@/lib/server-api";

export async function POST(request: Request) {
  const body = await readJsonBody(request);
  return forwardJson(await proxyToApi("/account/subscription/change-plan", {
    method: "POST",
    body: JSON.stringify(body),
  }));
}
