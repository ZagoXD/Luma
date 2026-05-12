"use client";

import { FormEvent, useState } from "react";
import { PaymentElement, useElements, useStripe } from "@stripe/react-stripe-js";
import { CreditCard } from "lucide-react";
import { confirmStripeSubscription, type BillingInterval, type PlanCode } from "@/lib/luma-api";
import { formatCpf, isValidCpf } from "@/lib/account-format";

type StripeCheckoutFormProps = {
  planCode: PlanCode;
  billingInterval: BillingInterval;
  stripeSubscriptionId: string;
  onSuccess: () => void;
};

export function StripeCheckoutForm({ planCode, billingInterval, stripeSubscriptionId, onSuccess }: StripeCheckoutFormProps) {
  const stripe = useStripe();
  const elements = useElements();
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cardholderName, setCardholderName] = useState("");
  const [billingCpf, setBillingCpf] = useState("");

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!stripe || !elements) return;

    setStatus("");

    if (!cardholderName.trim()) {
      setStatus("Informe o nome conforme está no cartão.");
      return;
    }

    if (!isValidCpf(billingCpf)) {
      setStatus("Informe um CPF válido para os dados de cobrança.");
      return;
    }

    setSubmitting(true);

    const result = await stripe.confirmPayment({
      elements,
      confirmParams: {
        payment_method_data: {
          billing_details: {
            name: cardholderName.trim(),
          },
        },
      },
      redirect: "if_required",
    });

    if (result.error) {
      setStatus(result.error.message || "Não consegui confirmar o pagamento.");
      setSubmitting(false);
      return;
    }

    try {
      await confirmStripeSubscription({ planCode, billingInterval, stripeSubscriptionId, cardholderName, billingCpf });
      onSuccess();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Pagamento confirmado, mas não consegui ativar o plano localmente.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className="account-form stripe-payment-form" onSubmit={handleSubmit}>
      <div className="form-row">
        <label>
          Nome no cartão
          <input
            autoComplete="cc-name"
            value={cardholderName}
            onChange={(event) => setCardholderName(event.target.value)}
            placeholder="Nome impresso no cartão"
            required
          />
        </label>
        <label>
          CPF do titular
          <input
            inputMode="numeric"
            autoComplete="off"
            value={billingCpf}
            onChange={(event) => setBillingCpf(formatCpf(event.target.value))}
            placeholder="000.000.000-00"
            maxLength={14}
            required
          />
        </label>
      </div>
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
