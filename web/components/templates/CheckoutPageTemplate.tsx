"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, BadgeCheck, Copy } from "lucide-react";
import { PaymentCardForm } from "@/components/organisms/PaymentCardForm";
import { paymentRequest, type CheckoutConfiguration } from "@/lib/billing";
import { prepareCheckout, submitCheckout, getCheckoutStatus, getPlanPrice, plans, type BillingInterval, type PlanCode, type PixDetails } from "@/lib/luma-api";

export function CheckoutPageTemplate({ planCode, billingInterval }: { planCode: PlanCode; billingInterval: BillingInterval }) {
  const router = useRouter();
  const [checkout, setCheckout] = useState<CheckoutConfiguration>();
  const [status, setStatus] = useState("");
  const [method, setMethod] = useState<"card" | "pix">("card");
  const [pix, setPix] = useState<PixDetails>();
  const [waiting, setWaiting] = useState(false);
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const plan = plans[planCode];

  useEffect(() => {
    let active = true;
    prepareCheckout({ planCode, billingInterval }).then(result => { if (active) setCheckout(result); })
      .catch(error => { if (active) setStatus(error instanceof Error ? error.message : "Não consegui preparar o checkout."); });
    return () => { active = false; };
  }, [planCode, billingInterval]);

  useEffect(() => {
    if (!waiting || !checkout) return;
    let active = true;
    let inFlight = false;
    const timer = setInterval(async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const { subscription } = await getCheckoutStatus(checkout.checkoutId);
        if (active && subscription.status !== "pending" && new Date(subscription.currentPeriodEndsAt) > new Date()) {
          setSuccess(true); setWaiting(false);
        }
      } catch { if (active) setStatus("A confirmação continua pendente. Você pode acompanhar pelo perfil."); }
      finally { inFlight = false; }
    }, 6000);
    const stop = setTimeout(() => { if (active) setWaiting(false); }, 180000);
    return () => { active = false; clearInterval(timer); clearTimeout(stop); };
  }, [waiting, checkout]);

  async function payPix() {
    if (!checkout) return;
    setSubmitting(true); setStatus("");
    try {
      const result = await submitCheckout({ checkoutId: checkout.checkoutId, paymentMethod: "pix" });
      setPix(result.pix); setWaiting(true);
    } catch (error) { setStatus(error instanceof Error ? error.message : "Não consegui gerar o Pix."); }
    finally { setSubmitting(false); }
  }

  return <main className="account-page"><section className="account-shell">
    <Link href="/perfil" className="account-back"><ArrowLeft size={18} /> Voltar</Link>
    <div className="checkout-grid">
      <aside className="account-panel plan-summary">
        <span className="account-kicker">Assinatura Luma</span><h1>{plan.name}</h1>
        <p className="checkout-price">{getPlanPrice(planCode, billingInterval)}</p>
        <div className="checkout-billing-switch" role="group" aria-label="Ciclo de cobrança">
          {(["annual", "monthly"] as const).map(interval => <button key={interval} type="button"
            className={billingInterval === interval ? "active" : ""} disabled={waiting || !!pix || submitting}
            onClick={() => router.replace(`/checkout/${planCode}?billing=${interval}`)}>{interval === "annual" ? "Anual" : "Mensal"}</button>)}
        </div>
        <p className="profile-note">{billingInterval === "annual" ? `${plan.annualEquivalent}, com cobrança anual à vista e acesso por 12 meses.` : "Cobrança mensal à vista."}</p>
        <p className="checkout-installment-note">No cartão, cancelar interrompe as próximas renovações e mantém seu acesso pelo período pago. No Pix, você renova quando o período terminar.</p>
        <ul className="checkout-benefits">{plan.benefits.map(benefit => <li key={benefit}><BadgeCheck size={18} />{benefit}</li>)}</ul>
      </aside>
      <div className="account-panel">
        {success ? <div className="checkout-success"><BadgeCheck size={36} /><h2>Pagamento confirmado</h2>
          <p>Seu plano está disponível. A Luma já pode responder pelo WhatsApp vinculado à sua conta.</p>
          <Link href="/perfil" className="account-primary as-link">Ver meu perfil</Link></div> : <>
          <div className="account-heading"><span className="account-kicker">Pagamento seguro</span><h2>Concluir assinatura</h2></div>
          {status && <p role="alert" className="account-status error">{status}</p>}
          {!checkout && !status && <p role="status">Preparando checkout seguro...</p>}
          {checkout && !waiting && !pix && <>
            <div className="checkout-billing-switch" role="group" aria-label="Forma de pagamento">
              <button type="button" className={method === "card" ? "active" : ""} onClick={() => setMethod("card")}>Cartão</button>
              <button type="button" className={method === "pix" ? "active" : ""} onClick={() => setMethod("pix")}>Pix</button>
            </div>
            {method === "card" ? <PaymentCardForm publicKey={checkout.publicKey} amount={checkout.amount} email={checkout.email}
              onSubmit={async form => {
                const result = await submitCheckout(paymentRequest(checkout.checkoutId, form));
                if (result.subscription.status !== "pending") setSuccess(true);
                else setWaiting(true);
              }} /> : <button type="button" className="account-primary" disabled={submitting} onClick={payPix}>{submitting ? "Gerando Pix..." : "Gerar Pix"}</button>}
          </>}
          {waiting && <p role="status" className="account-status info">Aguardando confirmação do pagamento. Seu plano será liberado após a aprovação.</p>}
          {pix && <div className="checkout-pix">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img width="256" height="256" src={"data:image/png;base64," + pix.qrCodeBase64} alt="QR Code para pagar com Pix" />
            <label>Pix Copia e Cola<textarea readOnly value={pix.qrCode} /></label>
            <button type="button" className="account-secondary" onClick={() => { void navigator.clipboard.writeText(pix.qrCode).then(() => setStatus("Código Pix copiado.")); }}><Copy size={16} />Copiar código Pix</button>
            <p>Pix válido por 30 minutos, sem renovação automática.</p>
          </div>}
          {(pix || waiting) && <Link href="/perfil" className="account-back">Acompanhar no meu perfil</Link>}
        </>}
      </div>
    </div>
  </section></main>;
}
