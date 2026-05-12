import { redirect } from "next/navigation";
import { getBillingInterval, getPlanCode } from "@/lib/luma-api";

type CheckoutRedirectPageProps = {
  searchParams: Promise<{ plan?: string; billing?: string }>;
};

export default async function CheckoutRedirectPage({ searchParams }: CheckoutRedirectPageProps) {
  const params = await searchParams;
  redirect(`/checkout/${getPlanCode(params.plan)}?billing=${getBillingInterval(params.billing)}`);
}
