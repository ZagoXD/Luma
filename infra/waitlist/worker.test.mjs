import { test } from "node:test";
import assert from "node:assert/strict";
import worker, { protect } from "./worker.mjs";
test("waitlist rejects requests without server authorization", async () => {
  assert.equal((await worker.fetch(new Request("https://example.com", { method: "POST" }), { API_SECRET: "secret" })).status, 401);
});
test("contact encryption is randomized and does not expose its payload", async () => {
  const key = Buffer.alloc(32, 1).toString("base64");
  const first = await protect({ email: "nay@example.com" }, key);
  assert.notEqual(first, await protect({ email: "nay@example.com" }, key));
  assert.ok(!Buffer.from(first, "base64").toString().includes("nay@example.com"));
});

const key = Buffer.alloc(32, 1).toString("base64");
const signup = { name: "Nay", email: "nay@example.com", channel: "email", termsAccepted: true,
  launchConsent: true, version: "2026-11-22", termsVersion: "2026-11-18", privacyVersion: "2026-11-18" };
function harness() {
  const inserts = [];
  const env = { API_SECRET: "secret", ENCRYPTION_KEY: key, ASSETS: { fetch: () => new Response("asset") },
    DB: { prepare(sql) { return { bind(...args) { return {
      async run() { if (sql.startsWith("INSERT INTO leads")) inserts.push(args); },
      async first() { return { attempts: 1 }; }
    }; } }; } } };
  const send = (body, origin = "https://espera.example.com") => worker.fetch(new Request("https://espera.example.com/api/waitlist", {
    method: "POST", headers: { "Content-Type": "application/json", origin, "cf-connecting-ip": "192.0.2.10" }, body: JSON.stringify(body)
  }), env);
  return { env, inserts, send };
}
async function unprotect(value) {
  const bytes = Buffer.from(value, "base64");
  const aes = await crypto.subtle.importKey("raw", Buffer.from(key, "base64"), "AES-GCM", false, ["decrypt"]);
  return JSON.parse(new TextDecoder().decode(await crypto.subtle.decrypt({name: "AES-GCM", iv: bytes.subarray(0,12),
    additionalData: new TextEncoder().encode("luma.waitlist.v1")}, aes, bytes.subarray(12))));
}
test("Pages rejects missing, combined or stale consents before persisting", async () => {
  const h = harness();
  for (const body of [{...signup, termsAccepted: false}, {...signup, launchConsent: false},
    {name: "Nay", email: "nay@example.com", consent: true}, {...signup, version: "old"}, null, []]) {
    const response = await h.send(body);
    assert.equal(response.status, 400); assert.ok((await response.json()).message);
  }
  assert.equal(h.inserts.length, 0);
});
test("Pages encrypts contacts and independent consent evidence for each channel", async () => {
  for (const channel of ["email", "whatsapp", "both"]) {
    const h = harness(); const response = await h.send({...signup, channel, whatsapp: "16992330309"});
    assert.equal(response.status, 200); assert.match((await response.json()).message, /canais escolhidos/);
    const insert = h.inserts[0], stored = await unprotect(insert[2]);
    assert.equal(stored.email, channel === "whatsapp" ? null : "nay@example.com");
    assert.equal(stored.whatsapp, channel === "email" ? null : "+5516992330309");
    assert.equal(stored.termsAccepted, true); assert.equal(stored.launchConsent, true);
    assert.equal(stored.emailOptIn, channel !== "whatsapp"); assert.equal(stored.whatsappOptIn, channel !== "email");
    assert.equal(stored.termsVersion, "2026-11-18"); assert.equal(stored.version, "2026-11-22");
    assert.ok(!Number.isNaN(Date.parse(stored.consentRecordedAt))); assert.equal(insert[3], "2026-11-22");
    assert.ok(!insert.join(" ").includes("nay@example.com"));
  }
});
test("Pages preserves origin checks, honeypot and persistent throttling", async () => {
  const h = harness();
  assert.equal((await h.send(signup, "https://evil.example.com")).status, 403);
  assert.equal((await h.send({...signup, website: "spam"})).status, 200); assert.equal(h.inserts.length, 0);
  h.env.DB.prepare = () => ({ bind() { return { async run() {}, async first() { return { attempts: 6 }; } }; } });
  assert.equal((await h.send(signup)).status, 429);
});
