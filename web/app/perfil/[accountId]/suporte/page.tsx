import { SupportPageTemplate } from "@/components/templates/SupportPageTemplate";

type SupportPageProps = {
  params: Promise<{ accountId: string }>;
};

export default async function SupportPage({ params }: SupportPageProps) {
  const { accountId } = await params;
  return <SupportPageTemplate accountId={accountId} />;
}
