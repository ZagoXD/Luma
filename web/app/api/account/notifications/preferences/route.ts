import { forwardJson, proxyToApi, readJsonBody } from "@/lib/server-api";

export async function GET() {
  return forwardJson(await proxyToApi("/account/notifications/preferences"));
}

export async function POST(request: Request) {
  const body = await readJsonBody(request);

  return forwardJson(
    await proxyToApi("/account/notifications/preferences", {
      method: "POST",
      body: JSON.stringify(body),
    }),
  );
}
