import { headers } from "next/headers";
import { ProfilePageTemplate } from "@/components/templates/ProfilePageTemplate";

type ProfileAccountPageProps = {
  params: Promise<{ accountId: string }>;
};

export default async function ProfileAccountPage({ params }: ProfileAccountPageProps) {
  const { accountId } = await params;
  const host = (await headers()).get("host")?.toLowerCase();
  const sandboxNumber = host && host === process.env.LUMA_SANDBOX_HOST
    ? process.env.LUMA_SANDBOX_WHATSAPP_FROM?.replace(/^whatsapp:/, "")
    : undefined;
  return <ProfilePageTemplate accountId={accountId} whatsappNumber={sandboxNumber} />;
}
