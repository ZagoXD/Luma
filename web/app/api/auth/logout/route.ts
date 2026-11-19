import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { clearSessionCookie, refreshCookieName, sessionCookieName } from "@/lib/server-api";

export async function POST() {
  const store = await cookies();
  const refreshToken = store.get(refreshCookieName)?.value;
  const access = store.get(sessionCookieName)?.value;
  if (refreshToken || access) {
    const api = await fetch(`${process.env.LUMA_API_BASE_URL || "http://localhost:5050"}/account/logout`, {
      method: "POST", headers: { "Content-Type": "application/json", ...(access ? { Authorization: `Bearer ${access}` } : {}) }, body: JSON.stringify({ refreshToken }), cache: "no-store",
    });
    if (!api.ok) return NextResponse.json({ message: "Não foi possível encerrar a sessão agora. Tente novamente." }, { status: 503 });
  }
  const response = NextResponse.json({ ok: true });
  clearSessionCookie(response);
  return response;
}
