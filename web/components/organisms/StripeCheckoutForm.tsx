"use client";

import { FormEvent, useState } from "react";
import { PaymentElement, useElements, useStripe } from "@stripe/react-stripe-js";
import { CreditCard } from "lucide-react";
import { confirmStripeSubscription, type PlanCode } from "@/lib/luma-api";

type StripeCheckoutFormProps = {
  planCode: PlanCode;
  stripeSubscriptionId: string;
  onSuccess: () => void;
};

export function StripeCheckoutForm({ planCode, stripeSubscriptionId, onSuccess }: StripeCheckoutFormProps) {
  const stripe = useStripe();
  const elements = useElements();
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!stripe || !elements) return;

    setStatus("");
    setSubmitting(true);

    const result = await stripe.confirmPayment({
      elements,
      redirect: "if_required",
    });

    if (result.error) {
      setStatus(result.error.message || "Não consegui confirmar o pagamento.");
      setSubmitting(false);
      return;
    }

    try {
      await confirmStripeSubscription({ planCode, stripeSubscriptionId });
      onSuccess();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Pagamento confirmado, mas não consegui ativar o plano localmente.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className="account-form stripe-payment-form" onSubmit={handleSubmit}>
      <div className="stripe-element-shell">
        <PaymentElement />
      </div>

      <button className="account-primary" type="submit" disabled={!stripe || submitting}>
        <CreditCard size={18} />
        {submitting ? "Confirmando..." : "Confirmar pagamento"}
      </button>
      {status && <p className="account-status error">{status}</p>}
    </form>
  );
}
