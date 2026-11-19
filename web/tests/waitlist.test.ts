import { test } from "node:test";
import assert from "node:assert/strict";
import { validateWaitlist } from "../lib/waitlist.ts";
test("waitlist requires explicit consent and valid bounded data", () => {
  assert.equal(validateWaitlist({ name: "Nay", email: "nay@example.com" }), null);
  assert.equal(validateWaitlist({ name: "Nay", email: "invalid", consent: true }), null);
  assert.equal(validateWaitlist({ name: "N".repeat(101), email: "nay@example.com", consent: true }), null);
  assert.equal(validateWaitlist({ name: " Nay ", email: " NAY@EXAMPLE.COM ", consent: true })?.email, "nay@example.com");
});
