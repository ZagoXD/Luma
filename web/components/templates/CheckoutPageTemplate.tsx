"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, BadgeCheck } from "lucide-react";
import { Elements } from "@stripe/react-stripe-js";
import { loadStripe, type StripeElementsOptions } from "@stripe/stripe-js";
import { StripeCheckoutForm } from "@/components/organisms/StripeCheckoutForm";
import {
  createStripeSubscription,
  getBillingIntervalLabel,
  getPlanPrice,
  plans,
  type BillingInterval,
  type PlanCode,
} from "@/lib/luma-api";

type CheckoutPageTemplateProps = {
  planCode: PlanCode;
  billingInterval: BillingInterval;
};

export function CheckoutPageTemplate({ planCode, billingInterval }: CheckoutPageTemplateProps) {
  const router = useRouter();
  const [status, setStatus] = useState("");
  const [success, setSuccess] = useState(false);
  const [loading, setLoading] = useState(true);
  const [publishableKey, setPublishableKey] = useState("");
  const [clientSecret, setClientSecret] = useState("");
  const [stripeSubscriptionId, setStripeSubscriptionId] = useState("");

  const plan = plans[planCode];
  const stripePromise = useMemo(
    () => publishableKey ? loadStripe(publishableKey) : null,
    [publishableKey],
  );

  useEffect(() => {
    let ignore = false;

    createStripeSubscription({ planCode, billingInterval })
      .then((result) => {
        if (ignore) return;
        setPublishableKey(result.publishableKey);
        setClientSecret(result.clientSecret);
        setStripeSubscriptionId(result.stripeSubscriptionId);
      })
      .catch((error) => {
        if (ignore) return;
        setStatus(error instanceof Error ? error.message : "Não consegui iniciar o checkout da Stripe.");
      })
      .finally(() => {
        if (!ignore) setLoading(false);
      });

    return () => {
      ignore = true;
    };
  }, [planCode, billingInterval]);

  function selectBillingInterval(nextBillingInterval: BillingInterval) {
    if (nextBillingInterval === billingInterval) return;
    setSuccess(false);
    router.replace(`/checkout/${planCode}?billing=${nextBillingInterval}`);
  }

  const options: StripeElementsOptions | undefined = clientSecret
    ? {
        clientSecret,
        appearance: {
          theme: "stripe",
          variables: {
            colorPrimary: "#6d3fb3",
            borderRadius: "8px",
            fontFamily: "DM Sans, system-ui, sans-serif",
          },
        },
      }
    : undefined;

  return (
    <main className="account-page">
      <section className="account-shell">
        <Link href="/" className="account-back">
          <ArrowLeft size={18} />
          Voltar
        </Link>

        <div className="checkout-grid">
          <aside className="account-panel plan-summary">
            <span className="account-kicker">Checkout seguro</span>
            <h1>{plan.name}</h1>
            <p className="checkout-price">{getPlanPrice(planCode, billingInterval)}</p>
            <div className="checkout-billing-switch" role="tablist" aria-label="Ciclo de cobrança">
              <button
                type="button"
                className={billingInterval === "annual" ? "active" : ""}
                onClick={() => selectBillingInterval("annual")}
              >
                Anual
              </button>
              <button
                type="button"
                className={billingInterval === "monthly" ? "active" : ""}
                onClick={() => selectBillingInterval("monthly")}
              >
                Mensal
              </button>
            </div>
            <p className="profile-note">
              Cobrança {getBillingIntervalLabel(billingInterval).toLowerCase()}.
              {billingInterval === "annual" ? ` ${plan.annualEquivalent}.` : " Você pode cancelar a renovação quando quiser."}
            </p>
            {billingInterval === "annual" ? (
              <p className="checkout-installment-note">
                No anual, o acesso vale por 12 meses. Cancelar impede a próxima renovação anual, mas não transforma o período vigente em plano mensal.
              </p>
            ) : (
              <p className="checkout-installment-note">
                O plano mensal é cobrado à vista a cada mês e não possui parcelamento.
              </p>
            )}
            <ul className="checkout-benefits">
              {plan.benefits.map((benefit) => (
                <li key={benefit}>
                  <BadgeCheck size={18} />
                  {benefit}
                </li>
              ))}
            </ul>
          </aside>

          <div className="account-panel">
            {success ? (
              <div className="checkout-success">
                <BadgeCheck size={36} />
                <h2>Plano ativado</h2>
                <p>Pagamento confirmado pela Stripe. A Luma já pode responder o WhatsApp vinculado à sua conta.</p>
                <Link className="account-primary as-link" href="/perfil">Ver meu perfil</Link>
              </div>
            ) : (
              <>
                <div className="account-heading">
                  <span className="account-kicker">Pagamento</span>
                  <h2>Dados de pagamento</h2>
                  <p>Os campos abaixo são renderizados pela Stripe dentro da Luma. Os dados do cartão não passam pelo nosso servidor.</p>
                </div>

                {loading && <p className="account-status info">Preparando checkout seguro...</p>}
                {status && <p className="account-status error">{status}</p>}
                {stripePromise && options && stripeSubscriptionId && (
                  <Elements stripe={stripePromise} options={options}>
                    <StripeCheckoutForm
                      planCode={planCode}
                      billingInterval={billingInterval}
                      stripeSubscriptionId={stripeSubscriptionId}
                      onSuccess={() => setSuccess(true)}
                    />
                  </Elements>
                )}
              </>
            )}
          </div>
        </div>
      </section>
    </main>
  );
}
