import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { cookieNames, sessionCookies, type SessionTokens } from "./session-policy";

const secure = process.env.LUMA_COOKIE_SECURE === "true";
export const sessionCookieName = cookieNames(secure).access;
export const refreshCookieName = cookieNames(secure).refresh;

const apiBaseUrl =
  process.env.LUMA_API_BASE_URL ||
  process.env.NEXT_PUBLIC_API_BASE_URL ||
  "http://localhost:5050";

export async function proxyToApi(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/json");

  return authenticatedFetch(path, { ...init, headers });
}

export async function proxyFormToApi(path: string, formData: FormData, init: RequestInit = {}) {
  const headers = new Headers(init.headers);

  return authenticatedFetch(path, {
    ...init,
    method: init.method || "POST",
    headers,
    body: formData,
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

async function authenticatedFetch(path: string, init: RequestInit) {
  const store = await cookies();
  const send = (token?: string) => {
    const headers = new Headers(init.headers);
    if (token) headers.set("Authorization", `Bearer ${token}`);
    return fetch(`${apiBaseUrl}${path}`, { ...init, headers, cache: "no-store" });
  };
  const response = await send(store.get(sessionCookieName)?.value);
  const refreshToken = store.get(refreshCookieName)?.value;
  if (response.status !== 401 || !refreshToken) return response;
  const refreshed = await fetch(`${apiBaseUrl}/account/refresh`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }), cache: "no-store", signal: AbortSignal.timeout(15000),
  });
  if (!refreshed.ok) {
    if (refreshed.status === 401) {
      for (const name of [sessionCookieName, refreshCookieName, "luma_session"])
        store.set(name, "", { httpOnly: true, secure, sameSite: "lax", path: "/", maxAge: 0 });
    }
    return refreshed;
  }
  const tokens = await refreshed.json() as SessionTokens;
  for (const cookie of sessionCookies(tokens, secure)) store.set(cookie.name, cookie.value, cookie.options);
  return send(tokens.token);
}

export async function forwardJson(response: Response) {
  const body = await safeJson(response);
  const result = NextResponse.json(body, { status: response.status, headers: { "Cache-Control": "no-store" } });
  const retry = response.headers.get("Retry-After");
  if (retry) result.headers.set("Retry-After", retry);
  return result;
}

export function setSessionCookie(response: NextResponse, tokens: SessionTokens) {
  for (const cookie of sessionCookies(tokens, secure)) response.cookies.set(cookie.name, cookie.value, cookie.options);
  response.cookies.set("luma_session", "", { httpOnly: true, secure, sameSite: "lax", path: "/", maxAge: 0 });
  response.headers.set("Cache-Control", "no-store");
}

export function clearSessionCookie(response: NextResponse) {
  for (const name of [sessionCookieName, refreshCookieName, "luma_session"]) response.cookies.set(name, "", {
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
