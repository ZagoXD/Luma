import { ResetPasswordPageTemplate } from "@/components/templates/ResetPasswordPageTemplate";
import { privateMetadata } from "@/lib/seo";
export const metadata = privateMetadata("Definir nova senha");

type ResetPasswordPageProps = {
  searchParams: Promise<{ token?: string }>;
};

export default async function ResetPasswordPage({ searchParams }: ResetPasswordPageProps) {
  const params = await searchParams;
  return <ResetPasswordPageTemplate token={params.token || ""} />;
}
