export type PlanCode = "basico" | "essencial";

export type AccountUser = {
  id: string;
  email: string;
  cpf: string;
  fullName: string;
  phoneNumber: string;
  createdAt: string;
};

export type AccountSubscription = {
  id: string;
  planCode: PlanCode;
  planName: string;
  status: "active" | "canceled" | "pending";
  stripeSubscriptionId?: string | null;
  startsAt: string;
  currentPeriodEndsAt: string;
  canceledAt?: string | null;
};

export type StripeSubscriptionIntent = {
  publishableKey: string;
  clientSecret: string;
  stripeSubscriptionId: string;
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

export const plans: Record<PlanCode, { name: string; price: string; benefits: string[] }> = {
  basico: {
    name: "Básico",
    price: "R$ 5,90/mês",
    benefits: [
      "Liberação da Luma no WhatsApp",
      "Registro de menstruação, sintomas, humor e relação sexual",
      "Histórico básico do ciclo",
    ],
  },
  essencial: {
    name: "Essencial",
    price: "R$ 9,90/mês",
    benefits: [
      "Tudo do plano Básico",
      "Acompanhamento menstrual e de gravidez",
      "Respostas mais completas com contexto do seu histórico",
    ],
  },
};

export function getPlanCode(value: string | null | undefined): PlanCode {
  return value === "basico" || value === "essencial" ? value : "essencial";
}

export async function registerAccount(input: {
  email: string;
  cpf: string;
  fullName: string;
  password: string;
  phoneNumber: string;
}) {
  return appRequest<{ user: AccountUser }>("/api/auth/register", {
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

export async function createStripeSubscription(input: { planCode: PlanCode }) {
  return appRequest<StripeSubscriptionIntent>("/api/checkout/create-subscription", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export async function confirmStripeSubscription(input: { planCode: PlanCode; stripeSubscriptionId: string }) {
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
