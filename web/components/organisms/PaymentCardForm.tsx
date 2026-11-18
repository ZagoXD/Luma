"use client";

import { useEffect, useId, useRef, useState } from "react";
import type { BrickSubmission } from "@/lib/billing";

type BrickController = { unmount(): Promise<void> };
type BrickSettings = {
  initialization: { amount: number; payer: { email: string } };
  customization: {
    paymentMethods: { minInstallments: number; maxInstallments: number };
    visual: { texts: { formSubmit: string } };
  };
  callbacks: { onReady(): void; onError(): void; onSubmit(form: BrickSubmission): Promise<void> };
};
declare global {
  interface Window {
    MercadoPago?: new (key: string, options: { locale: string }) => {
      bricks(): { create(type: string, id: string, settings: BrickSettings): Promise<BrickController> };
    };
  }
}

let sdkPromise: Promise<void> | undefined;
function loadSdk() {
  if (window.MercadoPago) return Promise.resolve();
  if (!sdkPromise) sdkPromise = new Promise<void>((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "https://sdk.mercadopago.com/js/v2";
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => { sdkPromise = undefined; script.remove(); reject(new Error("Não consegui carregar o pagamento seguro.")); };
    document.head.appendChild(script);
  });
  return sdkPromise;
}

export function PaymentCardForm({ publicKey, amount, email, onSubmit, updatingCard = false }: {
  publicKey: string; amount: number; email: string; updatingCard?: boolean;
  onSubmit(form: BrickSubmission): Promise<void>;
}) {
  const id = `payment-${useId().replace(/[^a-zA-Z0-9]/g, "")}`;
  const submit = useRef(onSubmit);
  const [error, setError] = useState("");
  const [ready, setReady] = useState(false);
  useEffect(() => { submit.current = onSubmit; }, [onSubmit]);
  useEffect(() => {
    let disposed = false;
    let controller: BrickController | undefined;
    loadSdk().then(async () => {
      if (disposed || !window.MercadoPago) return;
      const sdk = new window.MercadoPago(publicKey, { locale: "pt-BR" });
      controller = await sdk.bricks().create("cardPayment", id, {
        initialization: { amount, payer: { email } },
        customization: {
          paymentMethods: { minInstallments: 1, maxInstallments: 1 },
          visual: { texts: { formSubmit: updatingCard ? "Atualizar cartão" : "Assinar plano" } },
        },
        callbacks: {
          onReady: () => { if (!disposed) setReady(true); },
          onError: () => { if (!disposed) setError("Não consegui carregar os campos do cartão. Atualize a página para tentar novamente."); },
          onSubmit: async (form) => {
            setError("");
            try { await submit.current(form); }
            catch (cause) {
              setError(cause instanceof Error ? cause.message : "Não foi possível confirmar a operação.");
              throw cause;
            }
          },
        },
      });
      if (disposed) await controller.unmount();
    }).catch(() => { if (!disposed) setError("Não consegui carregar o pagamento seguro. Atualize a página para tentar novamente."); });
    return () => { disposed = true; if (controller) void controller.unmount(); };
  }, [publicKey, amount, email, id, updatingCard]);
  return <div className="payment-element-shell">
    {!ready && !error && <p role="status">Preparando pagamento seguro...</p>}
    <div id={id} />
    {error && <p className="account-status error" role="alert">{error}</p>}
  </div>;
}
