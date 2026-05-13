import { forwardJson, proxyFormToApi } from "@/lib/server-api";

export async function POST(request: Request) {
  return forwardJson(await proxyFormToApi("/support/requests", await request.formData()));
}
