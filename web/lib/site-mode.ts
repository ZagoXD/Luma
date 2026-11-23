export const isWaitlist = process.env.NEXT_PUBLIC_LUMA_SITE_MODE === "waitlist";
export const waitlistPlanEvent = "luma:waitlist-plan";
export function purchasePath(plan: string, interval: string) {
  return isWaitlist ? "/#lista" : `/checkout/${plan}?billing=${interval}`;
}
