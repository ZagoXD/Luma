import { ResetPasswordPageTemplate } from "@/components/templates/ResetPasswordPageTemplate";

type ResetPasswordPageProps = {
  searchParams: Promise<{ token?: string }>;
};

export default async function ResetPasswordPage({ searchParams }: ResetPasswordPageProps) {
  const params = await searchParams;
  return <ResetPasswordPageTemplate token={params.token || ""} />;
}
