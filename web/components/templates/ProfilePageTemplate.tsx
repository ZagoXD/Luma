"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  ArrowLeft,
  Bell,
  CalendarDays,
  CreditCard,
  LogOut,
  MessageCircle,
  RefreshCcw,
  Repeat2,
  Save,
  User,
} from "lucide-react";
import { Elements } from "@stripe/react-stripe-js";
import { loadStripe, type StripeElementsOptions } from "@stripe/stripe-js";
import { StripePaymentMethodForm } from "@/components/organisms/StripePaymentMethodForm";
import {
  cancelSubscription,
  changeSubscriptionPlan,
  createPaymentMethodSetupIntent,
  getAccountProfile,
  getNotificationPreferences,
  logoutAccount,
  plans,
  resumeSubscription,
  updateNotificationPreferences,
  type AccountProfile,
  type NotificationPreference,
  type PlanCode,
} from "@/lib/luma-api";
import { formatBrazilPhoneDisplay } from "@/lib/account-format";

type ProfilePageTemplateProps = {
  accountId: string;
};

const defaultNotificationPreference: NotificationPreference = {
  periodReminderEnabled: false,
  contraceptiveReminderEnabled: false,
  symptomCheckinEnabled: false,
  reminderTime: "09:00",
  timeZone: "America/Sao_Paulo",
};

