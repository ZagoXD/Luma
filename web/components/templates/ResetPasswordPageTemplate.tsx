"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { ArrowLeft, KeyRound } from "lucide-react";
import { resetPassword } from "@/lib/luma-api";

type ResetPasswordPageTemplateProps = {
  token: string;
};

export function ResetPasswordPageTemplate({ token }: ResetPasswordPageTemplateProps) {
  const [status, setStatus] = useState("");
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setStatus("");
    setSubmitting(true);

    const form = new FormData(event.currentTarget);
    const newPassword = String(form.get("newPassword") || "");
    const confirmPassword = String(form.get("confirmPassword") || "");
    if (newPassword.length < 8) {
      setStatus("A senha precisa ter pelo menos 8 caracteres.");
      setSubmitting(false);
      return;
    }

    if (newPassword !== confirmPassword) {
      setStatus("As senhas não coincidem.");
      setSubmitting(false);
      return;
    }

    try {
      const result = await resetPassword({ token, newPassword });
      setSuccess(true);
      setStatus(result.message);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui redefinir sua senha agora.");
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
            <h1>Definir nova senha</h1>
            <p>Crie uma nova senha para acessar sua conta Luma.</p>
          </div>

          {!token ? (
            <p className="account-status error">Link de recuperação inválido ou expirado.</p>
          ) : (
            <form className="account-form" onSubmit={handleSubmit}>
              <label>
                Nova senha
                <input name="newPassword" type="password" autoComplete="new-password" minLength={8} required disabled={success} />
              </label>
              <label>
                Confirmar nova senha
                <input name="confirmPassword" type="password" autoComplete="new-password" minLength={8} required disabled={success} />
              </label>

              {!success && (
                <button className="account-primary" type="submit" disabled={submitting}>
                  <KeyRound size={18} />
                  {submitting ? "Redefinindo..." : "Redefinir senha"}
                </button>
              )}

              {status && <p className={`account-status ${success ? "info" : "error"}`}>{status}</p>}
              {success && (
                <Link href="/login" className="account-secondary billing-button">
                  Ir para login
                </Link>
              )}
            </form>
          )}
        </div>
      </section>
    </main>
  );
}
