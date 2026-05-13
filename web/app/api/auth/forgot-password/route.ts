import { forwardJson, proxyToApi, readJsonBody } from "@/lib/server-api";

export async function POST(request: Request) {
  return forwardJson(
    await proxyToApi("/auth/forgot-password", {
      method: "POST",
      body: JSON.stringify(await readJsonBody(request)),
    }),
  );
}
