import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { cp, mkdir, writeFile } from "node:fs/promises";
import http from "node:http";

const origin = "https://ia-luma.com.br";
function tag(html, attribute, value) {
  return [...html.matchAll(/<(?:meta|link)\b[^>]*>/g)].map(x => x[0]).find(x => x.includes(`${attribute}="${value}"`)) || "";
}
function attribute(html, key) { return html.match(new RegExp(`${key}="([^"]*)"`))?.[1]?.replaceAll("&amp;", "&"); }

async function audit(base, production) {
  const response = await fetch(base + "/");
  assert.equal(response.status, 200);
  const html = await response.text();
  assert.match(html, /<title>Luma IA/);
  assert.equal(new URL(attribute(tag(html, "rel", "canonical"), "href")).href, origin + "/");
  assert.equal(attribute(tag(html, "property", "og:locale"), "content"), "pt_BR");
  assert.match(attribute(tag(html, "name", "robots"), "content"), production ? /^index,\s*follow$/ : /noindex/);
  assert.equal(Boolean(response.headers.get("x-robots-tag")?.includes("noindex")), !production);
  if (production) assert.match(html, /application\/ld\+json/);
  const robots = await fetch(base + "/robots.txt");
  assert.equal(robots.status, 200);
  assert.equal((await robots.text()).includes("Sitemap: " + origin + "/sitemap.xml"), production);
  const sitemap = await fetch(base + "/sitemap.xml");
  assert.equal(sitemap.status, 200);
  const xml = await sitemap.text();
  assert.equal((xml.match(/<loc>/g) || []).length, production ? 1 : 0);
  if (production) assert.ok(xml.includes("<loc>" + origin + "/</loc>"));
  const login = await fetch(base + "/login");
  assert.match(login.headers.get("x-robots-tag"), /noindex/);
  assert.match(attribute(tag(await login.text(), "name", "robots"), "content"), /noindex/);
  const profile = await fetch(base + "/perfil", { redirect: "manual" });
  assert.ok([307, 308].includes(profile.status));
  assert.match(profile.headers.get("x-robots-tag"), /noindex/);
  const iconLink = attribute(tag(html, "rel", "icon"), "href");
  assert.ok(iconLink, "Missing favicon link");
  for (const [path, width, height] of [[iconLink, 96, 96], ["/apple-icon", 180, 180], ["/opengraph-image", 1200, 630]]) {
    const image = await fetch(new URL(path, base));
    assert.equal(image.status, 200);
    assert.match(image.headers.get("content-type"), /image\/png/);
    const bytes = Buffer.from(await image.arrayBuffer());
    assert.equal(bytes.readUInt32BE(16), width);
    assert.equal(bytes.readUInt32BE(20), height);
    if (process.env.SEO_SAVE_ASSETS === "true" && production) {
      await mkdir("../.backups/seo-preview", { recursive: true });
      await writeFile(`../.backups/seo-preview/${width}.png`, bytes);
    }
  }
  if (production && base.startsWith("http://127.0.0.1")) {
    const www = await new Promise((resolve, reject) => {
      const request = http.get(base + "/?utm_source=seo-test", { headers: { Host: "www.ia-luma.com.br" } }, response => {
        response.resume(); resolve({ status: response.statusCode, location: response.headers.location });
      });
      request.on("error", reject);
    });
    assert.equal(www.status, 308);
    assert.equal(www.location, origin + "/?utm_source=seo-test");
  }
  console.log(JSON.stringify({ base, environment: production ? "production" : "non-production", metadata: true, icons: true, robots: true, sitemap: true, accountNoIndex: true }));
}

if (process.argv[2] === "--local") {
  await cp(".next/static", ".next/standalone/.next/static", { recursive: true });
  await cp("public", ".next/standalone/public", { recursive: true });
  for (const [port, production] of [[3010, true], [3011, false]]) {
    const base = `http://127.0.0.1:${port}`;
    const child = spawn(process.execPath, [".next/standalone/server.js"], {
      env: { ...process.env, HOSTNAME: "127.0.0.1", PORT: String(port), LUMA_SITE_URL: production ? origin : "https://dev.ia-luma.com.br" }, stdio: ["ignore", "pipe", "pipe"],
    });
    let logs = "";
    child.stdout.on("data", value => { logs += value; });
    child.stderr.on("data", value => { logs += value; });
    try {
      let ready = false;
      for (let i = 0; i < 60; i++) {
        if (child.exitCode !== null) throw new Error("Server failed: " + logs);
        try { const response = await fetch(base); ready = response.ok; } catch { }
        if (ready) break;
        await new Promise(resolve => setTimeout(resolve, 500));
      }
      assert.ok(ready, "Server did not become ready");
      await audit(base, production);
    } catch (error) { console.error(logs); throw error; }
    finally {
      if (child.exitCode === null && child.signalCode === null) {
        const exit = new Promise(resolve => child.once("exit", resolve));
        child.kill();
        await exit;
      }
    }
  }
} else {
  await audit(process.argv[2], process.argv[3] === "production");
}
