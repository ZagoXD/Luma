export type CheckoutConfiguration = {
  provider: "mercadopago";
  publicKey: string;
  checkoutId: string;
  amount: number;
  email: string;
  submitted?: boolean;
  scheduled?: boolean;
  startsAt?: string;
};

export type BrickSubmission = { token?: string; payer?: { email?: string } };

export function isPaidSubscription(subscription: { status: string; currentPeriodEndsAt: string }, now = Date.now()) {
  return (subscription.status === "active" || subscription.status === "canceled")
    && Date.parse(subscription.currentPeriodEndsAt) > now;
}

// Do not forward the full SDK form: only its opaque token and payer e-mail are accepted.
export function paymentRequest(checkoutId: string, form: BrickSubmission) {
  if (!form.token) throw new Error("Não foi possível validar o cartão. Confira os dados e tente novamente.");
  return { checkoutId, paymentToken: form.token, paymentMethod: "card" as const, payerEmail: form.payer?.email };
}
