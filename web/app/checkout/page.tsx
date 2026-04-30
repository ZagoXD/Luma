import { redirect } from "next/navigation";
import { getPlanCode } from "@/lib/luma-api";

type CheckoutRedirectPageProps = {
  searchParams: Promise<{ plan?: string }>;
};

export default async function CheckoutRedirectPage({ searchParams }: CheckoutRedirectPageProps) {
  const params = await searchParams;
  redirect(`/checkout/${getPlanCode(params.plan)}`);
}
