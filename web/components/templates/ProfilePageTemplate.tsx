"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  ArrowLeft,
  Bell,
  CalendarDays,
  CreditCard,
  LifeBuoy,
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
  getBillingTransactions,
  getAccountProfile,
  getNotificationPreferences,
  logoutAccount,
  plans,
  resumeSubscription,
  updateNotificationPreferences,
  type AccountProfile,
  type BillingInterval,
  type BillingTransaction,
  type NotificationPreference,
  type PlanCode,
  getPlanPrice,
  requestPhoneChange,
  confirmPhoneChange,
} from "@/lib/luma-api";
import { formatBrazilPhone, formatBrazilPhoneDisplay, isValidBrazilPhone } from "@/lib/account-format";

type ProfilePageTemplateProps = {
  accountId: string;
};

const defaultNotificationPreference: NotificationPreference = {
  periodReminderEnabled: false,
  contraceptiveReminderEnabled: false,
  symptomCheckinEnabled: false,
  reminderTime: "09:00",
  periodReminderTime: "09:00",
  contraceptiveReminderTime: "09:00",
  symptomCheckinTime: "09:00",
  timeZone: "America/Sao_Paulo",
};

export function ProfilePageTemplate({ accountId }: ProfilePageTemplateProps) {
  const router = useRouter();
  const lumaWhatsAppNumber = process.env.NEXT_PUBLIC_LUMA_WHATSAPP_NUMBER || "+14155238886";
  const lumaWhatsAppLink = buildWhatsAppLink(lumaWhatsAppNumber);
  const [profile, setProfile] = useState<AccountProfile | null>(null);
  const [transactions, setTransactions] = useState<BillingTransaction[]>([]);
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
  const [editingPhone, setEditingPhone] = useState(false);
  const [newPhoneNumber, setNewPhoneNumber] = useState("");
  const [phoneChangeCode, setPhoneChangeCode] = useState("");
  const [phoneChangeWaitingCode, setPhoneChangeWaitingCode] = useState(false);
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
        const billingHistory = await getBillingTransactions();
        if (!active) return;
        setTransactions(billingHistory.transactions);
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

  async function handleChangePlan(planCode: PlanCode, billingInterval?: BillingInterval) {
    setStatus("");
    const actionKey = `plan-${planCode}-${billingInterval || "same"}`;
    setBillingAction(actionKey);
    try {
      const result = await changeSubscriptionPlan({ planCode, billingInterval });
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

  async function handleRequestPhoneChange() {
    setStatus("");
    if (!isValidBrazilPhone(newPhoneNumber)) {
      setStatus("Informe um celular válido com DDD.");
      return;
    }

    setBillingAction("phone-request");
    try {
      const result = await requestPhoneChange({ phoneNumber: newPhoneNumber });
      setPhoneChangeWaitingCode(true);
      setStatus(result.message);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui enviar o código agora.");
    } finally {
      setBillingAction("");
    }
  }

  async function handleConfirmPhoneChange() {
    setStatus("");
    setBillingAction("phone-confirm");
    try {
      const result = await confirmPhoneChange({ phoneNumber: newPhoneNumber, code: phoneChangeCode });
      setProfile((current) => current && { ...current, user: result.user });
      setEditingPhone(false);
      setPhoneChangeWaitingCode(false);
      setNewPhoneNumber("");
      setPhoneChangeCode("");
      setStatus(result.message);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Não consegui confirmar o novo celular agora.");
    } finally {
      setBillingAction("");
    }
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
  const hasEssentialPlan = subscription?.planCode === "essencial" && (subscription.status === "active" || subscription.status === "canceled");
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
            <p>Gerencie sua conta, assinatura, WhatsApp, calendário e recursos da Luma.</p>
          </div>

        <div className="profile-grid">
          <section className="account-panel profile-card">
            <h2><User size={20} /> Conta</h2>
            <dl className="profile-list">
              <div><dt>E-mail</dt><dd>{profile?.user.email}</dd></div>
              <div>
                <dt>Celular</dt>
                <dd>
                  {profile?.user.phoneNumber ? formatBrazilPhoneDisplay(profile.user.phoneNumber) : ""}
                  {profile?.user.phoneVerifiedAt ? " confirmado" : " pendente"}
                </dd>
              </div>
              <div><dt>CPF</dt><dd>{profile?.user.cpf}</dd></div>
            </dl>
            {editingPhone ? (
              <div className="phone-change-panel">
                <label>
                  Novo celular
                  <input
                    inputMode="tel"
                    value={newPhoneNumber}
                    onChange={(event) => setNewPhoneNumber(formatBrazilPhone(event.target.value))}
                    placeholder="(16) 99999-9999"
                    maxLength={15}
                  />
                </label>
                {phoneChangeWaitingCode && (
                  <label>
                    Código recebido
                    <input
                      inputMode="numeric"
                      autoComplete="one-time-code"
                      value={phoneChangeCode}
                      onChange={(event) => setPhoneChangeCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
                      placeholder="000000"
                      maxLength={6}
                    />
                  </label>
                )}
                <div className="billing-actions">
                  <button className="account-primary" type="button" onClick={phoneChangeWaitingCode ? handleConfirmPhoneChange : handleRequestPhoneChange} disabled={billingAction.startsWith("phone-")}>
                    {phoneChangeWaitingCode ? "Confirmar novo número" : "Enviar código"}
                  </button>
                  <button className="account-secondary billing-button" type="button" onClick={() => setEditingPhone(false)}>
                    Cancelar
                  </button>
                </div>
              </div>
            ) : (
              <button className="account-secondary billing-button" type="button" onClick={() => setEditingPhone(true)}>
                Editar número
              </button>
            )}
          </section>

          <section className="account-panel profile-card">
            <h2><CreditCard size={20} /> Plano</h2>
            {subscription && plan ? (
              <>
                <p className="plan-pill">{plan.name} - {getPlanPrice(subscription.planCode, subscription.billingInterval)}</p>
                <dl className="profile-list">
                  <div><dt>Status</dt><dd>{formatSubscriptionStatus(subscription.status)}</dd></div>
                  <div><dt>Cobrança</dt><dd>{subscription.billingLabel}</dd></div>
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
                      onClick={() => handleChangePlan(nextPlanCode, subscription.billingInterval)}
                      disabled={billingAction === `plan-${nextPlanCode}-${subscription.billingInterval}`}
                    >
                      <Repeat2 size={16} />
                      {billingAction === `plan-${nextPlanCode}-${subscription.billingInterval}` ? "Trocando..." : `Trocar para ${plans[nextPlanCode].name}`}
                    </button>
                  )}
                  <button
                    className="account-secondary billing-button"
                    type="button"
                    onClick={() => handleChangePlan(subscription.planCode, subscription.billingInterval === "annual" ? "monthly" : "annual")}
                    disabled={billingAction.startsWith(`plan-${subscription.planCode}-`)}
                  >
                    <Repeat2 size={16} />
                    {subscription.billingInterval === "annual" ? "Trocar para mensal" : "Trocar para anual"}
                  </button>
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
                  <Link className="account-primary as-link" href="/checkout/essencial?billing=annual">Essencial anual</Link>
                </div>
              </>
            )}
          </section>

          <section className="account-panel profile-card">
            <h2><CreditCard size={20} /> Histórico de transações</h2>
            {transactions.length > 0 ? (
              <div className="transaction-list">
                {transactions.map((transaction) => (
                  <div className="transaction-item" key={transaction.id}>
                    <span>
                      <strong>{formatMoney(transaction.amountPaid, transaction.currency)}</strong>
                      <small>{formatDate(transaction.created)} - {formatInvoiceStatus(transaction.status)}</small>
                    </span>
                    {transaction.hostedInvoiceUrl && (
                      <a href={transaction.hostedInvoiceUrl} target="_blank" rel="noreferrer">Ver fatura</a>
                    )}
                  </div>
                ))}
              </div>
            ) : (
              <p>Quando houver cobranças confirmadas pela Stripe, elas aparecerão aqui.</p>
            )}
          </section>

          <section className="account-panel profile-card">
            <h2><CalendarDays size={20} /> Dados menstruais</h2>
            {profile?.menstrual ? (
              <>
                <dl className="profile-list">
                  <div><dt>Nome na conversa</dt><dd>{profile.menstrual.displayName || "Não informado"}</dd></div>
                  <div><dt>Última menstruação</dt><dd>{formatDateOnly(profile.menstrual.lastPeriodStartDate)}</dd></div>
                  <div><dt>Ciclo médio</dt><dd>{formatDays(profile.menstrual.averageCycleLength)}</dd></div>
                  <div><dt>Duração média</dt><dd>{formatDays(profile.menstrual.averagePeriodLength)}</dd></div>
                  <div><dt>Contraceptivo</dt><dd>{formatContraceptive(profile.menstrual.contraceptiveType)}</dd></div>
                </dl>
                <Link className="account-primary as-link profile-calendar-link" href={`/perfil/${accountId}/calendario`}>
                  <CalendarDays size={16} />
                  Abrir calendário
                </Link>
              </>
            ) : (
              <p>A Luma ainda não recebeu dados menstruais pelo WhatsApp para este celular.</p>
            )}
          </section>

          <section className="account-panel profile-card">
            <h2><MessageCircle size={20} /> WhatsApp</h2>
              <p>A Luma responde ao número cadastrado quando existe plano ativo ou cancelado ainda dentro do período pago.</p>
            <a className="luma-whatsapp-link" href={lumaWhatsAppLink} target="_blank" rel="noreferrer">
              <MessageCircle size={18} />
              <span>
                <strong>Número da Luma</strong>
                {formatWhatsAppNumberDisplay(lumaWhatsAppNumber)}
              </span>
            </a>
            <p className="profile-note">Depois de ativar um plano, envie uma mensagem pelo WhatsApp cadastrado.</p>
          </section>

          <section className="account-panel profile-card">
            <h2><LifeBuoy size={20} /> Precisa de ajuda?</h2>
            <p>Envie uma solicitação para nossa equipe de suporte. Você pode incluir imagens ou PDFs para explicar melhor o problema.</p>
            <Link className="account-primary as-link profile-calendar-link" href={`/perfil/${accountId}/suporte`}>
              <LifeBuoy size={16} />
              Abrir suporte
            </Link>
          </section>

          <section className="account-panel profile-card notification-card">
            <h2><Bell size={20} /> Recursos Essenciais</h2>
            {hasEssentialPlan && notificationsAvailable ? (
              <>
                <div className="notification-setting">
                  <label className="notification-toggle">
                    <input
                      type="checkbox"
                      checked={notificationPreference.periodReminderEnabled}
                      onChange={(event) => setNotificationPreference((current) => ({ ...current, periodReminderEnabled: event.target.checked }))}
                    />
                    Avisos de previsão menstrual
                  </label>
                  <label className="notification-time">
                    Horário
                    <input
                      type="time"
                      value={notificationPreference.periodReminderTime}
                      onChange={(event) => setNotificationPreference((current) => ({ ...current, periodReminderTime: event.target.value, reminderTime: event.target.value }))}
                    />
                  </label>
                </div>
                <div className="notification-setting">
                  <label className="notification-toggle">
                    <input
                      type="checkbox"
                      checked={notificationPreference.contraceptiveReminderEnabled}
                      onChange={(event) => setNotificationPreference((current) => ({ ...current, contraceptiveReminderEnabled: event.target.checked }))}
                    />
                    Lembrete de anticoncepcional
                  </label>
                  <label className="notification-time">
                    Horário
                    <input
                      type="time"
                      value={notificationPreference.contraceptiveReminderTime}
                      onChange={(event) => setNotificationPreference((current) => ({ ...current, contraceptiveReminderTime: event.target.value, reminderTime: event.target.value }))}
                    />
                  </label>
                </div>
                <div className="notification-setting">
                  <label className="notification-toggle">
                    <input
                      type="checkbox"
                      checked={notificationPreference.symptomCheckinEnabled}
                      onChange={(event) => setNotificationPreference((current) => ({ ...current, symptomCheckinEnabled: event.target.checked }))}
                    />
                    Check-in de sintomas
                  </label>
                  <label className="notification-time">
                    Horário
                    <input
                      type="time"
                      value={notificationPreference.symptomCheckinTime}
                      onChange={(event) => setNotificationPreference((current) => ({ ...current, symptomCheckinTime: event.target.value, reminderTime: event.target.value }))}
                    />
                  </label>
                </div>
                <button className="account-primary" type="button" onClick={handleSaveNotifications} disabled={savingNotifications}>
                  <Save size={16} />
                  {savingNotifications ? "Salvando..." : "Salvar notificações"}
                </button>
                {notificationStatus && <p className="account-status info">{notificationStatus}</p>}
                <p className="profile-note">Seu plano também libera mensagens por áudio e imagens educativas do bebê pelo WhatsApp.</p>
              </>
            ) : (
              <p>Notificações automáticas, mensagens por áudio e imagens educativas ficam disponíveis no plano Essencial depois da primeira conversa com a Luma no WhatsApp.</p>
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

function formatInvoiceStatus(status: string) {
  const labels: Record<string, string> = {
    paid: "Pago",
    open: "Aberto",
    draft: "Rascunho",
    void: "Cancelado",
    uncollectible: "Não cobrado",
  };

  return labels[status] || status;
}

function formatMoney(amountInCents: number, currency: string) {
  return new Intl.NumberFormat("pt-BR", {
    style: "currency",
    currency: currency || "BRL",
  }).format(amountInCents / 100);
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
