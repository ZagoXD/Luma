"use client";

import { FormEvent, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, LogIn, UserPlus } from "lucide-react";
import { confirmPhoneVerificationCode, loginAccount, registerAccount, resendPhoneVerificationCode, type AccountUser } from "@/lib/luma-api";
import { formatBrazilPhone, formatCpf, isValidBrazilPhone, isValidCpf } from "@/lib/account-format";

type Mode = "login" | "register";

export function LoginPageTemplate() {
  const router = useRouter();
  const [mode, setMode] = useState<Mode>("login");
  const [redirectTo] = useState(() => {
    if (typeof window === "undefined") return "/perfil";
    return new URLSearchParams(window.location.search).get("redirect") || "/perfil";
  });
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cpf, setCpf] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [pendingVerificationUser, setPendingVerificationUser] = useState<AccountUser | null>(null);
  const [verificationCode, setVerificationCode] = useState("");

  const title = useMemo(
    () => (mode === "login" ? "Entrar na sua conta" : "Criar sua conta"),
    [mode],
  );

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setStatus("");
    setSubmitting(true);

    const form = new FormData(event.currentTarget);
    try {
      if (mode === "register") {
        if (!isValidCpf(cpf)) {
          setStatus("Informe um CPF válido.");
          setSubmitting(false);
          return;
        }

        if (!isValidBrazilPhone(phoneNumber)) {
          setStatus("Informe um celular válido com DDD. Pode ser com ou sem o 9 extra.");
          setSubmitting(false);
          return;
        }

        if (form.get("dataConsentAccepted") !== "on") {
          setStatus("Confirme a autorização de coleta e tratamento dos dados para criar sua conta.");
          setSubmitting(false);
          return;
        }
      }

      if (mode === "login") {
        const result = await loginAccount({
          email: String(form.get("email") || ""),
          password: String(form.get("password") || ""),
        });
        router.push(redirectTo === "/perfil" ? `/perfil/${result.user.id}` : redirectTo);
        return;
      }

      const result = await registerAccount({
            fullName: String(form.get("fullName") || ""),
            email: String(form.get("email") || ""),
            cpf,
            phoneNumber,
            password: String(form.get("password") || ""),
            dataConsentAccepted: true,
          });

      if (!result.user.phoneVerifiedAt) {
        setPendingVerificationUser(result.user);
        setStatus(result.phoneVerificationMessage || "Enviamos um código para o WhatsApp informado.");
        return;
      }

      router.push(redirectTo === "/perfil" ? `/perfil/${result.user.id}` : redirectTo);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui autenticar agora.");
    } finally {
      setSubmitting(false);
    }
  }

  async function handleConfirmVerification(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!pendingVerificationUser) return;

    setStatus("");
    setSubmitting(true);
    try {
      const result = await confirmPhoneVerificationCode({ code: verificationCode });
      router.push(redirectTo === "/perfil" ? `/perfil/${result.user.id}` : redirectTo);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui confirmar o código agora.");
    } finally {
      setSubmitting(false);
    }
  }

  async function handleResendVerification() {
    setStatus("");
    setSubmitting(true);
    try {
      const result = await resendPhoneVerificationCode();
      setStatus(result.message);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui reenviar o código agora.");
    } finally {
      setSubmitting(false);
    }
  }

  if (pendingVerificationUser) {
    return (
      <main className="account-page">
        <section className="account-shell account-shell-narrow">
          <Link href="/" className="account-back">
            <ArrowLeft size={18} />
            Voltar
          </Link>

          <div className="account-panel">
            <div className="account-heading">
              <span className="account-kicker">Confirmação</span>
              <h1>Confirme seu WhatsApp</h1>
              <p>Enviamos um código para {pendingVerificationUser.phoneNumber}. Digite o código para concluir seu cadastro.</p>
            </div>

            <form className="account-form" onSubmit={handleConfirmVerification}>
              <label>
                Código recebido
                <input
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  value={verificationCode}
                  onChange={(event) => setVerificationCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
                  placeholder="000000"
                  maxLength={6}
                  required
                />
              </label>
              <button className="account-primary" type="submit" disabled={submitting || verificationCode.length !== 6}>
                {submitting ? "Confirmando..." : "Confirmar cadastro"}
              </button>
              <button className="account-secondary billing-button" type="button" onClick={handleResendVerification} disabled={submitting}>
                Reenviar código
              </button>
              {status && <p className="account-status info">{status}</p>}
            </form>
          </div>
        </section>
      </main>
    );
  }

  return (
    <main className="account-page">
      <section className="account-shell account-shell-narrow">
        <Link href="/" className="account-back">
          <ArrowLeft size={18} />
          Voltar
        </Link>

        <div className="account-panel">
          <div className="account-heading">
            <span className="account-kicker">Luma</span>
            <h1>{title}</h1>
            <p>Use o mesmo celular que será liberado para conversar com a Luma no WhatsApp.</p>
          </div>

          <div className="account-tabs" role="tablist" aria-label="Autenticação">
            <button className={mode === "login" ? "active" : ""} type="button" onClick={() => setMode("login")}>
              <LogIn size={16} />
              Entrar
            </button>
            <button className={mode === "register" ? "active" : ""} type="button" onClick={() => setMode("register")}>
              <UserPlus size={16} />
              Criar conta
            </button>
          </div>

          <form className="account-form" onSubmit={handleSubmit}>
            {mode === "register" && (
              <>
                <label>
                  Nome completo
                  <input name="fullName" autoComplete="name" required />
                </label>
                <label>
                  CPF
                  <input
                    name="cpf"
                    inputMode="numeric"
                    autoComplete="off"
                    placeholder="000.000.000-00"
                    value={cpf}
                    onChange={(event) => setCpf(formatCpf(event.target.value))}
                    maxLength={14}
                    required
                  />
                </label>
                <label>
                  Celular com DDD
                  <input
                    name="phoneNumber"
                    inputMode="tel"
                    autoComplete="tel"
                    placeholder="(16) 99999-9999"
                    value={phoneNumber}
                    onChange={(event) => setPhoneNumber(formatBrazilPhone(event.target.value))}
                    maxLength={15}
                    required
                  />
                </label>
              </>
            )}

            <label>
              E-mail
              <input name="email" type="email" autoComplete="email" required />
            </label>
            <label>
              Senha
              <input name="password" type="password" autoComplete={mode === "login" ? "current-password" : "new-password"} minLength={8} required />
            </label>

            {mode === "register" && (
              <label className="account-consent">
                <input name="dataConsentAccepted" type="checkbox" required />
                <span>
                  Confirmo que autorizo a coleta e o tratamento dos dados necessários para minha conta, ciclo,
                  saúde menstrual, gravidez e uso da Luma, conforme a política de privacidade.
                </span>
              </label>
            )}

            <button className="account-primary" type="submit" disabled={submitting}>
              {submitting ? "Aguarde..." : mode === "login" ? "Entrar" : "Criar conta"}
            </button>
            {mode === "login" && (
              <Link href="/forgot-password" className="account-inline-link">
                Esqueci minha senha
              </Link>
            )}
            {status && <p className="account-status error">{status}</p>}
          </form>
        </div>
      </section>
    </main>
  );
}
