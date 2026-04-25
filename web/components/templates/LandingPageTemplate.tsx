import { LandingPageBehavior } from "@/components/behaviors/LandingPageBehavior";
import { BenefitsSection } from "@/components/organisms/BenefitsSection";
import { DevelopmentSection } from "@/components/organisms/DevelopmentSection";
import { Footer } from "@/components/organisms/Footer";
import { HeroSection } from "@/components/organisms/HeroSection";
import { HowItWorksSection } from "@/components/organisms/HowItWorksSection";
import { NavBar } from "@/components/organisms/NavBar";
import { PricingSection } from "@/components/organisms/PricingSection";
import { PrivacySection } from "@/components/organisms/PrivacySection";
import { ProblemSection } from "@/components/organisms/ProblemSection";
import { RegisterSection } from "@/components/organisms/RegisterSection";
import { SolutionSection } from "@/components/organisms/SolutionSection";
import { WaitlistSection } from "@/components/organisms/WaitlistSection";

export function LandingPageTemplate() {
  return (
    <>
      <LandingPageBehavior />
      <NavBar />
      <main>
        <HeroSection />
        <ProblemSection />
        <SolutionSection />
        <RegisterSection />
        <HowItWorksSection />
        <BenefitsSection />
        <PrivacySection />
        <PricingSection />
        <DevelopmentSection />
        <WaitlistSection />
      </main>
      <Footer />
    </>
  );
}
