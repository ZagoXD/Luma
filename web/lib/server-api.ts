import { cookies } from "next/headers";
import { NextResponse } from "next/server";

export const sessionCookieName = "luma_session";

const apiBaseUrl =
  process.env.LUMA_API_BASE_URL ||
  process.env.NEXT_PUBLIC_API_BASE_URL ||
  "http://localhost:5050";

export async function proxyToApi(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/json");

  const cookieStore = await cookies();
  const token = cookieStore.get(sessionCookieName)?.value;
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  return fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers,
    cache: "no-store",
  });
}

export async function readJsonBody(request: Request) {
  try {
    return await request.json();
  } catch {
    return {};
  }
}

export async function forwardJson(response: Response) {
  const body = await safeJson(response);
  return NextResponse.json(body, { status: response.status });
}

export function setSessionCookie(response: NextResponse, token: string) {
  response.cookies.set(sessionCookieName, token, {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.LUMA_COOKIE_SECURE === "true",
    path: "/",
    maxAge: 60 * 60 * 24 * 30,
  });
}

export function clearSessionCookie(response: NextResponse) {
  response.cookies.set(sessionCookieName, "", {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.LUMA_COOKIE_SECURE === "true",
    path: "/",
    maxAge: 0,
  });
}

async function safeJson(response: Response) {
  try {
    return await response.json();
  } catch {
    return {};
  }
}
