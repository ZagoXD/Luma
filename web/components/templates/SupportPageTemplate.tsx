"use client";

import { FormEvent, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Send } from "lucide-react";
import { createSupportRequest, getAccountProfile, type AccountProfile } from "@/lib/luma-api";

type SupportPageTemplateProps = {
  accountId: string;
};

const maxFiles = 3;
const maxFileBytes = 5 * 1024 * 1024;
const allowedTypes = new Set(["image/png", "image/jpeg", "application/pdf"]);
const allowedExtensions = [".png", ".jpg", ".jpeg", ".pdf"];
const blockedExtensions = [".exe", ".bat", ".cmd", ".js", ".zip", ".rar", ".7z", ".scr"];

export function SupportPageTemplate({ accountId }: SupportPageTemplateProps) {
  const router = useRouter();
  const [profile, setProfile] = useState<AccountProfile | null>(null);
  const [status, setStatus] = useState("");
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    let active = true;

    async function loadProfile() {
      try {
        const nextProfile = await getAccountProfile();
        if (!active) return;
        if (nextProfile.user.id !== accountId) {
          router.replace(`/perfil/${nextProfile.user.id}/suporte`);
          return;
        }
        setProfile(nextProfile);
      } catch {
        router.replace(`/login?redirect=${encodeURIComponent(`/perfil/${accountId}/suporte`)}`);
      }
    }

    loadProfile();
    return () => {
      active = false;
    };
  }, [accountId, router]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setStatus("");
    setSuccess(false);

    const form = event.currentTarget;
    const formData = new FormData(form);
    const subject = String(formData.get("subject") || "").trim();
    const description = String(formData.get("description") || "").trim();
    const files = formData.getAll("attachments").filter((file): file is File => file instanceof File && file.size > 0);
    const validationError = validateSupportForm(subject, description, files);
    if (validationError) {
      setStatus(validationError);
      return;
    }

    const requestData = new FormData();
    requestData.set("subject", subject);
    requestData.set("description", description);
    files.forEach((file) => requestData.append("attachments", file));

    setSubmitting(true);
    try {
      const result = await createSupportRequest(requestData);
      setStatus(result.message);
      setSuccess(true);
      form.reset();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não foi possível enviar sua solicitação agora. Tente novamente em instantes.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="account-page support-page">
      <section className="account-shell account-shell-narrow support-shell">
        <Link href={`/perfil/${accountId}`} className="account-back">
          <ArrowLeft size={18} />
          Voltar ao perfil
        </Link>

        <div className="account-panel support-panel">
          <div className="account-heading support-heading">
            <span className="account-kicker">Suporte</span>
            <h1>Precisa de ajuda?</h1>
            <p>Descreva o que aconteceu e, se quiser, envie imagens ou PDFs que ajudem a explicar o problema.</p>
          </div>

          <form className="account-form support-form" onSubmit={handleSubmit}>
            <label>
              Assunto
              <input name="subject" maxLength={200} required />
            </label>

            <label>
              Descrição
              <textarea name="description" rows={7} maxLength={5000} required />
            </label>

            <label className="support-file-field">
              Anexos opcionais
              <input className="support-file-input" name="attachments" type="file" accept=".png,.jpg,.jpeg,.pdf,image/png,image/jpeg,application/pdf" multiple />
              <small className="support-help">Até 3 arquivos, com no máximo 5 MB cada. PNG, JPG, JPEG ou PDF.</small>
            </label>

            <button className="account-primary" type="submit" disabled={submitting || !profile}>
              {submitting ? (
                "Enviando solicitação..."
              ) : (
                <>
                  <Send size={18} />
                  Enviar solicitação
                </>
              )}
            </button>

            {status && <p className={`account-status ${success ? "info" : "error"}`}>{status}</p>}
          </form>
        </div>
      </section>
    </main>
  );
}

function validateSupportForm(subject: string, description: string, files: File[]) {
  if (!subject) return "Informe o assunto da solicitação.";
  if (!description) return "Informe a descrição da solicitação.";
  if (files.length > maxFiles) return "Você pode enviar no máximo 3 anexos.";

  for (const file of files) {
    const name = file.name.toLowerCase();
    const hasAllowedExtension = allowedExtensions.some((extension) => name.endsWith(extension));
    const hasBlockedExtension = blockedExtensions.some((extension) => name.endsWith(extension));
    if (file.size > maxFileBytes) return "Cada anexo deve ter no máximo 5 MB.";
    if (hasBlockedExtension || !hasAllowedExtension || !allowedTypes.has(file.type)) {
      return "Formato de arquivo não permitido. Envie apenas PNG, JPG, JPEG ou PDF.";
    }
  }

  return "";
}
