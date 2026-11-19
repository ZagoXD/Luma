import { clearSessionCookie, forwardJson, proxyToApi, readJsonBody } from "@/lib/server-api";

export async function POST(request: Request) {
  const api = await proxyToApi("/auth/reset-password", {
      method: "POST",
      body: JSON.stringify(await readJsonBody(request)),
    });
  const response = await forwardJson(api);
  if (api.ok) clearSessionCookie(response);
  return response;
}