export function ProfilePageTemplate({ accountId }: ProfilePageTemplateProps) {
  const router = useRouter();
  const lumaWhatsAppNumber = process.env.NEXT_PUBLIC_LUMA_WHATSAPP_NUMBER || "+14155238886";
  const lumaWhatsAppLink = buildWhatsAppLink(lumaWhatsAppNumber);
  const [profile, setProfile] = useState<AccountProfile | null>(null);
  const [notificationPreference, setNotificationPreference] = useState<NotificationPreference>(defaultNotificationPreference);
  const [notificationsAvailable, setNotificationsAvailable] = useState(false);
  const [status, setStatus] = useState("");
  const [notificationStatus, setNotificationStatus] = useState("");
  const [loading, setLoading] = useState(true);
  const [canceling, setCanceling] = useState(false);
  const [savingNotifications, setSavingNotifications] = useState(false);
  const [billingAction, setBillingAction] = useState("");
  const [setupClientSecret, setSetupClientSecret] = useState("");
  const [setupIntentId, setSetupIntentId] = useState("");
  const [setupPublishableKey, setSetupPublishableKey] = useState("");
  const stripePromise = useMemo(
    () => setupPublishableKey ? loadStripe(setupPublishableKey) : null,
    [setupPublishableKey],
  );

  useEffect(() => {
    let active = true;

    async function loadProfile() {
      try {
        const nextProfile = await getAccountProfile();
        if (!active) return;

        if (nextProfile.user.id !== accountId) {
          router.replace(`/perfil/${nextProfile.user.id}`);
          return;
        }

        setProfile(nextProfile);
        const preferences = await getNotificationPreferences();
        if (!active) return;

        setNotificationsAvailable(preferences.available);
        setNotificationPreference(preferences.preference || defaultNotificationPreference);
      } catch (error) {
        setStatus(error instanceof Error ? error.message : "Não consegui carregar seu perfil.");
        router.replace(`/login?redirect=${encodeURIComponent(`/perfil/${accountId}`)}`);
      } finally {
        if (active) setLoading(false);
      }
    }

    loadProfile();

    return () => {
      active = false;
    };
  }, [accountId, router]);

  async function handleCancel() {
    setStatus("");
    setCanceling(true);
    try {
      const result = await cancelSubscription();
      setProfile((current) => current && { ...current, subscription: result.subscription });
      setStatus("Assinatura cancelada. Seu acesso continua disponível até o fim do período já pago.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui cancelar agora.");
    } finally {
      setCanceling(false);
    }
  }

  async function handleResume() {
    setStatus("");
    setBillingAction("resume");
    try {
      const result = await resumeSubscription();
      setProfile((current) => current && { ...current, subscription: result.subscription });
      setStatus("Assinatura retomada. A próxima renovação seguirá normalmente.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui retomar a assinatura agora.");
    } finally {
      setBillingAction("");
    }
  }

  async function handleChangePlan(planCode: PlanCode) {
    setStatus("");
    setBillingAction(`plan-${planCode}`);
    try {
      const result = await changeSubscriptionPlan({ planCode });
      setProfile((current) => current && { ...current, subscription: result.subscription });
      setStatus(`Plano alterado para ${plans[planCode].name}. A Stripe calculará ajustes proporcionais quando aplicável.`);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui trocar o plano agora.");
    } finally {
      setBillingAction("");
    }
  }

  async function handleStartPaymentMethodUpdate() {
    setStatus("");
    setBillingAction("card");
    try {
      const result = await createPaymentMethodSetupIntent();
      setSetupPublishableKey(result.publishableKey);
      setSetupClientSecret(result.clientSecret);
      setSetupIntentId(result.setupIntentId);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui abrir a atualização de cartão.");
    } finally {
      setBillingAction("");
    }
  }

  async function handleSaveNotifications() {
    setNotificationStatus("");
    setSavingNotifications(true);
    try {
      const result = await updateNotificationPreferences(notificationPreference);
      setNotificationPreference(result.preference);
      setNotificationStatus("Preferências de notificação salvas.");
    } catch (error) {
      setNotificationStatus(error instanceof Error ? error.message : "Não consegui salvar as notificações agora.");
    } finally {
      setSavingNotifications(false);
    }
  }

  async function handleLogout() {
    await logoutAccount();
    router.push("/");
  }

  if (loading) {
    return (
      <main className="account-page">
        <section className="account-shell account-shell-narrow">
          <div className="account-panel">Carregando perfil...</div>
        </section>
      </main>
    );
  }

  const subscription = profile?.subscription;
  const plan = subscription ? plans[subscription.planCode] : null;
  const hasEssentialPlan = subscription?.planCode === "essencial" && subscription.status === "active";
  const nextPlanCode: PlanCode | null = subscription?.planCode === "basico"
    ? "essencial"
    : subscription?.planCode === "essencial"
      ? "basico"
      : null;
  const setupOptions: StripeElementsOptions | undefined = setupClientSecret
    ? {
        clientSecret: setupClientSecret,
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
        <div className="profile-topbar">
          <Link href="/" className="account-back">
            <ArrowLeft size={18} />
            Voltar
          </Link>
          <button className="account-secondary" type="button" onClick={handleLogout}>
            <LogOut size={16} />
            Sair
          </button>
        </div>

        <div className="account-heading profile-heading">
          <span className="account-kicker">Meu perfil</span>
          <h1>{profile?.user.fullName}</h1>
          <p>Configurações de conta, assinatura, WhatsApp e lembretes da Luma.</p>
        </div>

        <div className="profile-grid">
          <section className="account-panel profile-card">
            <h2><User size={20} /> Conta</h2>
            <dl className="profile-list">
              <div><dt>E-mail</dt><dd>{profile?.user.email}</dd></div>
              <div><dt>Celular</dt><dd>{profile?.user.phoneNumber ? formatBrazilPhoneDisplay(profile.user.phoneNumber) : ""}</dd></div>
              <div><dt>CPF</dt><dd>{profile?.user.cpf}</dd></div>
            </dl>
          </section>

          <section className="account-panel profile-card">
            <h2><CreditCard size={20} /> Plano</h2>
            {subscription && plan ? (
              <>
                <p className="plan-pill">{plan.name} - {plan.price}</p>
                <dl className="profile-list">
                  <div><dt>Status</dt><dd>{formatSubscriptionStatus(subscription.status)}</dd></div>
                  <div><dt>Acesso até</dt><dd>{formatDate(subscription.currentPeriodEndsAt)}</dd></div>
                </dl>
                <div className="billing-actions">
                  {subscription.status === "canceled" ? (
                    <button className="account-primary" type="button" onClick={handleResume} disabled={billingAction === "resume"}>
                      <RefreshCcw size={16} />
                      {billingAction === "resume" ? "Retomando..." : "Retomar assinatura"}
                    </button>
                  ) : (
                    <button className="account-secondary danger" type="button" onClick={handleCancel} disabled={canceling}>
                      {canceling ? "Cancelando..." : "Cancelar assinatura"}
                    </button>
                  )}
                  {nextPlanCode && (
                    <button
                      className="account-secondary billing-button"
                      type="button"
                      onClick={() => handleChangePlan(nextPlanCode)}
                      disabled={billingAction === `plan-${nextPlanCode}`}
                    >
                      <Repeat2 size={16} />
                      {billingAction === `plan-${nextPlanCode}` ? "Trocando..." : `Trocar para ${plans[nextPlanCode].name}`}
                    </button>
                  )}
                  <button className="account-secondary billing-button" type="button" onClick={handleStartPaymentMethodUpdate} disabled={billingAction === "card"}>
                    <CreditCard size={16} />
                    {billingAction === "card" ? "Abrindo..." : "Trocar cartão"}
                  </button>
                </div>
                {stripePromise && setupOptions && setupIntentId && (
                  <div className="payment-method-panel">
                    <p>Informe o novo cartão. A Stripe salvará esse método como padrão para as próximas faturas.</p>
                    <Elements stripe={stripePromise} options={setupOptions}>
                      <StripePaymentMethodForm
                        setupIntentId={setupIntentId}
                        onSuccess={() => {
                          setSetupClientSecret("");
                          setSetupIntentId("");
                          setSetupPublishableKey("");
                          setStatus("Cartão atualizado para as próximas cobranças.");
                        }}
                      />
                    </Elements>
                  </div>
                )}
              </>
            ) : (
              <>
                <p>Você ainda não tem um plano ativo. Escolha um plano para liberar a Luma no WhatsApp.</p>
                <div className="profile-plan-actions">
                  <Link className="account-primary as-link" href="/checkout/basico">Básico</Link>
                  <Link className="account-primary as-link" href="/checkout/essencial">Essencial</Link>
                </div>
              </>
            )}
          </section>

          <section className="account-panel profile-card">
            <h2><CalendarDays size={20} /> Dados menstruais</h2>
            {profile?.menstrual ? (
              <dl className="profile-list">
                <div><dt>Nome na conversa</dt><dd>{profile.menstrual.displayName || "Não informado"}</dd></div>
                <div><dt>Última menstruação</dt><dd>{formatDateOnly(profile.menstrual.lastPeriodStartDate)}</dd></div>
                <div><dt>Ciclo médio</dt><dd>{formatDays(profile.menstrual.averageCycleLength)}</dd></div>
                <div><dt>Duração média</dt><dd>{formatDays(profile.menstrual.averagePeriodLength)}</dd></div>
                <div><dt>Contraceptivo</dt><dd>{formatContraceptive(profile.menstrual.contraceptiveType)}</dd></div>
              </dl>
            ) : (
              <p>A Luma ainda não recebeu dados menstruais pelo WhatsApp para este celular.</p>
            )}
          </section>

          <section className="account-panel profile-card">
            <h2><MessageCircle size={20} /> WhatsApp</h2>
            <p>A Luma só responde números com plano ativo ou cancelado ainda dentro do período pago.</p>
            <a className="luma-whatsapp-link" href={lumaWhatsAppLink} target="_blank" rel="noreferrer">
              <MessageCircle size={18} />
              <span>
                <strong>Número da Luma</strong>
                {formatWhatsAppNumberDisplay(lumaWhatsAppNumber)}
              </span>
            </a>
            <p className="profile-note">Depois de ativar um plano, envie uma mensagem pelo WhatsApp cadastrado.</p>
          </section>

          <section className="account-panel profile-card notification-card">
            <h2><Bell size={20} /> Notificações</h2>
            {hasEssentialPlan && notificationsAvailable ? (
              <>
                <label className="notification-toggle">
                  <input
                    type="checkbox"
                    checked={notificationPreference.periodReminderEnabled}
                    onChange={(event) => setNotificationPreference((current) => ({ ...current, periodReminderEnabled: event.target.checked }))}
                  />
                  Avisos de previsão menstrual
                </label>
                <label className="notification-toggle">
                  <input
                    type="checkbox"
                    checked={notificationPreference.contraceptiveReminderEnabled}
                    onChange={(event) => setNotificationPreference((current) => ({ ...current, contraceptiveReminderEnabled: event.target.checked }))}
                  />
                  Lembrete de anticoncepcional
                </label>
                <label className="notification-toggle">
                  <input
                    type="checkbox"
                    checked={notificationPreference.symptomCheckinEnabled}
                    onChange={(event) => setNotificationPreference((current) => ({ ...current, symptomCheckinEnabled: event.target.checked }))}
                  />
                  Check-in de sintomas
                </label>
                <label className="notification-time">
                  Horário preferido
                  <input
                    type="time"
                    value={notificationPreference.reminderTime}
                    onChange={(event) => setNotificationPreference((current) => ({ ...current, reminderTime: event.target.value }))}
                  />
                </label>
                <button className="account-primary" type="button" onClick={handleSaveNotifications} disabled={savingNotifications}>
                  <Save size={16} />
                  {savingNotifications ? "Salvando..." : "Salvar notificações"}
                </button>
                {notificationStatus && <p className="account-status info">{notificationStatus}</p>}
              </>
            ) : (
              <p>As notificações automáticas ficam disponíveis no plano Essencial depois da primeira conversa com a Luma no WhatsApp.</p>
            )}
          </section>
        </div>

        {status && <p className="account-status info">{status}</p>}
      </section>
    </main>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("pt-BR", { dateStyle: "short" }).format(new Date(value));
}

function formatDateOnly(value?: string | null) {
  if (!value) return "Não informado";
  return new Intl.DateTimeFormat("pt-BR", { timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
}

function formatDays(value?: number | null) {
  return value ? `${value} dias` : "Não informado";
}

function formatSubscriptionStatus(status: "active" | "canceled" | "pending") {
  const labels = {
    active: "Ativo",
    canceled: "Cancelado",
    pending: "Pagamento pendente",
  };

  return labels[status];
}

function formatContraceptive(value?: string | null) {
  const labels: Record<string, string> = {
    none: "Não usa",
    pill: "Pílula",
    injection: "Injeção",
    hormonal_iud: "DIU hormonal",
    copper_iud: "DIU de cobre",
    implant: "Implante",
    condom: "Camisinha",
    other: "Outro",
    prefer_not_say: "Prefere não informar",
  };

  return value ? labels[value] || value : "Não informado";
}

function buildWhatsAppLink(phoneNumber: string) {
  const digits = phoneNumber.replace(/\D/g, "");
  const text = encodeURIComponent("Olá, Luma");
  return `https://wa.me/${digits}?text=${text}`;
}

function formatWhatsAppNumberDisplay(phoneNumber: string) {
  const digits = phoneNumber.replace(/\D/g, "");
  if (digits.startsWith("55")) {
    return formatBrazilPhoneDisplay(phoneNumber);
  }

  if (digits.startsWith("1") && digits.length === 11) {
    return `+1 (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7)}`;
  }

  return phoneNumber;
}
