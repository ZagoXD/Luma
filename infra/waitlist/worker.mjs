const encoder = new TextEncoder();
async function hash(value, secret) {
  const key = await crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return Array.from(new Uint8Array(await crypto.subtle.sign("HMAC", key, encoder.encode(value))), b => b.toString(16).padStart(2, "0")).join("");
}
export async function protect(value, base64Key) {
  const key = await crypto.subtle.importKey("raw", Uint8Array.from(atob(base64Key), c => c.charCodeAt(0)), "AES-GCM", false, ["encrypt"]);
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const cipher = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv, additionalData: encoder.encode("luma.waitlist.v1") }, key, encoder.encode(JSON.stringify(value))));
  return btoa(String.fromCharCode(...iv, ...cipher));
}
export default {
  async fetch(request, env) {
    const path = new URL(request.url).pathname;
    if (env.ASSETS && path !== "/api/waitlist") return env.ASSETS.fetch(request);
    if (request.method !== "POST" || (path !== "/" && path !== "/api/waitlist")) return new Response(null, { status: 404 });
    const auth = request.headers.get("authorization") || "";
    const fromPages = !!env.ASSETS;
    if (fromPages && request.headers.get("origin") !== new URL(request.url).origin) return new Response(null, { status: 403 });
    if (!fromPages && (!env.API_SECRET || await hash(auth, env.API_SECRET) !== await hash(`Bearer ${env.API_SECRET}`, env.API_SECRET))) return new Response(null, { status: 401 });
    const raw = await request.text();
    if (raw.length > 4096) return new Response(null, { status: 413 });
    let input;
    try { input = JSON.parse(raw); } catch { return new Response(null, { status: 400 }); }
    if (fromPages && input && typeof input === "object") {
      if (input.website) return Response.json({ message: "Interesse registrado." });
      input.ip = request.headers.get("cf-connecting-ip") || "unknown";
      input.version = "2026-11-18";
    }
    if (!input || input.consent !== true || input.version !== "2026-11-18" || typeof input.name !== "string" || input.name.trim().length < 2 || input.name.length > 100
      || typeof input.email !== "string" || input.email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(input.email) || typeof input.ip !== "string" || input.ip.length > 100)
      return new Response(null, { status: 400 });
    const now = Math.floor(Date.now() / 1000);
    const bucket = await hash(input.ip + ":" + Math.floor(now / 3600), env.API_SECRET);
    await env.DB.prepare("DELETE FROM request_limits WHERE expires_at < ?").bind(now).run();
    const limit = await env.DB.prepare("INSERT INTO request_limits(bucket,attempts,expires_at) VALUES(?,1,?) ON CONFLICT(bucket) DO UPDATE SET attempts=attempts+1 RETURNING attempts").bind(bucket, now + 3600).first();
    if (limit.attempts > 5) return new Response(null, { status: 429 });
    const email = input.email.trim().toLowerCase();
    const emailHash = await hash(email, env.ENCRYPTION_KEY);
    const encrypted = await protect({ name: input.name.trim(), email }, env.ENCRYPTION_KEY);
    await env.DB.prepare("INSERT INTO leads(id,email_hash,encrypted_contact,consent_version,created_at) VALUES(?,?,?,?,?) ON CONFLICT(email_hash) DO NOTHING")
      .bind(crypto.randomUUID(), emailHash, encrypted, input.version, new Date().toISOString()).run();
    return Response.json({ accepted: true, message: "Você está na lista! Avisaremos por e-mail quando a Luma estiver disponível." });
  }
};
