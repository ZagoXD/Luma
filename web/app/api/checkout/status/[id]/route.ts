import { forwardJson, proxyToApi } from "@/lib/server-api";

export async function GET(_request: Request, context: { params: Promise<{ id: string }> }) {
  const { id } = await context.params;
  return forwardJson(await proxyToApi(`/checkout/status/${encodeURIComponent(id)}`));
}
