import { notFound } from "next/navigation";
import { CheckoutPageTemplate } from "@/components/templates/CheckoutPageTemplate";
import { getBillingInterval, type PlanCode } from "@/lib/luma-api";

type CheckoutPlanPageProps = {
  params: Promise<{ plan: string }>;
  searchParams: Promise<{ billing?: string; replace?: string }>;
};

export default async function CheckoutPlanPage({ params, searchParams }: CheckoutPlanPageProps) {
  const { plan } = await params;
  const { billing, replace } = await searchParams;
  if (plan !== "basico" && plan !== "essencial") {
    notFound();
  }

  return <CheckoutPageTemplate key={`${plan}-${getBillingInterval(billing)}-${replace || ""}`} planCode={plan as PlanCode} billingInterval={getBillingInterval(billing)} replacesSubscriptionId={replace} />;
}
