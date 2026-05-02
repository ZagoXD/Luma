import { forwardJson, proxyToApi } from "@/lib/server-api";

export async function GET(request: Request) {
  const { searchParams } = new URL(request.url);
  const month = searchParams.get("month");

  return forwardJson(await proxyToApi(`/account/calendar${month ? `?month=${encodeURIComponent(month)}` : ""}`));
}
