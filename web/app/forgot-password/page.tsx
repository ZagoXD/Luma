import { ForgotPasswordPageTemplate } from "@/components/templates/ForgotPasswordPageTemplate";
import { privateMetadata } from "@/lib/seo";
export const metadata = privateMetadata("Recuperar acesso");

export default function ForgotPasswordPage() {
  return <ForgotPasswordPageTemplate />;
}
