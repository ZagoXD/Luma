import { LoginPageTemplate } from "@/components/templates/LoginPageTemplate";
import { privateMetadata } from "@/lib/seo";
export const metadata = privateMetadata("Entrar ou criar conta");

export default function LoginPage() {
  return <LoginPageTemplate />;
}
