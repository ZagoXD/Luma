import { ProfileCalendarPageTemplate } from "@/components/templates/ProfileCalendarPageTemplate";

type ProfileCalendarPageProps = {
  params: Promise<{ accountId: string }>;
};

export default async function ProfileCalendarPage({ params }: ProfileCalendarPageProps) {
  const { accountId } = await params;
  return <ProfileCalendarPageTemplate accountId={accountId} />;
}
