import assert from "node:assert/strict";
import test from "node:test";
import { buildMetadata, buildRobots, buildSitemap, buildStructuredData, isIndexableSite, isPrivatePath, privateMetadata } from "../lib/seo.ts";

test("only the exact HTTPS production origin is indexable", () => {
  assert.equal(isIndexableSite("https://ia-luma.com.br/"), true);
  for (const url of ["https://dev.ia-luma.com.br", "http://localhost:3000", "http://ia-luma.com.br", "https://ia-luma.com.br.evil.test", "invalid"]) {
    assert.equal(isIndexableSite(url), false);
  }
});

test("metadata identifies the product and uses the environment at request time", () => {
  const prod = buildMetadata("https://ia-luma.com.br");
  assert.match(JSON.stringify(prod.title), /Luma IA/);
  assert.match(prod.description!, /WhatsApp/);
  assert.match(prod.description!, /gravidez/);
  assert.equal((prod.robots as { index: boolean }).index, true);
  assert.equal((buildMetadata("https://dev.ia-luma.com.br").robots as { index: boolean }).index, false);
  assert.equal(new URL(buildMetadata("http://localhost:3000").metadataBase!.toString()).origin, "http://localhost:3000");
});

test("only the public home page is included in the production sitemap", () => {
  assert.deepEqual(buildSitemap("https://ia-luma.com.br"), [{ url: "https://ia-luma.com.br/" }]);
  assert.deepEqual(buildSitemap("https://dev.ia-luma.com.br"), []);
  assert.deepEqual(buildSitemap("http://localhost:3000"), []);
});

test("production robots advertises its sitemap without blocking icon and sharing images", () => {
  const robots = buildRobots("https://ia-luma.com.br");
  assert.equal(robots.sitemap, "https://ia-luma.com.br/sitemap.xml");
  const rules = robots.rules as { allow: string; disallow: string[] };
  assert.equal(rules.allow, "/");
  assert.ok(rules.disallow.includes("/perfil"));
  assert.ok(!rules.disallow.includes("/icon"));
  assert.equal(buildRobots("https://dev.ia-luma.com.br").sitemap, undefined);
});

test("account, payment, recovery and API paths are never indexable", () => {
  for (const path of ["/perfil", "/perfil/123/calendario", "/checkout/essencial", "/login", "/forgot-password", "/reset-password", "/api/auth/login"]) {
    assert.equal(isPrivatePath(path), true);
  }
  for (const path of ["/", "/icon", "/opengraph-image", "/robots.txt"]) assert.equal(isPrivatePath(path), false);
  assert.deepEqual(privateMetadata("Meu perfil").robots, { index: false, follow: false });
});

test("structured data declares the real brand without fictitious medical credentials or reviews", () => {
  const text = JSON.stringify(buildStructuredData());
  assert.match(text, /WebSite/);
  assert.match(text, /Luma IA/);
  assert.match(text, /https:\/\/ia-luma.com.br/);
  assert.doesNotMatch(text, /aggregateRating|MedicalOrganization|Physician/);
});
