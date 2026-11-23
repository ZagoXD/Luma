export const waitlistConsentVersion: "2026-11-23";
export const waitlistLegalVersion: "2026-11-22-v2";
export const waitlistSuccessMessage: string;
export type WaitlistChannel = "email" | "whatsapp" | "both";
export interface WaitlistInput {
  name: string;
  email: string | null;
  whatsapp: string | null;
  channel: WaitlistChannel;
  desiredPlan: "basico" | "essencial" | "lista-espera";
  termsAccepted: true;
  launchConsent: true;
  emailOptIn: boolean;
  whatsappOptIn: boolean;
  version: string;
  termsVersion: string;
  privacyVersion: string;
}
export function normalizeWaitlistPhone(value: unknown): string | null;
export function validateWaitlist(input: unknown): WaitlistInput | null;
