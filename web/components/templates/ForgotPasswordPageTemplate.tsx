"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Mail } from "lucide-react";
import { forgotPassword } from "@/lib/luma-api";

export function ForgotPasswordPageTemplate() {
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setStatus("");
    setSubmitting(true);

    const form = new FormData(event.currentTarget);
    try {
      const result = await forgotPassword({ email: String(form.get("email") || "") });
      setStatus(result.message);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui concluir essa ação agora.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="account-page">
      <section className="account-shell account-shell-narrow">
        <Link href="/login" className="account-back">
          <ArrowLeft size={18} />
          Voltar para login
        </Link>

        <div className="account-panel">
          <div className="account-heading">
            <span className="account-kicker">Senha</span>
            <h1>Recuperar acesso</h1>
            <p>Informe seu e-mail e enviaremos as instruções para criar uma nova senha.</p>
          </div>

          <form className="account-form" onSubmit={handleSubmit}>
            <label>
              E-mail
              <input name="email" type="email" autoComplete="email" required />
            </label>

            <button className="account-primary" type="submit" disabled={submitting}>
              <Mail size={18} />
              {submitting ? "Enviando..." : "Enviar instruções"}
            </button>
            {status && <p className="account-status info">{status}</p>}
          </form>
        </div>
      </section>
    </main>
  );
}
