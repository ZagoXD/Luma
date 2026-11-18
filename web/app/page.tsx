import { LandingPageTemplate } from "@/components/templates/LandingPageTemplate";
import { buildStructuredData, isIndexableSite, productionOrigin } from "@/lib/seo";

export const metadata = { alternates: { canonical: productionOrigin + "/" } };

export default function Home() {
  return <>
    {isIndexableSite() && <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(buildStructuredData()).replace(/</g, "\\u003c") }} />}
    <LandingPageTemplate />
  </>;
}
