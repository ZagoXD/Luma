import test from "node:test";
import assert from "node:assert/strict";
import { paymentRequest, isPaidSubscription } from "../lib/billing.ts";

test("only an opaque token is forwarded, never card data or client-controlled prices", () => {
  const submitted = { token: "opaque", payer: { email: "buyer@example.com" }, cardNumber: "forbidden", securityCode: "forbidden", amount: 1 };
  assert.deepEqual(paymentRequest("owned-checkout", submitted), {
    checkoutId: "owned-checkout", paymentToken: "opaque", paymentMethod: "card", payerEmail: "buyer@example.com",
  });
});

test("a missing card token cannot submit a subscription", () => {
  assert.throws(() => paymentRequest("checkout", {}));
});

test("only confirmed and unexpired entitlements can show a successful payment", () => {
  const now = Date.parse("2026-11-18T12:00:00Z");
  for (const status of ["pending", "failed"]) assert.equal(isPaidSubscription({ status, currentPeriodEndsAt: "2027-11-18T12:00:00Z" }, now), false);
  for (const status of ["active", "canceled"]) {
    assert.equal(isPaidSubscription({ status, currentPeriodEndsAt: "2027-11-18T12:00:00Z" }, now), true);
    assert.equal(isPaidSubscription({ status, currentPeriodEndsAt: "2026-11-17T12:00:00Z" }, now), false);
  }
});
