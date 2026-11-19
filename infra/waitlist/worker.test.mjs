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
