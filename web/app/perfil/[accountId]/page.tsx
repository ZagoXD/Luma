import { ProfilePageTemplate } from "@/components/templates/ProfilePageTemplate";

type ProfileAccountPageProps = {
  params: Promise<{ accountId: string }>;
};

export default async function ProfileAccountPage({ params }: ProfileAccountPageProps) {
  const { accountId } = await params;
  return <ProfilePageTemplate accountId={accountId} />;
}
