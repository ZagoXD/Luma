import test from "node:test";
import assert from "node:assert/strict";
import { paymentRequest } from "../lib/billing.ts";

test("only an opaque token is forwarded, never card data or client-controlled prices", () => {
  const submitted = { token: "opaque", payer: { email: "buyer@example.com" }, cardNumber: "forbidden", securityCode: "forbidden", amount: 1 };
  assert.deepEqual(paymentRequest("owned-checkout", submitted), {
    checkoutId: "owned-checkout", paymentToken: "opaque", paymentMethod: "card", payerEmail: "buyer@example.com",
  });
});

test("a missing card token cannot submit a subscription", () => {
  assert.throws(() => paymentRequest("checkout", {}));
});
