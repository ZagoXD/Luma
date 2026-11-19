import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { clearSessionCookie, refreshCookieName, setSessionCookie } from "@/lib/server-api";

export async function POST() {
  const refreshToken = (await cookies()).get(refreshCookieName)?.value;
  if (!refreshToken) return NextResponse.json({ message: "Entre novamente para continuar." }, { status: 401 });
  const api = await fetch(`${process.env.LUMA_API_BASE_URL || "http://localhost:5050"}/account/refresh`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ refreshToken }), cache: "no-store",
  });
  const response = NextResponse.json(api.ok ? { ok: true } : { message: "Não foi possível renovar a sessão." }, { status: api.status });
  if (api.ok) setSessionCookie(response, await api.json());
  else if (api.status === 401) clearSessionCookie(response);
  return response;
}
