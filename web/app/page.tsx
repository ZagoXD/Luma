import { LandingPageTemplate } from "@/components/templates/LandingPageTemplate";
import { buildStructuredData, isIndexableSite, productionOrigin } from "@/lib/seo";
import { isWaitlist } from "@/lib/site-mode";

export const metadata = { alternates: { canonical: (isWaitlist ? "https://espera.ia-luma.com.br" : productionOrigin) + "/" } };

export default function Home() {
  return <>
    {isIndexableSite() && !isWaitlist && <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(buildStructuredData()).replace(/</g, "\\u003c") }} />}
    <LandingPageTemplate />
  </>;
}
