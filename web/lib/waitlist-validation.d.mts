export const waitlistConsentVersion: "2026-11-22";
export const waitlistLegalVersion: "2026-11-18";
export const waitlistSuccessMessage: string;
export type WaitlistChannel = "email" | "whatsapp" | "both";
export interface WaitlistInput {
  name: string;
  email: string | null;
  whatsapp: string | null;
  channel: WaitlistChannel;
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
