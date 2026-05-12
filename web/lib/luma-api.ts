export type PlanCode = "basico" | "essencial";
export type BillingInterval = "monthly" | "annual";

export type AccountUser = {
  id: string;
  email: string;
  cpf: string;
  fullName: string;
  phoneNumber: string;
  phoneVerifiedAt?: string | null;
  createdAt: string;
};

export type AccountSubscription = {
  id: string;
  planCode: PlanCode;
  planName: string;
  billingInterval: BillingInterval;
  billingLabel: string;
  status: "active" | "canceled" | "pending";
  stripeSubscriptionId?: string | null;
  stripePriceId?: string | null;
  startsAt: string;
  currentPeriodEndsAt: string;
  canceledAt?: string | null;
};

export type BillingTransaction = {
  id: string;
  number?: string | null;
  status: string;
  currency: string;
  amountPaid: number;
  amountDue: number;
  hostedInvoiceUrl?: string | null;
  invoicePdf?: string | null;
  created: string;
};

export type StripeSubscriptionIntent = {
  publishableKey: string;
  clientSecret: string;
  stripeSubscriptionId: string;
};

export type StripeSetupIntent = {
  publishableKey: string;
  clientSecret: string;
  setupIntentId: string;
};

export type MenstrualSummary = {
  displayName?: string | null;
  onboardingStep: string;
  isAdultConfirmed?: boolean | null;
  lastPeriodStartDate?: string | null;
  averageCycleLength?: number | null;
  averagePeriodLength?: number | null;
  contraceptiveType?: string | null;
};

export type AccountProfile = {
  user: AccountUser;
  subscription?: AccountSubscription | null;
  menstrual?: MenstrualSummary | null;
};

export type NotificationPreference = {
  periodReminderEnabled: boolean;
  contraceptiveReminderEnabled: boolean;
  symptomCheckinEnabled: boolean;
  reminderTime: string;
  periodReminderTime: string;
  contraceptiveReminderTime: string;
  symptomCheckinTime: string;
  timeZone: string;
};

export type NotificationPreferenceResponse = {
  available: boolean;
  message?: string;
  preference?: NotificationPreference;
};

export type CalendarItem = {
  type: string;
  label: string;
  isPrediction: boolean;
};

export type CalendarDay = {
  date: string;
  items: CalendarItem[];
};

export type CycleCalendar = {
  month: string;
  summary: {
    lastPeriodDate?: string | null;
    nextPeriodDate?: string | null;
    activePregnancy: boolean;
    estimatedDueDate?: string | null;
  };
  days: CalendarDay[];
};

export const plans: Record<PlanCode, {
  name: string;
  monthlyPrice: string;
  annualPrice: string;
  annualEquivalent: string;
  benefits: string[];
}> = {
  basico: {
    name: "Básico",
    monthlyPrice: "R$ 11,00/mês",
    annualPrice: "R$ 70,80/ano",
    annualEquivalent: "equivale a R$ 5,90/mês",
    benefits: [
      "Liberação da Luma no WhatsApp",
      "Registro de menstruação, sintomas, humor e relação sexual",
      "Histórico e calendário visual do ciclo",
      "Previsões estimadas de menstruação",
    ],
  },
  essencial: {
    name: "Essencial",
    monthlyPrice: "R$ 20,00/mês",
    annualPrice: "R$ 118,80/ano",
    annualEquivalent: "equivale a R$ 9,90/mês",
    benefits: [
      "Tudo do plano Básico",
      "Mensagens por áudio no WhatsApp",
      "Notificações automáticas e lembretes",
      "Imagens educativas do bebê e outros recursos visuais",
    ],
  },
};

export function getPlanCode(value: string | null | undefined): PlanCode {
  return value === "basico" || value === "essencial" ? value : "essencial";
}

export function getBillingInterval(value: string | null | undefined): BillingInterval {
  return value === "monthly" || value === "mensal" ? "monthly" : "annual";
}

export function getPlanPrice(planCode: PlanCode, billingInterval: BillingInterval) {
  const plan = plans[planCode];
  return billingInterval === "annual" ? plan.annualPrice : plan.monthlyPrice;
}

