import { notFound } from "next/navigation";
import { CheckoutPageTemplate } from "@/components/templates/CheckoutPageTemplate";
import type { PlanCode } from "@/lib/luma-api";

type CheckoutPlanPageProps = {
  params: Promise<{ plan: string }>;
};

export default async function CheckoutPlanPage({ params }: CheckoutPlanPageProps) {
  const { plan } = await params;
  if (plan !== "basico" && plan !== "essencial") {
    notFound();
  }

  return <CheckoutPageTemplate planCode={plan as PlanCode} />;
}
