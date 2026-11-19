export type SessionTokens = {
  token: string; refreshToken: string; accessExpiresAt: string; refreshExpiresAt: string;
};

export function cookieNames(secure: boolean) {
  return secure ? { access: "__Host-luma_access", refresh: "__Host-luma_refresh" }
    : { access: "luma_access", refresh: "luma_refresh" };
}

export function sessionCookies(tokens: SessionTokens, secure: boolean, now = Date.now()) {
  const names = cookieNames(secure);
  return [
    { name: names.access, value: tokens.token, expires: tokens.accessExpiresAt, limit: 600 },
    { name: names.refresh, value: tokens.refreshToken, expires: tokens.refreshExpiresAt, limit: 30 * 86400 },
  ].map(cookie => ({ name: cookie.name, value: cookie.value, options: {
    httpOnly: true, secure, sameSite: "lax" as const, path: "/",
    maxAge: Math.max(0, Math.min(cookie.limit, Math.floor((Date.parse(cookie.expires) - now) / 1000))),
  } }));
}

export function trustedMutation(method: string, host: string, origin: string | null, fetchSite: string | null, secure: boolean) {
  if (["GET", "HEAD", "OPTIONS"].includes(method)) return true;
  if (!origin || fetchSite === "cross-site") return false;
  try {
    const url = new URL(origin);
    return url.host.toLowerCase() === host.toLowerCase() && url.origin === origin
      && (url.protocol === "https:" || (!secure && url.protocol === "http:"));
  } catch { return false; }
}

export function safeReturnPath(value: string | null) {
  if (!value || !value.startsWith("/")) return "/perfil";
  try {
    const decoded = decodeURIComponent(value);
    if (decoded.startsWith("//") || /[\\\u0000-\u001f]/.test(decoded)) return "/perfil";
    const url = new URL(value, "https://luma.invalid");
    return url.origin === "https://luma.invalid" ? url.pathname + url.search + url.hash : "/perfil";
  } catch { return "/perfil"; }
}
