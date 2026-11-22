export const waitlistConsentVersion = "2026-11-22";
export const waitlistLegalVersion = "2026-11-22";
export const waitlistSuccessMessage = "Você está na lista! O aviso de lançamento será enviado pelos canais escolhidos.";
const brazilAreaCodes = new Set([11,12,13,14,15,16,17,18,19,21,22,24,27,28,31,32,33,34,35,37,38,
  41,42,43,44,45,46,47,48,49,51,53,54,55,61,62,63,64,65,66,67,68,69,71,73,74,75,77,79,
  81,82,83,84,85,86,87,88,89,91,92,93,94,95,96,97,98,99]);
export function normalizeWaitlistPhone(value) {
  if (typeof value !== "string" || value.length > 40 || !/^[+\d\s().-]+$/.test(value)) return null;
  let digits = value.replace(/\D/g, "");
  if (digits.length === 13 && digits.startsWith("55")) digits = digits.slice(2);
  if (!/^\d{2}9\d{8}$/.test(digits) || !brazilAreaCodes.has(Number(digits.slice(0,2)))
    || /^(\d)\1+$/.test(digits.slice(2))) return null;
  return "+55" + digits;
}
export function validateWaitlist(input) {
  if (!input || typeof input !== "object" || Array.isArray(input)) return null;
  if (input.termsAccepted !== true || input.launchConsent !== true || input.version !== waitlistConsentVersion
    || input.termsVersion !== waitlistLegalVersion || input.privacyVersion !== waitlistLegalVersion
    || !["email", "whatsapp", "both"].includes(input.channel)) return null;
  const name = typeof input.name === "string" ? input.name.trim() : "";
  if (name.length < 2 || name.length > 100) return null;
  const emailOptIn = input.channel !== "whatsapp";
  const whatsappOptIn = input.channel !== "email";
  const email = emailOptIn && typeof input.email === "string" ? input.email.trim().toLowerCase() : null;
  const whatsapp = whatsappOptIn ? normalizeWaitlistPhone(input.whatsapp) : null;
  if (emailOptIn && (!email || email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))) return null;
  if (whatsappOptIn && !whatsapp) return null;
  return {name, email, whatsapp, channel:input.channel, termsAccepted:true, launchConsent:true, emailOptIn, whatsappOptIn,
    version:waitlistConsentVersion, termsVersion:waitlistLegalVersion, privacyVersion:waitlistLegalVersion};
}
