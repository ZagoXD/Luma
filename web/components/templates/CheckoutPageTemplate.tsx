"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, BadgeCheck } from "lucide-react";
import { PaymentCardForm } from "@/components/organisms/PaymentCardForm";
import { paymentRequest, isPaidSubscription, type CheckoutConfiguration } from "@/lib/billing";
import { prepareCheckout, submitCheckout, getCheckoutStatus, getPlanPrice, plans, type BillingInterval, type PlanCode } from "@/lib/luma-api";

export function CheckoutPageTemplate({ planCode, billingInterval, replacesSubscriptionId }: { planCode: PlanCode; billingInterval: BillingInterval; replacesSubscriptionId?: string }) {
  const router = useRouter();
  const [checkout, setCheckout] = useState<CheckoutConfiguration>();
  const [status, setStatus] = useState("");
  const [waiting, setWaiting] = useState(false);
  const [success, setSuccess] = useState(false);
  const [scheduled, setScheduled] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const plan = plans[planCode];

  useEffect(() => {
    let active = true;
    prepareCheckout({ planCode, billingInterval, replacesSubscriptionId }).then(result => {
      if (active) { setCheckout(result); setWaiting(!!result.submitted && !result.scheduled); setScheduled(!!result.scheduled); }
    })
      .catch(error => { if (active) setStatus(error instanceof Error ? error.message : "Não consegui preparar o checkout."); });
    return () => { active = false; };
  }, [planCode, billingInterval, replacesSubscriptionId]);

  useEffect(() => {
    if (!waiting || !checkout) return;
    let active = true;
    let inFlight = false;
    const timer = setInterval(async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const { subscription } = await getCheckoutStatus(checkout.checkoutId);
        if (active && isPaidSubscription(subscription)) {
          setSuccess(true); setWaiting(false);
        }
        else if (active && subscription.status === "failed") { setWaiting(false); setStatus("Esta contratação foi encerrada. Volte ao perfil para escolher um novo plano."); }
      } catch { if (active) setStatus("A confirmação continua pendente. Você pode acompanhar pelo perfil."); }
      finally { inFlight = false; }
    }, 6000);
    const stop = setTimeout(() => { if (active) setWaiting(false); }, 180000);
    return () => { active = false; clearInterval(timer); clearTimeout(stop); };
  }, [waiting, checkout]);

  return <main className="account-page"><section className="account-shell">
    <Link href="/perfil" className="account-back"><ArrowLeft size={18} /> Voltar</Link>
    <div className="checkout-grid">
      <aside className="account-panel plan-summary">
        <span className="account-kicker">Assinatura Luma</span><h1>{plan.name}</h1>
        <p className="checkout-price">{getPlanPrice(planCode, billingInterval)}</p>
        <div className="checkout-billing-switch" role="group" aria-label="Ciclo de cobrança">
          {(["annual", "monthly"] as const).map(interval => <button key={interval} type="button"
            className={billingInterval === interval ? "active" : ""} disabled={waiting || submitting}
            onClick={() => router.replace(`/checkout/${planCode}?billing=${interval}${replacesSubscriptionId ? `&replace=${encodeURIComponent(replacesSubscriptionId)}` : ""}`)}>{interval === "annual" ? "Anual" : "Mensal"}</button>)}
        </div>
        <p className="profile-note">{billingInterval === "annual" ? `${plan.annualEquivalent}, com cobrança anual à vista e acesso por 12 meses.` : "Cobrança mensal à vista."}</p>
        <p className="checkout-installment-note">Cartão de crédito com renovação automática. Desativar a renovação mantém o período pago; cancelar imediatamente encerra o acesso. <Link href="/termos">Consulte os termos e o direito de arrependimento de sete dias.</Link></p>
        <ul className="checkout-benefits">{plan.benefits.map(benefit => <li key={benefit}><BadgeCheck size={18} />{benefit}</li>)}</ul>
      </aside>
      <div className="account-panel">
        {success || scheduled ? <div className="checkout-success"><BadgeCheck size={36} /><h2>{scheduled ? "Troca agendada" : "Pagamento confirmado"}</h2>
          <p>{scheduled ? "Sua autorização foi recebida. O novo plano começa após o período já pago, mediante confirmação da próxima cobrança. A renovação anterior foi interrompida." : "Seu plano está disponível. A Luma já pode responder pelo WhatsApp vinculado à sua conta."}</p>
          <Link href="/perfil" className="account-primary as-link">Ver meu perfil</Link></div> : <>
          <div className="account-heading"><span className="account-kicker">Pagamento seguro</span><h2>Concluir assinatura</h2></div>
          {status && <p role="alert" className="account-status error">{status}</p>}
          {!checkout && !status && <p role="status">Preparando checkout seguro...</p>}
          {checkout && !waiting && !checkout.submitted && <>
            <PaymentCardForm publicKey={checkout.publicKey} amount={checkout.amount} email={checkout.email}
              onSubmit={async form => {
                const result = await submitCheckout(paymentRequest(checkout.checkoutId, form));
                setCheckout(current => current && { ...current, submitted: true });
                if (result.subscription.scheduled) setScheduled(true);
                else if (isPaidSubscription(result.subscription)) setSuccess(true);
                else if (result.subscription.status === "failed") throw new Error("Esta contratação foi encerrada.");
                else setWaiting(true);
              }} />
          </>}
          {waiting && <p role="status" className="account-status info">Aguardando confirmação do pagamento. Seu plano será liberado após a aprovação. No cartão, a primeira cobrança pode levar cerca de uma hora; não é necessário assinar novamente.</p>}
          {waiting && <Link href="/perfil" className="account-back">Acompanhar no meu perfil</Link>}
        </>}
      </div>
    </div>
  </section></main>;
}
