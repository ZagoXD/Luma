"use client";

import Link from "next/link";
import { ArrowRight, ShieldCheck } from "lucide-react";

export async function refreshWaitlistCount() {
  const counterEl = document.getElementById("counter-num");
  if (counterEl) counterEl.textContent = "MVP";
}

export function WaitlistSection() {
  return (
    <section className="cta-final" id="lista">
      <div className="section-inner">
        <div className="fade-up">
          <p className="section-tag" style={{ textAlign: "center" }}>Planos liberados</p>
          <h2 className="section-h2" style={{ color: "#fff", textAlign: "center" }}>
            Crie sua conta e libere
            <br />a <em style={{ color: "var(--lavender)" }}>Luma</em> no WhatsApp.
          </h2>
          <p className="section-lead" style={{ textAlign: "center", margin: "0 auto 2rem" }}>
            A lista de espera saiu do caminho. Agora o MVP usa pré-cadastro, checkout simulado e plano ativo para autorizar a conversa.
          </p>
        </div>

        <div className="launch-actions fade-up" style={{ transitionDelay: "0.1s" }}>
          <Link className="launch-primary" href="/checkout/essencial">
            Quero ativar a Luma
            <ArrowRight size={18} />
          </Link>
          <Link className="launch-secondary" href="/perfil">
            <ShieldCheck size={18} />
            Meu perfil
          </Link>
        </div>
      </div>
    </section>
  );
}