export function getBillingIntervalLabel(billingInterval: BillingInterval) {
  return billingInterval === "annual" ? "Anual" : "Mensal";
}

export async function registerAccount(input: {
  email: string;
  cpf: string;
  fullName: string;
  password: string;
  phoneNumber: string;
  dataConsentAccepted: boolean;
}) {
  return appRequest<{ user: AccountUser; phoneVerificationRequired?: boolean; phoneVerificationMessage?: string }>("/api/auth/register", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function resendPhoneVerificationCode() {
  return appRequest<{ message: string }>("/api/account/phone-verification/send", { method: "POST" });
}

export async function confirmPhoneVerificationCode(input: { code: string }) {
  return appRequest<{ message: string; user: AccountUser }>("/api/account/phone-verification/confirm", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function requestPhoneChange(input: { phoneNumber: string }) {
  return appRequest<{ message: string }>("/api/account/phone-change/request", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function confirmPhoneChange(input: { phoneNumber: string; code: string }) {
  return appRequest<{ message: string; user: AccountUser }>("/api/account/phone-change/confirm", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function loginAccount(input: { email: string; password: string }) {
  return appRequest<{ user: AccountUser }>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function logoutAccount() {
  return appRequest<{ ok: true }>("/api/auth/logout", { method: "POST" });
}

export async function getAccountProfile() {
  return appRequest<AccountProfile>("/api/account/me");
}

export async function getNotificationPreferences() {
  return appRequest<NotificationPreferenceResponse>("/api/account/notifications/preferences");
}

export async function updateNotificationPreferences(input: Partial<NotificationPreference>) {
  return appRequest<{ preference: NotificationPreference }>("/api/account/notifications/preferences", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function getAccountCalendar(month: string) {
  return appRequest<CycleCalendar>(`/api/account/calendar?month=${encodeURIComponent(month)}`);
}

export async function createStripeSubscription(input: { planCode: PlanCode; billingInterval: BillingInterval }) {
  return appRequest<StripeSubscriptionIntent>("/api/checkout/create-subscription", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function confirmStripeSubscription(input: {
  planCode: PlanCode;
  billingInterval: BillingInterval;
  stripeSubscriptionId: string;
  cardholderName?: string;
  billingCpf?: string;
}) {
  return appRequest<{ subscription: AccountSubscription }>("/api/checkout/confirm-subscription", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function cancelSubscription() {
  return appRequest<{ subscription: AccountSubscription }>("/api/account/subscription/cancel", {
    method: "POST",
  });
}

export async function resumeSubscription() {
  return appRequest<{ subscription: AccountSubscription }>("/api/account/subscription/resume", {
    method: "POST",
  });
}

export async function changeSubscriptionPlan(input: { planCode: PlanCode; billingInterval?: BillingInterval }) {
  return appRequest<{ subscription: AccountSubscription }>("/api/account/subscription/change-plan", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function createPaymentMethodSetupIntent() {
  return appRequest<StripeSetupIntent>("/api/account/payment-method/setup-intent", {
    method: "POST",
  });
}

export async function confirmPaymentMethod(input: {
  setupIntentId: string;
  cardholderName?: string;
  billingCpf?: string;
}) {
  return appRequest<{ ok: true }>("/api/account/payment-method/confirm", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function getBillingTransactions() {
  return appRequest<{ transactions: BillingTransaction[] }>("/api/account/billing/transactions");
}

async function appRequest<T>(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  headers.set("Content-Type", "application/json");

  const response = await fetch(path, {
    ...init,
    headers,
    credentials: "include",
  });

  if (response.status === 401) {
    throw new Error("Sessão expirada. Entre novamente para continuar.");
  }

  if (!response.ok) {
    let message = "Não consegui concluir essa ação agora.";
    try {
      const data = (await response.json()) as { message?: string };
      message = data.message || message;
    } catch {
      // Mantém a mensagem padrão.
    }
    throw new Error(message);
  }

  return (await response.json()) as T;
}
