"use client";

import Link from "next/link";
import { ArrowRight, ShieldCheck } from "lucide-react";

export async function refreshWaitlistCount() {
  const counterEl = document.getElementById("counter-num");
  if (counterEl) counterEl.textContent = "WhatsApp";
}

export function WaitlistSection() {
  return (
    <section className="cta-final" id="acesso">
      <div className="section-inner">
        <div className="fade-up">
          <p className="section-tag" style={{ textAlign: "center" }}>Acesso liberado</p>
          <h2 className="section-h2" style={{ color: "#fff", textAlign: "center" }}>
            Crie sua conta e libere
            <br />a <em style={{ color: "var(--lavender)" }}>Luma</em> no WhatsApp.
          </h2>
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
